using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ParentalTrack.Api.Modules.Devices;
using ParentalTrack.Api.Options;
using ParentalTrack.Domain;
using ParentalTrack.Domain.Entities;
using ParentalTrack.Domain.Enums;
using ParentalTrack.Infrastructure.Persistence;

namespace ParentalTrack.Api.Modules.History;

/// <summary>
/// Read side of the tracking data. Every query is filtered by the calling parent — a device owned
/// by somebody else is indistinguishable from one that does not exist.
/// </summary>
internal sealed class HistoryService
{
    /// <summary>
    /// Picks an even-stride sample entirely in the database. Only the sampled ids come back, so a
    /// 100k-point range never lands in server memory. <c>{0}</c> carries the optional accuracy
    /// filter, which is a fixed fragment, never user text.
    /// </summary>
    private const string SampleIdsSqlTemplate = """
        SELECT sampled.id
        FROM (
            SELECT id, (row_number() OVER (ORDER BY recorded_at, id) - 1) AS rn
            FROM location_records
            WHERE device_id = @device_id
              AND recorded_at >= @from_utc
              AND recorded_at <= @to_utc{0}
        ) sampled
        WHERE sampled.rn % @stride = 0 OR sampled.rn = @last_index
        ORDER BY sampled.rn
        """;

    private const string AccuracyFilterFragment = "\n              AND accuracy_meters <= @min_accuracy";

    private readonly AppDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly TrackingOptions _tracking;

    public HistoryService(AppDbContext db, IOptions<TrackingOptions> tracking, TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
        _tracking = tracking.Value;
    }

    public async Task<CurrentLocationResult> GetCurrentAsync(Guid parentId, Guid deviceId, CancellationToken ct)
    {
        var device = await _db.ChildDevices
            .AsNoTracking()
            .Where(d => d.Id == deviceId && d.ParentId == parentId)
            .Select(d => new { d.ChildName, d.LastSeenAt, d.LastLocationId })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (device is null)
        {
            return CurrentLocationResult.NotFound;
        }

        if (device.LastSeenAt is not { } lastSeenAt)
        {
            return CurrentLocationResult.NeverReported;
        }

        var record = await LoadLatestRecordAsync(deviceId, device.LastLocationId, ct).ConfigureAwait(false);
        var now = _timeProvider.GetUtcNow();
        var status = DeviceStatusCalculator.Evaluate(
            lastSeenAt,
            now,
            _tracking.OnlineThresholdSeconds,
            _tracking.StaleThresholdSeconds);

        return CurrentLocationResult.Found(new LocationSnapshotDto(
            deviceId,
            device.ChildName,
            status,
            status is DeviceStatus.Offline or DeviceStatus.NeverReported,
            SecondsSince(lastSeenAt, now),
            now,
            record is null ? null : LocationPointDto.FromEntity(record)));
    }

    /// <summary>Returns <c>null</c> when the device is not one of this parent's devices.</summary>
    public async Task<LocationHistoryResponse?> GetHistoryAsync(
        Guid parentId,
        Guid deviceId,
        HistoryQuery query,
        CancellationToken ct)
    {
        var device = await _db.ChildDevices
            .AsNoTracking()
            .Where(d => d.Id == deviceId && d.ParentId == parentId)
            .Select(d => new { d.ChildName })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (device is null)
        {
            return null;
        }

        var matching = FilterRecords(deviceId, query);
        var totalMatched = await matching.CountAsync(ct).ConfigureAwait(false);

        // Sampling only makes sense once the range holds more points than the caller will take, and
        // it needs at least two slots to keep both the first and the last fix.
        var simplified = query.Simplify && query.Limit >= 2 && totalMatched > query.Limit;

        var records = simplified
            ? await LoadSampledAsync(deviceId, query, totalMatched, ct).ConfigureAwait(false)
            : await LoadWindowAsync(matching, query, ct).ConfigureAwait(false);

        var points = records.Select(LocationPointDto.FromEntity).ToList();

        return new LocationHistoryResponse(
            deviceId,
            device.ChildName,
            query.FromUtc,
            query.ToUtc,
            points.Count,
            totalMatched,
            simplified,
            DistanceMeters(points),
            points);
    }

    private IQueryable<LocationRecord> FilterRecords(Guid deviceId, HistoryQuery query)
    {
        var records = _db.LocationRecords
            .AsNoTracking()
            .Where(r => r.DeviceId == deviceId && r.RecordedAt >= query.FromUtc && r.RecordedAt <= query.ToUtc);

        if (query.MinAccuracyMeters is { } minAccuracy)
        {
            records = records.Where(r => r.AccuracyMeters <= minAccuracy);
        }

        return records;
    }

    private static async Task<List<LocationRecord>> LoadWindowAsync(
        IQueryable<LocationRecord> matching,
        HistoryQuery query,
        CancellationToken ct)
    {
        var ordered = query.Descending
            ? matching.OrderByDescending(r => r.RecordedAt).ThenByDescending(r => r.Id)
            : matching.OrderBy(r => r.RecordedAt).ThenBy(r => r.Id);

        return await ordered.Take(query.Limit).ToListAsync(ct).ConfigureAwait(false);
    }

    private async Task<List<LocationRecord>> LoadSampledAsync(
        Guid deviceId,
        HistoryQuery query,
        int totalMatched,
        CancellationToken ct)
    {
        var lastIndex = (long)totalMatched - 1;

        // Stride chosen so the multiples of it, plus the final row, never exceed the limit.
        var stride = (long)Math.Ceiling((double)lastIndex / (query.Limit - 1));

        var ids = await LoadSampleIdsAsync(deviceId, query, stride, lastIndex, ct).ConfigureAwait(false);
        if (ids.Count == 0)
        {
            return [];
        }

        var sampled = _db.LocationRecords
            .AsNoTracking()
            .Where(r => r.DeviceId == deviceId && ids.Contains(r.Id));

        var ordered = query.Descending
            ? sampled.OrderByDescending(r => r.RecordedAt).ThenByDescending(r => r.Id)
            : sampled.OrderBy(r => r.RecordedAt).ThenBy(r => r.Id);

        return await ordered.ToListAsync(ct).ConfigureAwait(false);
    }

    private async Task<List<long>> LoadSampleIdsAsync(
        Guid deviceId,
        HistoryQuery query,
        long stride,
        long lastIndex,
        CancellationToken ct)
    {
        var sql = string.Format(
            CultureInfo.InvariantCulture,
            SampleIdsSqlTemplate,
            query.MinAccuracyMeters is null ? string.Empty : AccuracyFilterFragment);

        var ids = new List<long>(query.Limit);

        await _db.Database.OpenConnectionAsync(ct).ConfigureAwait(false);
        try
        {
            await using var command = _db.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            AddParameter(command, "device_id", deviceId);
            AddParameter(command, "from_utc", query.FromUtc.ToUniversalTime());
            AddParameter(command, "to_utc", query.ToUtc.ToUniversalTime());
            AddParameter(command, "stride", stride);
            AddParameter(command, "last_index", lastIndex);

            if (query.MinAccuracyMeters is { } minAccuracy)
            {
                AddParameter(command, "min_accuracy", minAccuracy);
            }

            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                ids.Add(reader.GetInt64(0));
            }
        }
        finally
        {
            await _db.Database.CloseConnectionAsync().ConfigureAwait(false);
        }

        return ids;
    }

    private async Task<LocationRecord?> LoadLatestRecordAsync(Guid deviceId, long? lastLocationId, CancellationToken ct)
    {
        if (lastLocationId is { } id)
        {
            var pointed = await _db.LocationRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == id && r.DeviceId == deviceId, ct)
                .ConfigureAwait(false);

            if (pointed is not null)
            {
                return pointed;
            }
        }

        // The denormalised pointer can lag a retention pass or predate a device it was moved from;
        // the (device_id, recorded_at DESC) index makes the fallback cheap.
        return await _db.LocationRecords
            .AsNoTracking()
            .Where(r => r.DeviceId == deviceId)
            .OrderByDescending(r => r.RecordedAt)
            .ThenByDescending(r => r.Id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Haversine sum along the track. The points are a contiguous run of the same fixes either way,
    /// and each leg is symmetric, so this is the ascending-time path length whichever order the
    /// caller asked for.
    /// </summary>
    private static double DistanceMeters(IReadOnlyList<LocationPointDto> points)
    {
        if (points.Count < 2)
        {
            return 0;
        }

        var total = 0d;
        for (var i = 1; i < points.Count; i++)
        {
            var previous = points[i - 1];
            var current = points[i];
            total += GeoMath.HaversineMeters(
                previous.Latitude,
                previous.Longitude,
                current.Latitude,
                current.Longitude);
        }

        return Math.Round(total, 1);
    }

    private static long SecondsSince(DateTimeOffset lastSeenAt, DateTimeOffset now) =>
        (long)Math.Max(0, (now - lastSeenAt).TotalSeconds);

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}

using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ParentalTrack.Api.Options;
using ParentalTrack.Domain.Entities;
using ParentalTrack.Infrastructure.Persistence;

namespace ParentalTrack.Api.Modules.Ingestion;

/// <summary>
/// Drains <see cref="LocationIngestQueue"/> and persists fixes. Requests are answered 202 before
/// this runs, so the loop is the only thing standing between a device and lost data: every
/// iteration is guarded and the worker never exits on an error.
/// </summary>
internal sealed class LocationIngestWorker : BackgroundService
{
    private const int MaxWriteAttempts = 2;
    private static readonly TimeSpan WriteRetryDelay = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Inserts the batch and folds the newest accepted fix per device into <c>child_devices</c> in
    /// one round trip. <c>ON CONFLICT DO NOTHING</c> makes a replayed batch a no-op rather than an
    /// exception, and the guards on <c>last_seen_at</c> keep an out-of-order batch from rewinding a
    /// device that has already reported something newer.
    /// </summary>
    private const string PersistSql = """
        WITH input AS (
            SELECT *
            FROM unnest(
                @device_ids::uuid[], @client_ids::uuid[],
                @latitudes::double precision[], @longitudes::double precision[],
                @accuracies::double precision[], @altitudes::double precision[],
                @speeds::double precision[], @bearings::double precision[],
                @batteries::int[], @charging::boolean[], @providers::smallint[],
                @recorded_at::timestamptz[], @received_at::timestamptz[])
            AS t(device_id, client_id, latitude, longitude, accuracy_meters, altitude_meters,
                 speed_mps, bearing_degrees, battery_percent, is_charging, provider,
                 recorded_at, received_at)
        ),
        known AS (
            SELECT i.*
            FROM input i
            WHERE EXISTS (SELECT 1 FROM child_devices d WHERE d.id = i.device_id)
        ),
        inserted AS (
            INSERT INTO location_records (
                device_id, client_id, latitude, longitude, accuracy_meters, altitude_meters,
                speed_mps, bearing_degrees, battery_percent, is_charging, provider,
                recorded_at, received_at)
            SELECT device_id, client_id, latitude, longitude, accuracy_meters, altitude_meters,
                   speed_mps, bearing_degrees, battery_percent, is_charging, provider,
                   recorded_at, received_at
            FROM known
            ON CONFLICT (device_id, client_id) DO NOTHING
            RETURNING id, device_id, battery_percent, recorded_at
        ),
        newest AS (
            SELECT DISTINCT ON (device_id) device_id, id, battery_percent, recorded_at
            FROM inserted
            ORDER BY device_id, recorded_at DESC, id DESC
        ),
        touched AS (
            UPDATE child_devices d
            SET last_seen_at = GREATEST(d.last_seen_at, n.recorded_at),
                last_battery_percent = CASE
                    WHEN d.last_seen_at IS NULL OR n.recorded_at >= d.last_seen_at
                    THEN COALESCE(n.battery_percent, d.last_battery_percent)
                    ELSE d.last_battery_percent END,
                last_location_id = CASE
                    WHEN d.last_seen_at IS NULL OR n.recorded_at >= d.last_seen_at
                    THEN n.id
                    ELSE d.last_location_id END
            FROM newest n
            WHERE d.id = n.device_id
            RETURNING d.id
        )
        SELECT (SELECT count(*) FROM inserted), (SELECT count(*) FROM touched)
        """;

    private readonly LocationIngestQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<LocationIngestWorker> _logger;
    private readonly int _writeBatchSize;
    private readonly TimeSpan _coalesceWindow;

    public LocationIngestWorker(
        LocationIngestQueue queue,
        IServiceScopeFactory scopeFactory,
        IOptions<IngestionOptions> options,
        TimeProvider timeProvider,
        ILogger<LocationIngestWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
        _writeBatchSize = Math.Max(1, options.Value.WriteBatchSize);
        _coalesceWindow = TimeSpan.FromMilliseconds(Math.Max(0, options.Value.FlushIntervalMilliseconds));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var buffer = new List<LocationRecord>(_writeBatchSize);
        await using var enumerator = _queue.ReadAllAsync(stoppingToken).GetAsyncEnumerator(stoppingToken);

        // A MoveNextAsync that outlived the coalescing window is carried over instead of abandoned,
        // otherwise the batch it is about to yield would be lost.
        Task<bool>? pendingMove = null;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var move = pendingMove ?? enumerator.MoveNextAsync().AsTask();
                pendingMove = null;

                if (!await move.ConfigureAwait(false))
                {
                    return;
                }

                buffer.AddRange(enumerator.Current);
                pendingMove = await CoalesceAsync(enumerator, buffer, stoppingToken).ConfigureAwait(false);
                await PersistAsync(buffer, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Ingest worker iteration failed; {PointCount} location points were dropped.",
                    buffer.Count);
            }
            finally
            {
                buffer.Clear();
            }
        }
    }

    /// <summary>
    /// Pulls further queued batches into <paramref name="buffer"/> until it holds a full write
    /// batch or the coalescing window elapses. Returns the still-running read, if any.
    /// </summary>
    private async Task<Task<bool>?> CoalesceAsync(
        IAsyncEnumerator<IReadOnlyList<LocationRecord>> enumerator,
        List<LocationRecord> buffer,
        CancellationToken ct)
    {
        if (buffer.Count >= _writeBatchSize)
        {
            return null;
        }

        using var window = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var windowElapsed = Task.Delay(_coalesceWindow, _timeProvider, window.Token);

        while (buffer.Count < _writeBatchSize)
        {
            var move = enumerator.MoveNextAsync().AsTask();

            if (!move.IsCompleted)
            {
                var first = await Task.WhenAny(move, windowElapsed).ConfigureAwait(false);
                if (!ReferenceEquals(first, move))
                {
                    window.Cancel();
                    return move;
                }
            }

            if (!await move.ConfigureAwait(false))
            {
                break;
            }

            buffer.AddRange(enumerator.Current);
        }

        window.Cancel();
        return null;
    }

    private async Task PersistAsync(List<LocationRecord> buffer, CancellationToken ct)
    {
        if (buffer.Count == 0)
        {
            return;
        }

        // Two requests can carry the same clientId; collapse them here so a single statement never
        // conflicts with itself.
        var records = Deduplicate(buffer);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var (accepted, devices) = await WriteAsync(records, ct).ConfigureAwait(false);
                _logger.LogDebug(
                    "Persisted {AcceptedCount} of {SubmittedCount} location points across {DeviceCount} devices.",
                    accepted,
                    records.Count,
                    devices);
                return;
            }
            catch (Exception ex) when (attempt < MaxWriteAttempts && !ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Location write attempt {Attempt} failed; retrying once.", attempt);
                await Task.Delay(WriteRetryDelay, _timeProvider, ct).ConfigureAwait(false);
            }
        }
    }

    private async Task<(long Accepted, long TouchedDevices)> WriteAsync(
        IReadOnlyList<LocationRecord> records,
        CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.Database.OpenConnectionAsync(ct).ConfigureAwait(false);
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = PersistSql;

            AddArray(command, "device_ids", records.Select(r => r.DeviceId).ToArray());
            AddArray(command, "client_ids", records.Select(r => r.ClientId).ToArray());
            AddArray(command, "latitudes", records.Select(r => r.Latitude).ToArray());
            AddArray(command, "longitudes", records.Select(r => r.Longitude).ToArray());
            AddArray(command, "accuracies", records.Select(r => r.AccuracyMeters).ToArray());
            AddArray(command, "altitudes", records.Select(r => r.AltitudeMeters).ToArray());
            AddArray(command, "speeds", records.Select(r => r.SpeedMetersPerSecond).ToArray());
            AddArray(command, "bearings", records.Select(r => r.BearingDegrees).ToArray());
            AddArray(command, "batteries", records.Select(r => r.BatteryPercent).ToArray());
            AddArray(command, "charging", records.Select(r => r.IsCharging).ToArray());
            AddArray(command, "providers", records.Select(r => (short)r.Provider).ToArray());
            AddArray(command, "recorded_at", records.Select(r => r.RecordedAt.ToUniversalTime()).ToArray());
            AddArray(command, "received_at", records.Select(r => r.ReceivedAt.ToUniversalTime()).ToArray());

            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                return (0, 0);
            }

            return (reader.GetInt64(0), reader.GetInt64(1));
        }
        finally
        {
            await db.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }

    private static List<LocationRecord> Deduplicate(IReadOnlyList<LocationRecord> records)
    {
        var seen = new HashSet<(Guid DeviceId, Guid ClientId)>(records.Count);
        var unique = new List<LocationRecord>(records.Count);

        foreach (var record in records)
        {
            if (seen.Add((record.DeviceId, record.ClientId)))
            {
                unique.Add(record);
            }
        }

        return unique;
    }

    private static void AddArray<T>(DbCommand command, string name, T[] values)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = values;
        command.Parameters.Add(parameter);
    }
}

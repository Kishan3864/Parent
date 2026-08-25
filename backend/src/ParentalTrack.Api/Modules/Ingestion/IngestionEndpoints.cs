using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ParentalTrack.Api.Common;
using ParentalTrack.Api.Options;
using ParentalTrack.Api.Security;
using ParentalTrack.Domain.Entities;
using ParentalTrack.Domain.Enums;
using ParentalTrack.Infrastructure.Persistence;

namespace ParentalTrack.Api.Modules.Ingestion;

/// <summary>
/// The device-facing write path. It does nothing but validate and queue: the database write happens
/// in <see cref="LocationIngestWorker"/> so a slow database never stalls a phone on a mobile link.
/// </summary>
internal static class IngestionEndpoints
{
    private const double MinLatitude = -90;
    private const double MaxLatitude = 90;
    private const double MinLongitude = -180;
    private const double MaxLongitude = 180;
    private const double MaxAccuracyMeters = 10_000;

    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);
    private static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(5);

    internal static void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/ingest")
            .WithTags("Ingestion")
            .RequireAuthorization(AuthConstants.DevicePolicy)
            .RequireRateLimiting(AuthConstants.IngestRateLimit);

        group.MapPost("/locations", IngestLocationsAsync)
            .WithName("IngestLocations")
            .WithSummary("Queue a batch of location fixes recorded by a child device.")
            .Produces<IngestResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<IResult> IngestLocationsAsync(
        IngestRequest? request,
        ClaimsPrincipal user,
        LocationIngestQueue queue,
        AppDbContext db,
        IOptions<IngestionOptions> options,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var points = request?.Points;
        if (points is null || points.Count == 0)
        {
            return ApiResults.BadRequest(
                "Empty batch",
                "At least one location point is required.");
        }

        var maxBatchSize = Math.Max(1, options.Value.MaxBatchSize);
        if (points.Count > maxBatchSize)
        {
            return ApiResults.BadRequest(
                "Batch too large",
                $"A batch may contain at most {maxBatchSize} points; {points.Count} were sent.");
        }

        var deviceId = user.GetDeviceId();
        var now = timeProvider.GetUtcNow();
        var oldestAccepted = now - MaxAge;
        var newestAccepted = now + MaxClockSkew;

        var records = new List<LocationRecord>(points.Count);
        var clientIds = new HashSet<Guid>(points.Count);
        var duplicates = 0;
        var rejected = 0;

        foreach (var point in points)
        {
            // One unusable point must never cost the device the rest of the batch — it would
            // resend the same batch forever.
            if (point is null || !IsValid(point, oldestAccepted, newestAccepted))
            {
                rejected++;
                continue;
            }

            if (!clientIds.Add(point.ClientId))
            {
                duplicates++;
                continue;
            }

            records.Add(ToRecord(point, deviceId, now));
        }

        // Contract §2.4 counts a point whose (device_id, client_id) pair is already stored as a
        // duplicate, not as accepted. The unique index in LocationIngestWorker still discards a
        // replay that slips past this check (a batch still sitting in the channel, or a concurrent
        // request), so this only has to be right, not atomic.
        if (records.Count > 0)
        {
            var alreadyStored = await LoadStoredClientIdsAsync(db, deviceId, records, loggerFactory, ct);
            if (alreadyStored.Count > 0)
            {
                duplicates += records.RemoveAll(record => alreadyStored.Contains(record.ClientId));
            }
        }

        if (records.Count > 0 && !await queue.EnqueueAsync(records, ct))
        {
            return ApiResults.Problem(
                StatusCodes.Status503ServiceUnavailable,
                "Ingest queue saturated",
                "The server could not accept the batch in time. Retry with the same points.");
        }

        return TypedResults.Accepted(
            (string?)null,
            new IngestResponse(records.Count, duplicates, rejected, now));
    }

    /// <summary>
    /// The <c>clientId</c>s of <paramref name="records"/> that this device has already stored.
    /// A failure here is deliberately swallowed: the write path is the queue plus the unique index,
    /// so a database that is unreachable for this read must cost the batch its duplicate count, not
    /// its 202 — the device would otherwise resend points the writer will discard anyway.
    /// </summary>
    private static async Task<HashSet<Guid>> LoadStoredClientIdsAsync(
        AppDbContext db,
        Guid deviceId,
        List<LocationRecord> records,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var clientIds = records.Select(record => record.ClientId).ToArray();
        try
        {
            var stored = await db.LocationRecords
                .AsNoTracking()
                .Where(record => record.DeviceId == deviceId && clientIds.Contains(record.ClientId))
                .Select(record => record.ClientId)
                .ToListAsync(ct);

            return [.. stored];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            loggerFactory
                .CreateLogger(typeof(IngestionEndpoints).FullName!)
                .LogWarning(ex,
                    "Could not look up stored clientIds for device {DeviceId}; the batch is queued " +
                    "without a stored-duplicate count.",
                    deviceId);
            return [];
        }
    }

    private static bool IsValid(IngestPointDto point, DateTimeOffset oldestAccepted, DateTimeOffset newestAccepted) =>
        point.ClientId != Guid.Empty
        && point.Latitude >= MinLatitude && point.Latitude <= MaxLatitude
        && point.Longitude >= MinLongitude && point.Longitude <= MaxLongitude
        && point.AccuracyMeters >= 0 && point.AccuracyMeters <= MaxAccuracyMeters
        && point.BatteryPercent is null or (>= 0 and <= 100)
        && point.RecordedAt >= oldestAccepted
        && point.RecordedAt <= newestAccepted;

    private static LocationRecord ToRecord(IngestPointDto point, Guid deviceId, DateTimeOffset receivedAt) => new()
    {
        DeviceId = deviceId,
        ClientId = point.ClientId,
        Latitude = point.Latitude,
        Longitude = point.Longitude,
        AccuracyMeters = point.AccuracyMeters,
        AltitudeMeters = point.AltitudeMeters,
        SpeedMetersPerSecond = point.SpeedMetersPerSecond,
        BearingDegrees = point.BearingDegrees,
        BatteryPercent = point.BatteryPercent,
        IsCharging = point.IsCharging,
        Provider = ParseProvider(point.Provider),
        RecordedAt = point.RecordedAt.ToUniversalTime(),
        ReceivedAt = receivedAt,
    };

    /// <summary>
    /// Unknown or missing provider names degrade to <see cref="LocationProvider.Unknown"/>: a
    /// newer app build must not have its fixes thrown away over a label.
    /// </summary>
    private static LocationProvider ParseProvider(string? value) =>
        Enum.TryParse<LocationProvider>(value, ignoreCase: true, out var provider) && Enum.IsDefined(provider)
            ? provider
            : LocationProvider.Unknown;
}

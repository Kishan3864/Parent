using ParentalTrack.Api.Modules.Devices;
using ParentalTrack.Domain.Enums;

namespace ParentalTrack.Api.Modules.History;

/// <summary>
/// Where a child device is right now, with the freshness the server computed. The UI never derives
/// staleness itself — it renders <paramref name="Status"/> and re-ticks
/// <paramref name="SecondsSinceUpdate"/> against the thresholds from <c>GET /api/v1/config</c>.
/// </summary>
public sealed record LocationSnapshotDto(
    Guid DeviceId,
    string ChildName,
    DeviceStatus Status,
    bool IsStale,
    long SecondsSinceUpdate,
    DateTimeOffset ServerTimeUtc,
    LocationPointDto? Location);

/// <summary>
/// A slice of a device's track. <paramref name="Count"/> is what came back,
/// <paramref name="TotalMatched"/> what the range actually holds — they differ when the range was
/// capped by <c>limit</c> or downsampled, which <paramref name="Simplified"/> reports.
/// </summary>
public sealed record LocationHistoryResponse(
    Guid DeviceId,
    string ChildName,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    int Count,
    int TotalMatched,
    bool Simplified,
    double DistanceMeters,
    IReadOnlyList<LocationPointDto> Points);

/// <summary>Client-visible tuning values, so nothing about staleness is hard-coded in the UI.</summary>
public sealed record AppConfigDto(
    int OnlineThresholdSeconds,
    int StaleThresholdSeconds,
    int DefaultRefreshSeconds,
    string MapTileUrl,
    string MapAttribution);

/// <summary>A history request after the endpoint has validated and defaulted every parameter.</summary>
internal sealed record HistoryQuery(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    int Limit,
    bool Descending,
    double? MinAccuracyMeters,
    bool Simplify);

internal enum CurrentLocationOutcome
{
    /// <summary>No such device, or it belongs to another parent — both answer 404.</summary>
    NotFound = 0,

    /// <summary>The device exists but has never reported a fix — answers 204.</summary>
    NeverReported = 1,

    Found = 2,
}

internal readonly record struct CurrentLocationResult(CurrentLocationOutcome Outcome, LocationSnapshotDto? Snapshot)
{
    internal static CurrentLocationResult NotFound { get; } = new(CurrentLocationOutcome.NotFound, null);

    internal static CurrentLocationResult NeverReported { get; } = new(CurrentLocationOutcome.NeverReported, null);

    internal static CurrentLocationResult Found(LocationSnapshotDto snapshot) =>
        new(CurrentLocationOutcome.Found, snapshot);
}

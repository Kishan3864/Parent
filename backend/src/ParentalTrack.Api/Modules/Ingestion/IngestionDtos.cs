namespace ParentalTrack.Api.Modules.Ingestion;

/// <summary>
/// A batch upload from a child device. The app flushes its offline queue in one request, so a
/// single malformed point must never cost the device the whole batch (contract §2.4).
/// </summary>
public sealed record IngestRequest(IReadOnlyList<IngestPointDto>? Points);

/// <summary>
/// One fix as the device recorded it. <paramref name="Provider"/> is carried as a string rather
/// than the enum on purpose: an unknown provider name from a newer app build degrades to
/// <c>unknown</c> instead of failing deserialisation of the entire batch.
/// </summary>
public sealed record IngestPointDto(
    Guid ClientId,
    double Latitude,
    double Longitude,
    double AccuracyMeters,
    double? AltitudeMeters,
    double? SpeedMetersPerSecond,
    double? BearingDegrees,
    int? BatteryPercent,
    bool? IsCharging,
    string? Provider,
    DateTimeOffset RecordedAt);

/// <summary>
/// Outcome of a batch. <paramref name="Accepted"/> counts the points queued for persistence,
/// <paramref name="Duplicates"/> the points whose <c>(deviceId, clientId)</c> pair repeated — inside
/// this request or against what the device has already stored (contract §2.4) — and
/// <paramref name="Rejected"/> the points that failed validation. A replay that still slips past the
/// check (a batch not yet drained from the channel) is discarded by the writer's unique index
/// without error; the device may delete every point it sent as soon as it sees the 202.
/// </summary>
public sealed record IngestResponse(int Accepted, int Duplicates, int Rejected, DateTimeOffset ServerTimeUtc);

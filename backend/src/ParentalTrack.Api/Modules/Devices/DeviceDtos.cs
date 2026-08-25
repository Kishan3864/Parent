using ParentalTrack.Api.Options;
using ParentalTrack.Domain.Entities;
using ParentalTrack.Domain.Enums;

namespace ParentalTrack.Api.Modules.Devices;

/// <summary>
/// A single stored fix. Owned by the Devices module and reused by the History and Ingestion
/// modules — do not redeclare it elsewhere (contract §9).
/// </summary>
public sealed record LocationPointDto(
    long Id,
    double Latitude,
    double Longitude,
    double AccuracyMeters,
    double? AltitudeMeters,
    double? SpeedMetersPerSecond,
    double? BearingDegrees,
    int? BatteryPercent,
    bool? IsCharging,
    LocationProvider Provider,
    DateTimeOffset RecordedAt,
    DateTimeOffset ReceivedAt)
{
    public static LocationPointDto FromEntity(LocationRecord record) => new(
        record.Id,
        record.Latitude,
        record.Longitude,
        record.AccuracyMeters,
        record.AltitudeMeters,
        record.SpeedMetersPerSecond,
        record.BearingDegrees,
        record.BatteryPercent,
        record.IsCharging,
        record.Provider,
        record.RecordedAt,
        record.ReceivedAt);
}

/// <summary>
/// Tracking parameters handed to the child device at enrollment so the app never hard-codes them.
/// Owned by the Devices module (contract §9).
/// </summary>
public sealed record TrackingConfigDto(
    int IntervalSeconds,
    int FastestIntervalSeconds,
    int MinDistanceMeters,
    int BatchMaxSize,
    int UploadIntervalSeconds)
{
    public static TrackingConfigDto FromOptions(TrackingOptions options) => new(
        options.IntervalSeconds,
        options.FastestIntervalSeconds,
        options.MinDistanceMeters,
        options.BatchMaxSize,
        options.UploadIntervalSeconds);
}

/// <summary>Row shown in the parent's device list.</summary>
public sealed record DeviceSummaryDto(
    Guid Id,
    string ChildName,
    string? DeviceLabel,
    string? Platform,
    string? Model,
    bool IsActive,
    bool IsPaired,
    bool HasActiveSession,
    DeviceStatus Status,
    bool IsStale,
    DateTimeOffset? LastSeenAt,
    long? SecondsSinceUpdate,
    int? BatteryPercent,
    LocationPointDto? LastLocation);

/// <summary>
/// Everything the summary carries plus enrollment detail. <paramref name="PairingCode"/> is only
/// ever non-null on the response that issued it — it is never read back from storage.
/// </summary>
public sealed record DeviceDetailDto(
    Guid Id,
    string ChildName,
    string? DeviceLabel,
    string? Platform,
    string? Model,
    bool IsActive,
    bool IsPaired,
    bool HasActiveSession,
    DeviceStatus Status,
    bool IsStale,
    DateTimeOffset? LastSeenAt,
    long? SecondsSinceUpdate,
    int? BatteryPercent,
    LocationPointDto? LastLocation,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PairedAt,
    string? PairingCode,
    DateTimeOffset? PairingCodeExpiresAtUtc,
    string? AppVersion,
    string? OsVersion);

public sealed record CreateDeviceRequest(string? ChildName, string? DeviceLabel);

/// <summary>
/// Partial update: a null member leaves the stored value untouched. Send an empty string for
/// <paramref name="DeviceLabel"/> to clear it.
/// </summary>
public sealed record UpdateDeviceRequest(string? ChildName, string? DeviceLabel, bool? IsActive);

public sealed record PairingCodeDto(string PairingCode, DateTimeOffset ExpiresAtUtc);

public sealed record EnrollRequest(
    string? PairingCode,
    string? InstallId,
    string? Manufacturer,
    string? Model,
    string? OsVersion,
    string? AppVersion);

public sealed record EnrollResponse(
    Guid DeviceId,
    string ChildName,
    string DeviceToken,
    DateTimeOffset TokenExpiresAtUtc,
    TrackingConfigDto Tracking);

public sealed record DeviceSelfDto(
    Guid DeviceId,
    string ChildName,
    bool IsActive,
    TrackingConfigDto Tracking);

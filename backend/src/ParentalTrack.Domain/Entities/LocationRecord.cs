using ParentalTrack.Domain.Enums;

namespace ParentalTrack.Domain.Entities;

/// <summary>
/// A single location fix uploaded by a child device. Maps to table <c>location_records</c>.
/// </summary>
public sealed class LocationRecord
{
    /// <summary>Primary key, database generated (bigserial).</summary>
    public long Id { get; set; }

    /// <summary>Device that produced the fix. Cascade delete.</summary>
    public Guid DeviceId { get; set; }

    /// <summary>Idempotency key generated on the device; unique together with <see cref="DeviceId"/>.</summary>
    public Guid ClientId { get; set; }

    /// <summary>Latitude in degrees, -90..90.</summary>
    public double Latitude { get; set; }

    /// <summary>Longitude in degrees, -180..180.</summary>
    public double Longitude { get; set; }

    /// <summary>Horizontal accuracy radius in metres.</summary>
    public double AccuracyMeters { get; set; }

    /// <summary>Altitude in metres, when the provider supplied one.</summary>
    public double? AltitudeMeters { get; set; }

    /// <summary>Ground speed in metres per second, when the provider supplied one.</summary>
    public double? SpeedMetersPerSecond { get; set; }

    /// <summary>Bearing in degrees, when the provider supplied one.</summary>
    public double? BearingDegrees { get; set; }

    /// <summary>Battery level 0..100 at the time of the fix.</summary>
    public int? BatteryPercent { get; set; }

    /// <summary>Whether the device was charging at the time of the fix.</summary>
    public bool? IsCharging { get; set; }

    /// <summary>Provider that produced the fix. Stored as <c>smallint</c>.</summary>
    public LocationProvider Provider { get; set; }

    /// <summary>Device clock: when the fix was taken.</summary>
    public DateTimeOffset RecordedAt { get; set; }

    /// <summary>Server clock: when the fix was accepted for writing.</summary>
    public DateTimeOffset ReceivedAt { get; set; }
}

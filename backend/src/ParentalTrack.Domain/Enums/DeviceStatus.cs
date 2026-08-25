namespace ParentalTrack.Domain.Enums;

/// <summary>
/// Freshness of a child device, computed server-side by
/// <see cref="ParentalTrack.Domain.DeviceStatusCalculator"/>. Serialised on the wire as a camelCase
/// string ("neverReported", "online", "idle", "offline").
/// </summary>
public enum DeviceStatus
{
    /// <summary>The device has never delivered a location fix.</summary>
    NeverReported = 0,

    /// <summary>Seen within the online threshold.</summary>
    Online = 1,

    /// <summary>Seen within the stale threshold but not within the online threshold.</summary>
    Idle = 2,

    /// <summary>Not seen within the stale threshold; the last known fix is stale.</summary>
    Offline = 3
}

namespace ParentalTrack.Domain.Enums;

/// <summary>
/// Source of a location fix as reported by the child device. Persisted as <c>smallint</c>;
/// serialised on the wire as a camelCase string ("unknown", "gps", "network", "fused", "passive").
/// </summary>
public enum LocationProvider : short
{
    /// <summary>The device could not attribute the fix to a known provider.</summary>
    Unknown = 0,

    /// <summary>GNSS/GPS hardware fix.</summary>
    Gps = 1,

    /// <summary>Cell tower or Wi-Fi derived fix.</summary>
    Network = 2,

    /// <summary>Google Play services fused provider.</summary>
    Fused = 3,

    /// <summary>Fix opportunistically received from another application's request.</summary>
    Passive = 4
}

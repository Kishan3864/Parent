using ParentalTrack.Domain.Enums;

namespace ParentalTrack.Domain;

/// <summary>
/// The single definition of device freshness. Every read path (device list, device detail, current
/// location, history) calls this so the admin UI never has to re-derive thresholds.
/// </summary>
public static class DeviceStatusCalculator
{
    /// <summary>
    /// Classifies a device from the age of its most recent accepted fix.
    /// </summary>
    /// <param name="lastSeenAt">Greatest accepted <c>recordedAt</c>, or null if the device never reported.</param>
    /// <param name="now">Current server time.</param>
    /// <param name="onlineThresholdSeconds">Age at or below which the device counts as online (default 180).</param>
    /// <param name="staleThresholdSeconds">Age at or below which the device counts as idle (default 600).</param>
    /// <returns>
    /// <see cref="DeviceStatus.NeverReported"/> when <paramref name="lastSeenAt"/> is null,
    /// otherwise <see cref="DeviceStatus.Online"/>, <see cref="DeviceStatus.Idle"/> or
    /// <see cref="DeviceStatus.Offline"/> by increasing age.
    /// </returns>
    public static DeviceStatus Evaluate(DateTimeOffset? lastSeenAt, DateTimeOffset now,
                                        int onlineThresholdSeconds, int staleThresholdSeconds)
    {
        if (lastSeenAt is null)
        {
            return DeviceStatus.NeverReported;
        }

        // A device clock running ahead yields a negative age; treat it as freshly seen rather than stale.
        var ageSeconds = (now - lastSeenAt.Value).TotalSeconds;

        if (ageSeconds <= onlineThresholdSeconds)
        {
            return DeviceStatus.Online;
        }

        return ageSeconds <= staleThresholdSeconds ? DeviceStatus.Idle : DeviceStatus.Offline;
    }
}

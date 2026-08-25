namespace ParentalTrack.Domain.Entities;

/// <summary>
/// One issued device token. The row id is the JWT <c>jti</c>, which is how a token is revoked
/// without waiting for it to expire. Maps to table <c>device_sessions</c>.
/// </summary>
public sealed class DeviceSession
{
    /// <summary>Primary key; also the <c>jti</c> claim of the device token.</summary>
    public Guid Id { get; set; }

    /// <summary>Device the token was issued to. Cascade delete.</summary>
    public Guid DeviceId { get; set; }

    /// <summary>When the token was issued.</summary>
    public DateTimeOffset IssuedAt { get; set; }

    /// <summary>Absolute expiry of the token.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Null while the session is valid.</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>Why the session was revoked, for support and audit.</summary>
    public string? RevokedReason { get; set; }

    /// <summary>User agent captured at enrollment.</summary>
    public string? EnrolledUserAgent { get; set; }
}

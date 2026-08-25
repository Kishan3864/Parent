namespace ParentalTrack.Domain.Entities;

/// <summary>
/// A single-use opaque refresh token issued to a parent session. Rotated on every refresh:
/// the previous row is revoked and a new one inserted. Maps to table <c>refresh_tokens</c>.
/// </summary>
public sealed class RefreshToken
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Owning parent. Cascade delete.</summary>
    public Guid ParentId { get; set; }

    /// <summary>SHA-256 (base64) of the opaque token. The plaintext is never stored. Unique.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>Absolute expiry of the token.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>When the token was issued.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Null while the token is still usable; set on rotation, logout or breach response.</summary>
    public DateTimeOffset? RevokedAt { get; set; }
}

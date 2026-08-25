namespace ParentalTrack.Domain.Entities;

/// <summary>
/// A parent account: the owner of one or more <see cref="ChildDevice"/> records.
/// Maps to table <c>parents</c>.
/// </summary>
public sealed class Parent
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>The address exactly as typed by the user.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Lookup key: <c>Email.Trim().ToLowerInvariant()</c>. Unique.</summary>
    public string EmailNormalized { get; set; } = string.Empty;

    /// <summary>PBKDF2-HMAC-SHA256 hash in the format <c>pbkdf2-sha256$iterations$salt$subkey</c>.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>Name shown in the admin UI.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>False disables sign-in without deleting any data.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>When the account was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Devices owned by this parent. Deleting the parent cascades to them.</summary>
    public ICollection<ChildDevice> Devices { get; set; } = new List<ChildDevice>();
}

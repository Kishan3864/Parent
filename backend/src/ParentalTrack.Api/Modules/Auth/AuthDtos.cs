namespace ParentalTrack.Api.Modules.Auth;

/// <summary>
/// Request bodies bind with nullable members: a missing JSON property is a validation failure we
/// report ourselves, not a binder crash.
/// </summary>
public sealed record RegisterRequest(string? Email, string? Password, string? DisplayName);

public sealed record LoginRequest(string? Email, string? Password);

public sealed record RefreshRequest(string? RefreshToken);

/// <summary>The parent object embedded in <see cref="AuthResponse"/>.</summary>
public sealed record AuthParentDto(Guid Id, string Email, string DisplayName);

public sealed record AuthResponse(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAtUtc,
    AuthParentDto Parent);

/// <summary>Returned by <c>GET /api/v1/auth/me</c>.</summary>
public sealed record ParentDto(Guid Id, string Email, string DisplayName, DateTimeOffset CreatedAt);

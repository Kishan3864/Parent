using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ParentalTrack.Api.Options;
using ParentalTrack.Api.Security;
using ParentalTrack.Domain.Entities;
using ParentalTrack.Infrastructure.Persistence;

namespace ParentalTrack.Api.Modules.Auth;

/// <summary>Why an auth operation failed. The endpoint maps this onto a status code.</summary>
public enum AuthError
{
    None = 0,
    RegistrationDisabled,
    DuplicateEmail,
    InvalidCredentials,
    InvalidRefreshToken,
}

public sealed record AuthResult(AuthResponse? Response, AuthError Error)
{
    public static AuthResult Success(AuthResponse response) => new(response, AuthError.None);

    public static AuthResult Fail(AuthError error) => new(null, error);
}

/// <summary>
/// All parent authentication logic: registration, login, refresh-token rotation and logout.
/// Scoped, so it shares the request's <see cref="AppDbContext"/>.
/// </summary>
public sealed class AuthService
{
    /// <summary>
    /// A real PBKDF2 hash of a value nobody knows. Login verifies against it when the email is
    /// unknown, so an unknown account costs the same time as a wrong password and the caller gets no
    /// timing signal to enumerate users with.
    /// </summary>
    private static readonly string DummyPasswordHash =
        PasswordHasher.Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));

    private readonly AppDbContext _db;
    private readonly TokenService _tokens;
    private readonly JwtOptions _jwt;
    private readonly AuthOptions _auth;
    private readonly ILogger<AuthService> _logger;
    private readonly TimeProvider _timeProvider;

    public AuthService(
        AppDbContext db,
        TokenService tokens,
        IOptions<JwtOptions> jwt,
        IOptions<AuthOptions> auth,
        ILogger<AuthService> logger,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(jwt);
        ArgumentNullException.ThrowIfNull(auth);

        _db = db;
        _tokens = tokens;
        _jwt = jwt.Value;
        _auth = auth.Value;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public async Task<AuthResult> RegisterAsync(string email, string password, string displayName, CancellationToken ct)
    {
        if (!_auth.AllowSelfRegistration)
        {
            return AuthResult.Fail(AuthError.RegistrationDisabled);
        }

        var now = _timeProvider.GetUtcNow();
        var trimmedEmail = email.Trim();
        var normalizedEmail = Normalize(trimmedEmail);

        var exists = await _db.Parents
            .AsNoTracking()
            .AnyAsync(p => p.EmailNormalized == normalizedEmail, ct)
            .ConfigureAwait(false);

        if (exists)
        {
            return AuthResult.Fail(AuthError.DuplicateEmail);
        }

        var parent = new Parent
        {
            Id = Guid.NewGuid(),
            Email = trimmedEmail,
            EmailNormalized = normalizedEmail,
            PasswordHash = PasswordHasher.Hash(password),
            DisplayName = displayName.Trim(),
            IsActive = true,
            CreatedAt = now,
        };

        _db.Parents.Add(parent);
        var response = IssueTokens(parent, now);

        try
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException ex)
        {
            // ix_parents_email_normalized is the only unique constraint this insert can trip:
            // two registrations for the same address raced past the check above.
            _logger.LogWarning(ex, "Registration rejected: that email address is already registered.");
            return AuthResult.Fail(AuthError.DuplicateEmail);
        }

        _logger.LogInformation("Registered parent {ParentId}.", parent.Id);
        return AuthResult.Success(response);
    }

    public async Task<AuthResult> LoginAsync(string email, string password, CancellationToken ct)
    {
        var now = _timeProvider.GetUtcNow();
        var normalizedEmail = Normalize(email);

        var parent = await _db.Parents
            .FirstOrDefaultAsync(p => p.EmailNormalized == normalizedEmail, ct)
            .ConfigureAwait(false);

        if (parent is null)
        {
            _ = PasswordHasher.Verify(password, DummyPasswordHash);
            return AuthResult.Fail(AuthError.InvalidCredentials);
        }

        // A disabled account answers exactly like a wrong password, so no account state leaks.
        if (!PasswordHasher.Verify(password, parent.PasswordHash) || !parent.IsActive)
        {
            return AuthResult.Fail(AuthError.InvalidCredentials);
        }

        var response = IssueTokens(parent, now);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return AuthResult.Success(response);
    }

    public async Task<AuthResult> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        var now = _timeProvider.GetUtcNow();
        var tokenHash = TokenService.HashToken(refreshToken);

        var stored = await _db.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct)
            .ConfigureAwait(false);

        if (stored is null)
        {
            return AuthResult.Fail(AuthError.InvalidRefreshToken);
        }

        if (stored.RevokedAt is not null)
        {
            // Replay of a token we already rotated away: treat the whole family as compromised.
            await RevokeAllForParentAsync(stored.ParentId, now, ct).ConfigureAwait(false);
            _logger.LogWarning(
                "Refresh token reuse detected for parent {ParentId}; every active refresh token was revoked.",
                stored.ParentId);
            return AuthResult.Fail(AuthError.InvalidRefreshToken);
        }

        if (stored.ExpiresAt <= now)
        {
            stored.RevokedAt = now;
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return AuthResult.Fail(AuthError.InvalidRefreshToken);
        }

        var parent = await _db.Parents
            .FirstOrDefaultAsync(p => p.Id == stored.ParentId, ct)
            .ConfigureAwait(false);

        if (parent is null || !parent.IsActive)
        {
            stored.RevokedAt = now;
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return AuthResult.Fail(AuthError.InvalidRefreshToken);
        }

        stored.RevokedAt = now;
        var response = IssueTokens(parent, now);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return AuthResult.Success(response);
    }

    /// <summary>
    /// Revokes the presented refresh token. An unknown or already revoked token is a no-op, so logout
    /// always looks the same from outside.
    /// </summary>
    public async Task LogoutAsync(string? refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return;
        }

        var tokenHash = TokenService.HashToken(refreshToken);

        var stored = await _db.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash && t.RevokedAt == null, ct)
            .ConfigureAwait(false);

        if (stored is null)
        {
            return;
        }

        stored.RevokedAt = _timeProvider.GetUtcNow();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<ParentDto?> GetParentAsync(Guid parentId, CancellationToken ct) =>
        await _db.Parents
            .AsNoTracking()
            .Where(p => p.Id == parentId && p.IsActive)
            .Select(p => new ParentDto(p.Id, p.Email, p.DisplayName, p.CreatedAt))
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

    /// <summary>Mints the access token and queues a fresh refresh-token row. The caller saves.</summary>
    private AuthResponse IssueTokens(Parent parent, DateTimeOffset now)
    {
        var (accessToken, expiresAt) = _tokens.CreateParentAccessToken(parent);
        var refreshToken = TokenService.CreateOpaqueToken();

        _db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            ParentId = parent.Id,
            TokenHash = TokenService.HashToken(refreshToken),
            CreatedAt = now,
            ExpiresAt = now.AddDays(_jwt.RefreshTokenDays),
        });

        return new AuthResponse(
            accessToken,
            refreshToken,
            expiresAt,
            new AuthParentDto(parent.Id, parent.Email, parent.DisplayName));
    }

    private async Task RevokeAllForParentAsync(Guid parentId, DateTimeOffset now, CancellationToken ct)
    {
        var active = await _db.RefreshTokens
            .Where(t => t.ParentId == parentId && t.RevokedAt == null)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (var token in active)
        {
            token.RevokedAt = now;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static string Normalize(string email) => email.Trim().ToLowerInvariant();
}

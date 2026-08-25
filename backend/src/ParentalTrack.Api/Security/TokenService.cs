using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ParentalTrack.Api.Options;
using ParentalTrack.Domain.Entities;

namespace ParentalTrack.Api.Security;

/// <summary>
/// Issues the two JWT flavours (parent access token, device token) and the opaque refresh tokens.
/// Registered as a singleton; the signing credentials are built once.
/// </summary>
public sealed class TokenService
{
    private const int SigningKeyMinimumBytes = 32;
    private const int OpaqueTokenBytes = 32;

    private static readonly JsonWebTokenHandler Handler = new();

    private readonly JwtOptions _options;
    private readonly SigningCredentials _credentials;

    public TokenService(IOptions<JwtOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;

        if (string.IsNullOrWhiteSpace(_options.SigningKey)
            || Encoding.UTF8.GetByteCount(_options.SigningKey) < SigningKeyMinimumBytes)
        {
            throw new InvalidOperationException(
                $"'{JwtOptions.SectionName}:SigningKey' is missing or shorter than {SigningKeyMinimumBytes} bytes.");
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        _credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    }

    public (string Token, DateTimeOffset ExpiresAt) CreateParentAccessToken(Parent parent)
    {
        ArgumentNullException.ThrowIfNull(parent);

        var issuedAt = DateTimeOffset.UtcNow;
        var expiresAt = TruncateToSeconds(issuedAt.AddMinutes(_options.ParentAccessTokenMinutes));

        var claims = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [AuthConstants.SubjectClaim] = parent.Id.ToString(),
            [AuthConstants.EmailClaim] = parent.Email,
            [AuthConstants.NameClaim] = parent.DisplayName,
            [AuthConstants.TypeClaim] = AuthConstants.ParentTokenType,
        };

        return (Create(claims, _options.ParentAudience, issuedAt, expiresAt), expiresAt);
    }

    public (string Token, DateTimeOffset ExpiresAt) CreateDeviceToken(Guid deviceId, Guid parentId, Guid sessionId)
    {
        var issuedAt = DateTimeOffset.UtcNow;
        var expiresAt = TruncateToSeconds(issuedAt.AddDays(_options.DeviceTokenDays));

        var claims = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [AuthConstants.SubjectClaim] = deviceId.ToString(),
            // The session id is the revocation handle: DeviceSessionValidator looks it up on every request.
            [AuthConstants.TokenIdClaim] = sessionId.ToString(),
            [AuthConstants.ParentIdClaim] = parentId.ToString(),
            [AuthConstants.TypeClaim] = AuthConstants.DeviceTokenType,
        };

        return (Create(claims, _options.DeviceAudience, issuedAt, expiresAt), expiresAt);
    }

    /// <summary>32 cryptographically random bytes, base64url encoded. Never stored in plaintext.</summary>
    public static string CreateOpaqueToken() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(OpaqueTokenBytes));

    /// <summary>SHA-256, base64 — what actually lands in <c>refresh_tokens.token_hash</c>.</summary>
    public static string HashToken(string token)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    private string Create(IDictionary<string, object> claims, string audience, DateTimeOffset issuedAt, DateTimeOffset expiresAt)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = audience,
            IssuedAt = issuedAt.UtcDateTime,
            NotBefore = issuedAt.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = _credentials,
            Claims = claims,
        };

        return Handler.CreateToken(descriptor);
    }

    /// <summary>JWT "exp" has second precision — round-trip the advertised expiry through the same
    /// resolution so the client and the token never disagree.</summary>
    private static DateTimeOffset TruncateToSeconds(DateTimeOffset value) =>
        new(value.UtcDateTime.AddTicks(-(value.UtcDateTime.Ticks % TimeSpan.TicksPerSecond)), TimeSpan.Zero);
}

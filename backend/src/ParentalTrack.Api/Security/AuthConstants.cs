namespace ParentalTrack.Api.Security;

/// <summary>
/// Names shared by every module. Never inline these strings anywhere else.
/// </summary>
public static class AuthConstants
{
    // Authorization policies.
    public const string ParentPolicy = "ParentPolicy";
    public const string DevicePolicy = "DevicePolicy";

    // Custom claims. NOTE: the JWT bearer handler is configured with MapInboundClaims = false,
    // so the raw JWT claim names ("sub", "jti", ...) survive into ClaimsPrincipal untouched.
    public const string TypeClaim = "typ";
    public const string ParentIdClaim = "pid";
    public const string SubjectClaim = "sub";
    public const string TokenIdClaim = "jti";
    public const string EmailClaim = "email";
    public const string NameClaim = "name";

    // Values of the "typ" claim.
    public const string ParentTokenType = "parent";
    public const string DeviceTokenType = "device";

    // Rate limiter policy names.
    public const string LoginRateLimit = "login";
    public const string EnrollRateLimit = "enroll";
    public const string IngestRateLimit = "ingest";

    // Memory cache key prefix used by DeviceSessionValidator.
    public const string DeviceSessionCacheKeyPrefix = "devsess:";

    public static string DeviceSessionCacheKey(Guid sessionId) => DeviceSessionCacheKeyPrefix + sessionId;
}

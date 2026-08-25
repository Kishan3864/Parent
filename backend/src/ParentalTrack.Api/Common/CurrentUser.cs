using System.Security.Claims;
using ParentalTrack.Api.Security;

namespace ParentalTrack.Api.Common;

/// <summary>
/// Reads the caller's identity out of the validated JWT. Every parent-scoped query MUST be filtered
/// by <see cref="GetParentId"/> — that is the only tenancy boundary in the system.
/// </summary>
public static class CurrentUser
{
    /// <summary>Parent id from a parent token ("sub").</summary>
    public static Guid GetParentId(this ClaimsPrincipal principal) =>
        RequireGuid(principal, AuthConstants.SubjectClaim, ClaimTypes.NameIdentifier);

    /// <summary>Device id from a device token ("sub").</summary>
    public static Guid GetDeviceId(this ClaimsPrincipal principal) =>
        RequireGuid(principal, AuthConstants.SubjectClaim, ClaimTypes.NameIdentifier);

    /// <summary>Device session id from a device token ("jti"). This is the revocation handle.</summary>
    public static Guid GetSessionId(this ClaimsPrincipal principal) =>
        RequireGuid(principal, AuthConstants.TokenIdClaim);

    /// <summary>Owning parent of a device token ("pid").</summary>
    public static Guid GetDeviceParentId(this ClaimsPrincipal principal) =>
        RequireGuid(principal, AuthConstants.ParentIdClaim);

    public static bool TryGetGuid(this ClaimsPrincipal principal, string claimType, out Guid value)
    {
        var raw = principal.FindFirstValue(claimType);
        return Guid.TryParse(raw, out value);
    }

    private static Guid RequireGuid(ClaimsPrincipal principal, params string[] claimTypes)
    {
        foreach (var claimType in claimTypes)
        {
            if (principal.TryGetGuid(claimType, out var value))
            {
                return value;
            }
        }

        throw new InvalidOperationException(
            $"Authenticated principal is missing a usable '{claimTypes[0]}' claim.");
    }
}

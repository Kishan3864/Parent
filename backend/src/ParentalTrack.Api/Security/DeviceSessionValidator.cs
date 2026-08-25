using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ParentalTrack.Infrastructure.Persistence;

namespace ParentalTrack.Api.Security;

/// <summary>
/// Device token revocation check, run from <c>JwtBearerEvents.OnTokenValidated</c> for every device
/// request. Backed by a 30 s <see cref="IMemoryCache"/> entry so a revoke takes effect immediately
/// (the revoking endpoint calls <see cref="Invalidate"/>) while normal traffic stays off the database.
/// </summary>
public sealed class DeviceSessionValidator
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;

    public DeviceSessionValidator(AppDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<bool> IsSessionValidAsync(Guid sessionId, CancellationToken ct)
    {
        var key = AuthConstants.DeviceSessionCacheKey(sessionId);

        if (!_cache.TryGetValue(key, out SessionState? state) || state is null)
        {
            state = await LoadAsync(sessionId, ct).ConfigureAwait(false);
            _cache.Set(key, state, CacheTtl);
        }

        // Expiry is re-evaluated on every call rather than baked into the cached verdict, so a session
        // that lapses inside the TTL window stops working at the right moment.
        return state.Exists
               && state.RevokedAt is null
               && state.ExpiresAt > DateTimeOffset.UtcNow
               && state.DeviceIsActive;
    }

    public void Invalidate(IEnumerable<Guid> sessionIds)
    {
        ArgumentNullException.ThrowIfNull(sessionIds);

        foreach (var sessionId in sessionIds)
        {
            _cache.Remove(AuthConstants.DeviceSessionCacheKey(sessionId));
        }
    }

    private async Task<SessionState> LoadAsync(Guid sessionId, CancellationToken ct)
    {
        var state = await _db.DeviceSessions
            .AsNoTracking()
            .Where(session => session.Id == sessionId)
            .Join(
                _db.ChildDevices,
                session => session.DeviceId,
                device => device.Id,
                (session, device) => new SessionState(true, session.RevokedAt, session.ExpiresAt, device.IsActive))
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return state ?? SessionState.Missing;
    }

    private sealed record SessionState(bool Exists, DateTimeOffset? RevokedAt, DateTimeOffset ExpiresAt, bool DeviceIsActive)
    {
        public static readonly SessionState Missing = new(false, null, DateTimeOffset.MinValue, false);
    }
}

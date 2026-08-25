using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ParentalTrack.Api.Options;
using ParentalTrack.Api.Security;
using ParentalTrack.Domain;
using ParentalTrack.Domain.Entities;
using ParentalTrack.Domain.Enums;
using ParentalTrack.Infrastructure.Persistence;

namespace ParentalTrack.Api.Modules.Devices;

/// <summary>
/// Parent-facing device management. Every method takes the parent id from the caller's JWT and
/// filters on it, so a device owned by another parent is indistinguishable from one that does not
/// exist (both surface as 404 at the endpoint layer).
/// </summary>
public sealed class DeviceService(
    AppDbContext db,
    IOptions<TrackingOptions> trackingOptions,
    IOptions<DevicesOptions> devicesOptions,
    TimeProvider timeProvider,
    DeviceSessionValidator sessionValidator,
    ILogger<DeviceService> logger)
{
    public const int MaxChildNameLength = 128;
    public const int MaxDeviceLabelLength = 128;

    private const string RevokedByParentReason = "Revoked by parent";

    private readonly TrackingOptions _tracking = trackingOptions.Value;
    private readonly DevicesOptions _devices = devicesOptions.Value;

    public async Task<IReadOnlyList<DeviceSummaryDto>> ListAsync(Guid parentId, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();

        var devices = await db.ChildDevices
            .AsNoTracking()
            .Where(d => d.ParentId == parentId)
            .OrderBy(d => d.ChildName)
            .ThenBy(d => d.CreatedAt)
            .ToListAsync(ct);

        if (devices.Count == 0)
        {
            return [];
        }

        var deviceIds = devices.Select(d => d.Id).ToArray();

        // One query for the newest fix of every device: LastLocationId already points straight at it.
        var lastLocationIds = devices
            .Where(d => d.LastLocationId.HasValue)
            .Select(d => d.LastLocationId!.Value)
            .Distinct()
            .ToArray();

        Dictionary<long, LocationRecord> lastLocations = lastLocationIds.Length > 0
            // The device predicate is what enforces tenancy here: last_location_id is a plain
            // column with no FK, so a wrong pointer must not be able to hand a parent another
            // family's coordinates. Same shape as HistoryService.LoadLatestRecordAsync.
            ? await db.LocationRecords
                .AsNoTracking()
                .Where(r => deviceIds.Contains(r.DeviceId) && lastLocationIds.Contains(r.Id))
                .ToDictionaryAsync(r => r.Id, ct)
            : new();

        // One query for session liveness across the whole list.
        var devicesWithActiveSession = (await db.DeviceSessions
                .AsNoTracking()
                .Where(s => deviceIds.Contains(s.DeviceId) && s.RevokedAt == null && s.ExpiresAt > now)
                .Select(s => s.DeviceId)
                .Distinct()
                .ToListAsync(ct))
            .ToHashSet();

        var summaries = new List<DeviceSummaryDto>(devices.Count);
        foreach (var device in devices)
        {
            LocationRecord? lastLocation = null;

            // The pointer can outlive the row it names once the retention worker has run, and it
            // is only a plain column — a row that does not belong to this device is ignored.
            if (device.LastLocationId is { } locationId
                && lastLocations.TryGetValue(locationId, out var candidate)
                && candidate.DeviceId == device.Id)
            {
                lastLocation = candidate;
            }

            summaries.Add(ToSummary(device, lastLocation, devicesWithActiveSession.Contains(device.Id), now));
        }

        return summaries;
    }

    public async Task<DeviceDetailDto?> GetAsync(Guid parentId, Guid deviceId, CancellationToken ct)
    {
        var device = await db.ChildDevices
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == deviceId && d.ParentId == parentId, ct);

        return device is null ? null : await BuildDetailAsync(device, pairingCode: null, ct);
    }

    public async Task<DeviceDetailDto> CreateAsync(Guid parentId, CreateDeviceRequest request, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var code = PairingCode.Generate();

        var device = new ChildDevice
        {
            Id = Guid.NewGuid(),
            ParentId = parentId,
            ChildName = request.ChildName!.Trim(),
            DeviceLabel = Clean(request.DeviceLabel),
            IsActive = true,
            CreatedAt = now,
            PairingCodeHash = TokenService.HashToken(code),
            PairingCodeExpiresAt = now.AddMinutes(_devices.PairingCodeTtlMinutes),
        };

        db.ChildDevices.Add(device);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Parent {ParentId} created device {DeviceId}", parentId, device.Id);

        // A brand new device has no sessions and no locations, so no extra round trips are needed.
        return ToDetail(device, lastLocation: null, hasActiveSession: false, now, PairingCode.Format(code));
    }

    public async Task<DeviceDetailDto?> UpdateAsync(Guid parentId, Guid deviceId, UpdateDeviceRequest request,
        CancellationToken ct)
    {
        var device = await db.ChildDevices
            .FirstOrDefaultAsync(d => d.Id == deviceId && d.ParentId == parentId, ct);

        if (device is null)
        {
            return null;
        }

        if (request.ChildName is not null)
        {
            device.ChildName = request.ChildName.Trim();
        }

        if (request.DeviceLabel is not null)
        {
            device.DeviceLabel = Clean(request.DeviceLabel);
        }

        var deactivated = request.IsActive is false && device.IsActive;
        if (request.IsActive is { } isActive)
        {
            device.IsActive = isActive;
        }

        // The session validator caches its verdict for 30 s, so a disabled device would keep being
        // accepted until that entry expires. Evict now. The sessions themselves are left alone, so
        // re-enabling the device brings the existing token back to life.
        Guid[] cachedSessionIds = [];
        if (deactivated)
        {
            cachedSessionIds = await db.DeviceSessions
                .AsNoTracking()
                .Where(s => s.DeviceId == deviceId && s.RevokedAt == null)
                .Select(s => s.Id)
                .ToArrayAsync(ct);
        }

        await db.SaveChangesAsync(ct);

        if (cachedSessionIds.Length > 0)
        {
            sessionValidator.Invalidate(cachedSessionIds);
        }

        return await BuildDetailAsync(device, pairingCode: null, ct);
    }

    public async Task<bool> DeleteAsync(Guid parentId, Guid deviceId, CancellationToken ct)
    {
        var device = await db.ChildDevices
            .FirstOrDefaultAsync(d => d.Id == deviceId && d.ParentId == parentId, ct);

        if (device is null)
        {
            return false;
        }

        // Sessions and location rows go with the device (cascade), but the validator cache has to be
        // evicted explicitly or the deleted device keeps being accepted for up to 30 s.
        var sessionIds = await db.DeviceSessions
            .AsNoTracking()
            .Where(s => s.DeviceId == deviceId)
            .Select(s => s.Id)
            .ToArrayAsync(ct);

        db.ChildDevices.Remove(device);
        await db.SaveChangesAsync(ct);

        if (sessionIds.Length > 0)
        {
            sessionValidator.Invalidate(sessionIds);
        }

        logger.LogInformation("Parent {ParentId} deleted device {DeviceId}", parentId, deviceId);
        return true;
    }

    public async Task<PairingCodeDto?> RegeneratePairingCodeAsync(Guid parentId, Guid deviceId, CancellationToken ct)
    {
        var device = await db.ChildDevices
            .FirstOrDefaultAsync(d => d.Id == deviceId && d.ParentId == parentId, ct);

        if (device is null)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        var code = PairingCode.Generate();
        var expiresAt = now.AddMinutes(_devices.PairingCodeTtlMinutes);

        device.PairingCodeHash = TokenService.HashToken(code);
        device.PairingCodeExpiresAt = expiresAt;

        await db.SaveChangesAsync(ct);

        logger.LogInformation("Parent {ParentId} issued a pairing code for device {DeviceId}", parentId, deviceId);
        return new PairingCodeDto(PairingCode.Format(code), expiresAt);
    }

    public async Task<bool> RevokeAsync(Guid parentId, Guid deviceId, CancellationToken ct)
    {
        var deviceExists = await db.ChildDevices
            .AsNoTracking()
            .AnyAsync(d => d.Id == deviceId && d.ParentId == parentId, ct);

        if (!deviceExists)
        {
            return false;
        }

        var sessions = await db.DeviceSessions
            .Where(s => s.DeviceId == deviceId && s.RevokedAt == null)
            .ToListAsync(ct);

        if (sessions.Count == 0)
        {
            return true;
        }

        var now = timeProvider.GetUtcNow();
        foreach (var session in sessions)
        {
            session.RevokedAt = now;
            session.RevokedReason = RevokedByParentReason;
        }

        await db.SaveChangesAsync(ct);

        // Evict the cached verdicts so the next device call is a 401 instead of waiting out the TTL.
        sessionValidator.Invalidate(sessions.Select(s => s.Id));

        logger.LogInformation("Parent {ParentId} revoked {SessionCount} session(s) of device {DeviceId}",
            parentId, sessions.Count, deviceId);
        return true;
    }

    private async Task<DeviceDetailDto> BuildDetailAsync(ChildDevice device, string? pairingCode, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();

        var hasActiveSession = await db.DeviceSessions
            .AsNoTracking()
            .AnyAsync(s => s.DeviceId == device.Id && s.RevokedAt == null && s.ExpiresAt > now, ct);

        LocationRecord? lastLocation = null;
        if (device.LastLocationId is { } locationId)
        {
            lastLocation = await db.LocationRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == locationId && r.DeviceId == device.Id, ct);
        }

        return ToDetail(device, lastLocation, hasActiveSession, now, pairingCode);
    }

    private DeviceSummaryDto ToSummary(ChildDevice device, LocationRecord? lastLocation, bool hasActiveSession,
        DateTimeOffset now)
    {
        var (status, isStale, secondsSinceUpdate) = Evaluate(device.LastSeenAt, now);

        return new DeviceSummaryDto(
            device.Id,
            device.ChildName,
            device.DeviceLabel,
            device.Platform,
            device.Model,
            device.IsActive,
            device.PairedAt is not null,
            hasActiveSession,
            status,
            isStale,
            device.LastSeenAt,
            secondsSinceUpdate,
            device.LastBatteryPercent,
            lastLocation is null ? null : LocationPointDto.FromEntity(lastLocation));
    }

    private DeviceDetailDto ToDetail(ChildDevice device, LocationRecord? lastLocation, bool hasActiveSession,
        DateTimeOffset now, string? pairingCode)
    {
        var (status, isStale, secondsSinceUpdate) = Evaluate(device.LastSeenAt, now);

        return new DeviceDetailDto(
            device.Id,
            device.ChildName,
            device.DeviceLabel,
            device.Platform,
            device.Model,
            device.IsActive,
            device.PairedAt is not null,
            hasActiveSession,
            status,
            isStale,
            device.LastSeenAt,
            secondsSinceUpdate,
            device.LastBatteryPercent,
            lastLocation is null ? null : LocationPointDto.FromEntity(lastLocation),
            device.CreatedAt,
            device.PairedAt,
            pairingCode,
            // Only meaningful while a code is outstanding: the hash is cleared once one is consumed.
            device.PairingCodeHash is null ? null : device.PairingCodeExpiresAt,
            device.AppVersion,
            device.OsVersion);
    }

    private (DeviceStatus Status, bool IsStale, long? SecondsSinceUpdate) Evaluate(DateTimeOffset? lastSeenAt,
        DateTimeOffset now)
    {
        var status = DeviceStatusCalculator.Evaluate(
            lastSeenAt, now, _tracking.OnlineThresholdSeconds, _tracking.StaleThresholdSeconds);

        var isStale = status is DeviceStatus.Offline or DeviceStatus.NeverReported;

        long? secondsSinceUpdate = lastSeenAt is null
            ? null
            : (long)Math.Max(0d, Math.Round((now - lastSeenAt.Value).TotalSeconds, MidpointRounding.AwayFromZero));

        return (status, isStale, secondsSinceUpdate);
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// Pairing codes are 8 characters drawn from an alphabet without visually ambiguous glyphs
/// (no I, O, 0 or 1), shown to the parent as XXXX-XXXX and stored only as a SHA-256 hash.
/// </summary>
internal static class PairingCode
{
    internal const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    internal const int Length = 8;

    /// <summary>A new code in canonical (hashable) form: 8 upper-case characters, no dash.</summary>
    internal static string Generate() => new(RandomNumberGenerator.GetItems<char>(Alphabet, Length));

    /// <summary>Human-readable rendering of a canonical code: XXXX-XXXX.</summary>
    internal static string Format(string code) =>
        code.Length == Length ? string.Concat(code[..4], "-", code[4..]) : code;

    /// <summary>
    /// Canonicalises whatever the child app sent: upper-cased, with every non-alphanumeric character
    /// dropped, so "ab3d-9kmp", "AB3D 9KMP" and "AB3D9KMP" all hash to the same value.
    /// </summary>
    internal static string Canonicalise(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        var buffer = new char[input.Length];
        var count = 0;

        foreach (var ch in input)
        {
            if (char.IsAsciiLetterOrDigit(ch))
            {
                buffer[count++] = char.ToUpperInvariant(ch);
            }
        }

        return new string(buffer, 0, count);
    }
}

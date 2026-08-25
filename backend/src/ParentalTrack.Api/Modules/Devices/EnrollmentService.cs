using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ParentalTrack.Api.Options;
using ParentalTrack.Api.Security;
using ParentalTrack.Domain.Entities;
using ParentalTrack.Infrastructure.Persistence;

namespace ParentalTrack.Api.Modules.Devices;

/// <summary>
/// Device-facing side of the Devices module: turns a one-time pairing code into a device session
/// plus a long-lived device token, and answers the device asking about itself.
/// </summary>
public sealed class EnrollmentService(
    AppDbContext db,
    IOptions<TrackingOptions> trackingOptions,
    TimeProvider timeProvider,
    TokenService tokenService,
    DeviceSessionValidator sessionValidator,
    ILogger<EnrollmentService> logger)
{
    private const int MaxManufacturerLength = 64;
    private const int MaxModelLength = 64;
    private const int MaxOsVersionLength = 32;
    private const int MaxAppVersionLength = 32;
    private const int MaxInstallIdLength = 64;
    private const int MaxUserAgentLength = 256;

    private const string AndroidPlatform = "android";
    private const string SupersededReason = "Superseded by a new enrollment";

    private readonly TrackingOptions _tracking = trackingOptions.Value;

    /// <summary>
    /// Consumes a pairing code. Returns <c>null</c> when the code is unknown, expired, already used
    /// or belongs to a disabled device — the caller turns every one of those into the same 400 so a
    /// caller cannot probe which codes exist.
    /// </summary>
    public async Task<EnrollResponse?> EnrollAsync(EnrollRequest request, string? userAgent, CancellationToken ct)
    {
        var code = PairingCode.Canonicalise(request.PairingCode);
        if (code.Length != PairingCode.Length)
        {
            logger.LogWarning("Rejected an enrollment attempt with a malformed pairing code");
            return null;
        }

        var now = timeProvider.GetUtcNow();
        var codeHash = TokenService.HashToken(code);

        // The hash is the lookup key, so this is one indexed equality query rather than a scan.
        var device = await db.ChildDevices
            .FirstOrDefaultAsync(
                d => d.PairingCodeHash == codeHash
                     && d.PairingCodeExpiresAt != null
                     && d.PairingCodeExpiresAt > now
                     && d.IsActive,
                ct);

        if (device is null)
        {
            logger.LogWarning("Rejected an enrollment attempt with an unknown or expired pairing code");
            return null;
        }

        // A device holds at most one live session: pairing again supersedes the previous install.
        var previousSessions = await db.DeviceSessions
            .Where(s => s.DeviceId == device.Id && s.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var previous in previousSessions)
        {
            previous.RevokedAt = now;
            previous.RevokedReason = SupersededReason;
        }

        var sessionId = Guid.NewGuid();
        var (deviceToken, tokenExpiresAt) = tokenService.CreateDeviceToken(device.Id, device.ParentId, sessionId);

        db.DeviceSessions.Add(new DeviceSession
        {
            Id = sessionId,
            DeviceId = device.Id,
            IssuedAt = now,
            ExpiresAt = tokenExpiresAt,
            EnrolledUserAgent = Clean(userAgent, MaxUserAgentLength),
        });

        device.PairedAt = now;
        device.Platform = AndroidPlatform;
        device.Manufacturer = Clean(request.Manufacturer, MaxManufacturerLength);
        device.Model = Clean(request.Model, MaxModelLength);
        device.OsVersion = Clean(request.OsVersion, MaxOsVersionLength);
        device.AppVersion = Clean(request.AppVersion, MaxAppVersionLength);
        device.InstallId = Clean(request.InstallId, MaxInstallIdLength);

        // Single use: the code dies with the enrollment that consumed it. Pairing this device again
        // needs a fresh code from the parent dashboard.
        device.PairingCodeHash = null;
        device.PairingCodeExpiresAt = null;

        await db.SaveChangesAsync(ct);

        if (previousSessions.Count > 0)
        {
            sessionValidator.Invalidate(previousSessions.Select(s => s.Id));
        }

        logger.LogInformation(
            "Device {DeviceId} enrolled with session {SessionId}, superseding {SupersededCount} session(s)",
            device.Id, sessionId, previousSessions.Count);

        return new EnrollResponse(
            device.Id,
            device.ChildName,
            deviceToken,
            tokenExpiresAt,
            TrackingConfigDto.FromOptions(_tracking));
    }

    /// <summary>What the child app is allowed to know about itself.</summary>
    public async Task<DeviceSelfDto?> GetSelfAsync(Guid deviceId, CancellationToken ct)
    {
        var device = await db.ChildDevices
            .AsNoTracking()
            .Where(d => d.Id == deviceId)
            .Select(d => new { d.Id, d.ChildName, d.IsActive })
            .FirstOrDefaultAsync(ct);

        return device is null
            ? null
            : new DeviceSelfDto(device.Id, device.ChildName, device.IsActive, TrackingConfigDto.FromOptions(_tracking));
    }

    /// <summary>
    /// Trims and truncates a device-reported string to its column width. The values come straight
    /// from <c>android.os.Build</c>, so they are clamped rather than rejected: a long vendor string
    /// must not cost the child a working enrollment.
    /// </summary>
    private static string? Clean(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}

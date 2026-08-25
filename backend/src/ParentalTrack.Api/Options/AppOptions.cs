namespace ParentalTrack.Api.Options;

/// <summary>
/// Strongly typed configuration. These types are the shared vocabulary between all API modules —
/// bind them once in <c>Program.cs</c> and inject <c>IOptions&lt;T&gt;</c> where needed.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "parentaltrack";
    public string ParentAudience { get; set; } = "parentaltrack.admin";
    public string DeviceAudience { get; set; } = "parentaltrack.device";

    /// <summary>HMAC-SHA256 signing key. Must be supplied via configuration (Jwt__SigningKey) and be >= 32 bytes.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public int ParentAccessTokenMinutes { get; set; } = 60;
    public int RefreshTokenDays { get; set; } = 30;
    public int DeviceTokenDays { get; set; } = 365;
}

public sealed class TrackingOptions
{
    public const string SectionName = "Tracking";

    /// <summary>A device seen within this many seconds is "online".</summary>
    public int OnlineThresholdSeconds { get; set; } = 180;

    /// <summary>Beyond this many seconds a device is "offline" and its last fix is stale.</summary>
    public int StaleThresholdSeconds { get; set; } = 600;

    /// <summary>How often the admin panel should poll for current locations.</summary>
    public int DefaultRefreshSeconds { get; set; } = 15;

    // Values handed to the child device at enrollment.
    public int IntervalSeconds { get; set; } = 60;
    public int FastestIntervalSeconds { get; set; } = 30;
    public int MinDistanceMeters { get; set; } = 25;
    public int BatchMaxSize { get; set; } = 100;
    public int UploadIntervalSeconds { get; set; } = 120;

    public string MapTileUrl { get; set; } = "https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png";
    public string MapAttribution { get; set; } = "(c) OpenStreetMap contributors";
}

public sealed class IngestionOptions
{
    public const string SectionName = "Ingestion";

    /// <summary>Maximum points accepted in a single ingest request.</summary>
    public int MaxBatchSize { get; set; } = 200;

    /// <summary>Bounded capacity of the in-process ingest channel.</summary>
    public int QueueCapacity { get; set; } = 10_000;

    /// <summary>How many points the background writer persists per database round trip.</summary>
    public int WriteBatchSize { get; set; } = 200;

    public int FlushIntervalMilliseconds { get; set; } = 500;

    /// <summary>Location rows older than this are deleted by the retention worker.</summary>
    public int RetentionDays { get; set; } = 90;
}

public sealed class DevicesOptions
{
    public const string SectionName = "Devices";

    public int PairingCodeTtlMinutes { get; set; } = 60;
}

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>When false, POST /api/v1/auth/register returns 403.</summary>
    public bool AllowSelfRegistration { get; set; }
}

public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    public bool Enabled { get; set; }
    public string ParentEmail { get; set; } = "parent@example.com";
    public string ParentPassword { get; set; } = "ChangeMe123!";
    public string ParentDisplayName { get; set; } = "Demo Parent";
}

public sealed class CorsOptions
{
    public const string SectionName = "Cors";

    public string[] AllowedOrigins { get; set; } = [];
}

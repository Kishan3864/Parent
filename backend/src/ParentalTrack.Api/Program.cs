using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using ParentalTrack.Api;
using ParentalTrack.Api.Common;
using ParentalTrack.Api.Modules.Auth;
using ParentalTrack.Api.Modules.Devices;
using ParentalTrack.Api.Modules.History;
using ParentalTrack.Api.Modules.Ingestion;
using ParentalTrack.Api.Options;
using ParentalTrack.Api.Security;
using ParentalTrack.Infrastructure;
using ParentalTrack.Infrastructure.Persistence;
using Scalar.AspNetCore;

const int SigningKeyMinimumBytes = 32;
const string LiveHealthTag = "live";
const string ReadyHealthTag = "ready";

var builder = WebApplication.CreateBuilder(args);
var isDevelopment = builder.Environment.IsDevelopment();

// ---------------------------------------------------------------------------------------------
// 1. Configuration. Every section is bound once here; modules only inject IOptions<T>.
// ---------------------------------------------------------------------------------------------
builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.Issuer), "Jwt:Issuer must not be empty.")
    .Validate(o => !string.IsNullOrWhiteSpace(o.ParentAudience), "Jwt:ParentAudience must not be empty.")
    .Validate(o => !string.IsNullOrWhiteSpace(o.DeviceAudience), "Jwt:DeviceAudience must not be empty.")
    .Validate(o => o.ParentAudience != o.DeviceAudience, "Jwt:ParentAudience and Jwt:DeviceAudience must differ.")
    .Validate(o => o.ParentAccessTokenMinutes > 0, "Jwt:ParentAccessTokenMinutes must be greater than zero.")
    .Validate(o => o.RefreshTokenDays > 0, "Jwt:RefreshTokenDays must be greater than zero.")
    .Validate(o => o.DeviceTokenDays > 0, "Jwt:DeviceTokenDays must be greater than zero.")
    .ValidateOnStart();

builder.Services.AddOptions<TrackingOptions>()
    .Bind(builder.Configuration.GetSection(TrackingOptions.SectionName))
    .Validate(o => o.OnlineThresholdSeconds > 0, "Tracking:OnlineThresholdSeconds must be greater than zero.")
    .Validate(o => o.StaleThresholdSeconds >= o.OnlineThresholdSeconds,
        "Tracking:StaleThresholdSeconds must be greater than or equal to Tracking:OnlineThresholdSeconds.")
    .Validate(o => o.DefaultRefreshSeconds > 0, "Tracking:DefaultRefreshSeconds must be greater than zero.")
    .Validate(o => o.IntervalSeconds > 0, "Tracking:IntervalSeconds must be greater than zero.")
    .Validate(o => o.FastestIntervalSeconds > 0 && o.FastestIntervalSeconds <= o.IntervalSeconds,
        "Tracking:FastestIntervalSeconds must be greater than zero and not greater than Tracking:IntervalSeconds.")
    .Validate(o => o.MinDistanceMeters >= 0, "Tracking:MinDistanceMeters must not be negative.")
    .Validate(o => o.BatchMaxSize > 0, "Tracking:BatchMaxSize must be greater than zero.")
    .Validate(o => o.UploadIntervalSeconds > 0, "Tracking:UploadIntervalSeconds must be greater than zero.")
    .Validate(o => !string.IsNullOrWhiteSpace(o.MapTileUrl), "Tracking:MapTileUrl must not be empty.")
    .ValidateOnStart();

builder.Services.AddOptions<IngestionOptions>()
    .Bind(builder.Configuration.GetSection(IngestionOptions.SectionName))
    .Validate(o => o.MaxBatchSize > 0, "Ingestion:MaxBatchSize must be greater than zero.")
    .Validate(o => o.QueueCapacity > 0, "Ingestion:QueueCapacity must be greater than zero.")
    .Validate(o => o.WriteBatchSize > 0, "Ingestion:WriteBatchSize must be greater than zero.")
    .Validate(o => o.FlushIntervalMilliseconds > 0, "Ingestion:FlushIntervalMilliseconds must be greater than zero.")
    .Validate(o => o.RetentionDays > 0, "Ingestion:RetentionDays must be greater than zero.")
    .ValidateOnStart();

builder.Services.AddOptions<DevicesOptions>()
    .Bind(builder.Configuration.GetSection(DevicesOptions.SectionName))
    .Validate(o => o.PairingCodeTtlMinutes > 0, "Devices:PairingCodeTtlMinutes must be greater than zero.")
    .ValidateOnStart();

// AuthOptions is a single feature flag — binding is all the validation it can have.
builder.Services.AddOptions<AuthOptions>()
    .Bind(builder.Configuration.GetSection(AuthOptions.SectionName));

builder.Services.AddOptions<SeedOptions>()
    .Bind(builder.Configuration.GetSection(SeedOptions.SectionName))
    .Validate(o => !o.Enabled || !string.IsNullOrWhiteSpace(o.ParentEmail),
        "Seed:ParentEmail must be set when Seed:Enabled is true.")
    .Validate(o => !o.Enabled || o.ParentPassword.Length >= 10,
        "Seed:ParentPassword must be at least 10 characters when Seed:Enabled is true.")
    .Validate(o => !o.Enabled || !string.IsNullOrWhiteSpace(o.ParentDisplayName),
        "Seed:ParentDisplayName must be set when Seed:Enabled is true.")
    .ValidateOnStart();

builder.Services.AddOptions<CorsOptions>()
    .Bind(builder.Configuration.GetSection(CorsOptions.SectionName))
    .Validate(o => o.AllowedOrigins.All(origin => Uri.IsWellFormedUriString(origin, UriKind.Absolute)),
        "Cors:AllowedOrigins must contain absolute origins such as http://localhost:5173.")
    .ValidateOnStart();

// ---------------------------------------------------------------------------------------------
// 2. Startup guard. A key shorter than 32 bytes cannot sign HMAC-SHA256, so refuse to start rather
//    than fail on the first login. Development takes its key from appsettings.Development.json.
// ---------------------------------------------------------------------------------------------
var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
var signingKeyBytes = Encoding.UTF8.GetBytes(jwtOptions.SigningKey);
if (signingKeyBytes.Length < SigningKeyMinimumBytes)
{
    throw new InvalidOperationException(
        $"Jwt:SigningKey is missing or shorter than {SigningKeyMinimumBytes} bytes. Supply it through the " +
        "Jwt__SigningKey environment variable or user secrets " +
        "(dotnet user-secrets set \"Jwt:SigningKey\" \"<64 random characters>\").");
}

// ---------------------------------------------------------------------------------------------
// 3. Ambient services every module depends on. TimeProvider is injected everywhere instead of
//    DateTimeOffset.UtcNow so status and staleness logic stays testable.
// ---------------------------------------------------------------------------------------------
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddMemoryCache();
builder.Services.TryAddSingleton<TokenService>();
builder.Services.TryAddScoped<DeviceSessionValidator>();

// ---------------------------------------------------------------------------------------------
// 4. Persistence.
// ---------------------------------------------------------------------------------------------
builder.Services.AddInfrastructure(builder.Configuration);

// ---------------------------------------------------------------------------------------------
// 5. Wire format: RFC7807 for every error, camelCase JSON, enums as camelCase strings.
// ---------------------------------------------------------------------------------------------
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(json =>
{
    json.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    // Nullable members stay on the wire as explicit nulls: contract §0/§2 spell them out
    // ("lastLocation": null) and both clients declare them present-and-nullable.
    json.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
    json.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    // Contract §0: ISO-8601 UTC with a "Z" and millisecond precision, not the round-trip form.
    json.SerializerOptions.Converters.Add(new UtcDateTimeOffsetJsonConverter());
});

// ---------------------------------------------------------------------------------------------
// 6. Authentication: one bearer scheme, two audiences (parent + device).
// ---------------------------------------------------------------------------------------------
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Keep the raw JWT claim names ("sub", "jti", "typ", "pid") — CurrentUser reads exactly those.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudiences = [jwtOptions.ParentAudience, jwtOptions.DeviceAudience],
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(signingKeyBytes),
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = AuthConstants.NameClaim
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var principal = context.Principal;
                if (principal is null)
                {
                    context.Fail("Token validation produced no principal.");
                    return;
                }

                // Only device tokens are revocable per session; parent access tokens are short-lived.
                if (!string.Equals(
                        principal.FindFirstValue(AuthConstants.TypeClaim),
                        AuthConstants.DeviceTokenType,
                        StringComparison.Ordinal))
                {
                    return;
                }

                if (!principal.TryGetGuid(AuthConstants.TokenIdClaim, out var sessionId))
                {
                    context.Fail("Device token is missing a usable 'jti' claim.");
                    return;
                }

                var validator = context.HttpContext.RequestServices.GetRequiredService<DeviceSessionValidator>();
                if (!await validator.IsSessionValidAsync(sessionId, context.HttpContext.RequestAborted))
                {
                    context.Fail("Device session was revoked or expired, or the device is disabled.");
                }
            }
        };
    });

// ---------------------------------------------------------------------------------------------
// 7. Authorization: the token type decides which half of the API a caller may reach.
// ---------------------------------------------------------------------------------------------
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(AuthConstants.ParentPolicy, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim(AuthConstants.TypeClaim, AuthConstants.ParentTokenType))
    .AddPolicy(AuthConstants.DevicePolicy, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim(AuthConstants.TypeClaim, AuthConstants.DeviceTokenType));

// ---------------------------------------------------------------------------------------------
// 8. Rate limiting. Endpoints opt in by policy name through RequireRateLimiting.
// ---------------------------------------------------------------------------------------------
static string ClientPartitionKey(HttpContext httpContext) =>
    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown-client";

static FixedWindowRateLimiterOptions PerMinute(int permitLimit) => new()
{
    PermitLimit = permitLimit,
    Window = TimeSpan.FromMinutes(1),
    QueueLimit = 0,
    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
    AutoReplenishment = true
};

builder.Services.AddRateLimiter(rateLimiter =>
{
    rateLimiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    rateLimiter.AddPolicy(AuthConstants.LoginRateLimit, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(ClientPartitionKey(httpContext), _ => PerMinute(10)));

    rateLimiter.AddPolicy(AuthConstants.EnrollRateLimit, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(ClientPartitionKey(httpContext), _ => PerMinute(5)));

    // Partitioned by the device id carried in the token so one noisy device cannot starve the rest.
    // UseRateLimiter runs after UseAuthentication, so the claim is already populated here.
    rateLimiter.AddPolicy(AuthConstants.IngestRateLimit, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.User.FindFirstValue(AuthConstants.SubjectClaim) ?? ClientPartitionKey(httpContext),
            _ => PerMinute(120)));

    rateLimiter.OnRejected = (context, _) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        // The body is deliberately left empty: UseStatusCodePages renders the 429 as ProblemDetails.
        return ValueTask.CompletedTask;
    };
});

// ---------------------------------------------------------------------------------------------
// 8b. Forwarded headers. The rate limiters above partition on the caller's IP (contract §4.3:
//     "login 10/min/IP, enroll 5/min/IP"), but TLS is terminated upstream and the container speaks
//     plain HTTP, so Connection.RemoteIpAddress is the reverse proxy for every request and the whole
//     deployment would share one partition. X-Forwarded-For is honoured ONLY from proxies listed in
//     "Network:KnownProxies" / "Network:KnownNetworks"; with nothing configured the defaults trust
//     loopback only, so an untrusted caller can never choose its own partition key.
// ---------------------------------------------------------------------------------------------
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

    var knownProxies = builder.Configuration.GetSection("Network:KnownProxies").Get<string[]>() ?? [];
    foreach (var candidate in knownProxies)
    {
        if (IPAddress.TryParse(candidate, out var address))
        {
            options.KnownProxies.Add(address);
        }
    }

    var knownNetworks = builder.Configuration.GetSection("Network:KnownNetworks").Get<string[]>() ?? [];
    foreach (var candidate in knownNetworks)
    {
        if (System.Net.IPNetwork.TryParse(candidate, out var network))
        {
            options.KnownIPNetworks.Add(network);
        }
    }

    // One hop by default: the terminator itself. Raise it only when a chain of trusted proxies
    // is actually deployed, or the client-supplied end of the header becomes reachable.
    options.ForwardLimit = Math.Max(1, builder.Configuration.GetValue("Network:ForwardLimit", 1));
});

// ---------------------------------------------------------------------------------------------
// 9. CORS. Bearer tokens, not cookies, so credentials stay disabled.
// ---------------------------------------------------------------------------------------------
var corsOptions = builder.Configuration.GetSection(CorsOptions.SectionName).Get<CorsOptions>() ?? new CorsOptions();
builder.Services.AddCors(cors => cors.AddDefaultPolicy(policy => policy
    .WithOrigins(corsOptions.AllowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()));

// ---------------------------------------------------------------------------------------------
// 10. Modules. Each Add/Map pair is the whole seam: move a module folder into its own host and it
//     becomes a service without touching the others.
// ---------------------------------------------------------------------------------------------
builder.Services.AddAuthModule(builder.Configuration);
builder.Services.AddDevicesModule(builder.Configuration);
builder.Services.AddIngestionModule(builder.Configuration);
builder.Services.AddHistoryModule(builder.Configuration);

// ---------------------------------------------------------------------------------------------
// 11. OpenAPI document (exposed in Development only, see the endpoint section below).
// ---------------------------------------------------------------------------------------------
builder.Services.AddOpenApi(openApi => openApi.AddDocumentTransformer((document, _, _) =>
{
    var components = document.Components ??= new OpenApiComponents();
    var securitySchemes = components.SecuritySchemes ??=
        new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal);

    securitySchemes["bearer"] = new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Parent access token (POST /api/v1/auth/login) or device token (POST /api/v1/devices/enroll)."
    };

    return Task.CompletedTask;
}));

// ---------------------------------------------------------------------------------------------
// 12. Health checks: liveness never touches the database, readiness always does.
// ---------------------------------------------------------------------------------------------
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy("API process is running."), tags: [LiveHealthTag])
    .AddCheck<DatabaseHealthCheck>("database", tags: [ReadyHealthTag]);

var app = builder.Build();

// ---------------------------------------------------------------------------------------------
// Pipeline.
// ---------------------------------------------------------------------------------------------
// First in the pipeline: everything downstream (rate-limiter partitions, HTTPS redirection, logs)
// must see the real client address rather than the reverse proxy's.
app.UseForwardedHeaders();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (!isDevelopment)
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    await next(context);
});

// The built admin panel is copied into wwwroot at deploy time. Serving it from the API process means
// the browser talks to a single origin (no CORS, no second vhost) and the reverse proxy forwards
// everything to one upstream. In development the panel is served by Vite instead, so wwwroot is absent
// and this whole block stays off.
var webRoot = app.Environment.WebRootPath
    ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot");
var adminPanelIndex = Path.Combine(webRoot, "index.html");
var servesAdminPanel = File.Exists(adminPanelIndex);

if (servesAdminPanel)
{
    app.UseDefaultFiles();
    app.UseStaticFiles(new StaticFileOptions
    {
        OnPrepareResponse = context =>
        {
            // Vite fingerprints asset filenames, so they can be cached forever. index.html must not be,
            // or a deploy leaves browsers pinned to the previous bundle.
            context.Context.Response.Headers.CacheControl =
                context.File.Name.Equals("index.html", StringComparison.OrdinalIgnoreCase)
                    ? "no-cache, no-store, must-revalidate"
                    : "public, max-age=31536000, immutable";
        }
    });
}

app.UseCors();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapAuthModule();
app.MapDevicesModule();
app.MapIngestionModule();
app.MapHistoryModule();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains(LiveHealthTag)
}).AllowAnonymous();

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains(ReadyHealthTag)
}).AllowAnonymous();

if (isDevelopment)
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

if (servesAdminPanel)
{
    // Client-side routes (/devices, /?device=...) must return the SPA shell. Endpoint routing matches
    // real endpoints first, so only genuinely unrouted paths land here - but an unknown /api or /health
    // path must still be an honest 404 rather than a page of HTML.
    app.MapFallback(async context =>
    {
        if (context.Request.Path.StartsWithSegments("/api")
            || context.Request.Path.StartsWithSegments("/health"))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
        await context.Response.SendFileAsync(adminPanelIndex);
    });
}

// ---------------------------------------------------------------------------------------------
// Schema and demo data. Development only: a production database is migrated deliberately by an
// operator before the new build starts serving traffic.
// ---------------------------------------------------------------------------------------------
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var startupLogger = app.Logger;
    var stoppingToken = app.Lifetime.ApplicationStopping;

    if (isDevelopment)
    {
        try
        {
            await db.Database.MigrateAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            startupLogger.LogError(ex,
                "Applying migrations failed. Is PostgreSQL running and reachable on the configured connection string?");
            throw;
        }
    }
    else
    {
        startupLogger.LogInformation(
            "Automatic migrations are disabled outside Development. Apply deploy/sql/migrations.sql (or run " +
            "\"dotnet ef database update\") before starting a build that changes the schema.");
    }

    // Bootstrap parent. Gated by Seed:Enabled and idempotent, so it is safe to leave enabled in
    // production: it creates the first administrator on a fresh database and does nothing afterwards.
    // Without it a production deployment has no account to sign in with, since self-registration is off.
    var seed = scope.ServiceProvider.GetRequiredService<IOptions<SeedOptions>>().Value;
    await DbSeeder.SeedAsync(
        db,
        new SeedSettings(seed.Enabled, seed.ParentEmail, seed.ParentPassword, seed.ParentDisplayName),
        PasswordHasher.Hash,
        startupLogger,
        stoppingToken);
}

await app.RunAsync();

namespace ParentalTrack.Api
{
    /// <summary>
    /// Readiness probe. Uses the provider's connectivity check rather than a query, so it stays cheap
    /// and does not depend on any particular table existing yet.
    /// </summary>
    internal sealed class DatabaseHealthCheck(AppDbContext db) : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                return await db.Database.CanConnectAsync(cancellationToken)
                    ? HealthCheckResult.Healthy("Database is reachable.")
                    : HealthCheckResult.Unhealthy("Database is not reachable.");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("Database connectivity check failed.", ex);
            }
        }
    }

    /// <summary>
    /// Writes every <see cref="DateTimeOffset"/> as ISO-8601 UTC with a literal <c>Z</c> and
    /// millisecond precision (<c>2026-08-25T10:15:30.123Z</c>), which is the wire format contract
    /// §0 mandates and every §2 example shows. The default round-trip form
    /// (<c>...1230000+00:00</c>) parses fine in both clients but is not what the contract pins, and
    /// it breaks the moment a consumer compares timestamps as strings.
    /// </summary>
    internal sealed class UtcDateTimeOffsetJsonConverter : JsonConverter<DateTimeOffset>
    {
        private const string Format = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var text = reader.GetString();
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new JsonException("Expected an ISO-8601 timestamp.");
            }

            // Offsets other than Z are accepted on the way in and normalised to UTC; a value with
            // no offset at all is read as UTC rather than as server-local time.
            return DateTimeOffset.Parse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
        }

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToUniversalTime().ToString(Format, CultureInfo.InvariantCulture));
    }
}

# ParentalTrack — Binding Implementation Contract (v1)

This file is the **single source of truth** for every component. Backend, Android app and
Admin web app are authored in parallel; they only fit together if this contract is followed
**exactly** (paths, names, casing, status codes). Do not "improve" names here — if something
is wrong, implement it as written and note it in the component README.

Repo root: `c:\location` (git-bash: `/c/location`)

```
c:\location
  backend\      ASP.NET Core 10 modular monolith (5 modules = 5 extractable services)
  android\      Kotlin child app (foreground location service)
  admin-web\    React + Vite + Leaflet parent dashboard
  docs\         this contract + architecture + runbook + privacy
```

---

## 0. Global rules

* Target framework: **net10.0**. Nullable enabled, implicit usings enabled.
* JSON: **camelCase** everywhere (System.Text.Json web defaults). No snake_case on the wire.
* Timestamps on the wire: **ISO-8601 UTC with `Z`**, millisecond precision (`2026-08-25T10:15:30.123Z`).
  All `DateTimeOffset` in C#; all stored as `timestamptz`.
* Database identifiers: **snake_case**, written explicitly via `ToTable("...")` / `HasColumnName("...")`
  in entity configurations so nothing is magic.
* IDs: `Guid` (uuid) for parents, devices, sessions. `long` (bigserial) for location records.
* Every error response uses RFC7807 `ProblemDetails` (`application/problem+json`).
* API version prefix: `/api/v1`.
* Ports: API `http://localhost:5080` and `https://localhost:7443`.
  Web dev server `http://localhost:5173`. Android emulator reaches the API at `http://10.0.2.2:5080` (debug only).

### Root namespace map

| Project | Namespace root |
|---|---|
| `backend/src/ParentalTrack.Domain` | `ParentalTrack.Domain` |
| `backend/src/ParentalTrack.Infrastructure` | `ParentalTrack.Infrastructure` |
| `backend/src/ParentalTrack.Api` | `ParentalTrack.Api` |

Android package root: `com.parentaltrack.child`

---

## 1. Domain model (backend/src/ParentalTrack.Domain)

All entities live in `ParentalTrack.Domain.Entities`. Enums in `ParentalTrack.Domain.Enums`.

### Parent → table `parents`
| C# property | type | column | notes |
|---|---|---|---|
| Id | Guid | id | PK |
| Email | string | email | as typed by user, max 256 |
| EmailNormalized | string | email_normalized | `Email.Trim().ToLowerInvariant()`, **unique index** `ix_parents_email_normalized` |
| PasswordHash | string | password_hash | PBKDF2 format, see §4.1 |
| DisplayName | string | display_name | max 128 |
| IsActive | bool | is_active | default true |
| CreatedAt | DateTimeOffset | created_at | |
| Devices | ICollection&lt;ChildDevice&gt; | – | nav |

### RefreshToken → table `refresh_tokens`
| C# property | type | column | notes |
|---|---|---|---|
| Id | Guid | id | PK |
| ParentId | Guid | parent_id | FK → parents, cascade delete, index |
| TokenHash | string | token_hash | SHA-256 base64 of the opaque token, **unique index** |
| ExpiresAt | DateTimeOffset | expires_at | |
| CreatedAt | DateTimeOffset | created_at | |
| RevokedAt | DateTimeOffset? | revoked_at | null = active |

### ChildDevice → table `child_devices`
| C# property | type | column | notes |
|---|---|---|---|
| Id | Guid | id | PK |
| ParentId | Guid | parent_id | FK → parents, cascade delete, index `ix_child_devices_parent_id` |
| ChildName | string | child_name | max 128, required |
| DeviceLabel | string? | device_label | max 128, e.g. "Pixel 7" |
| Platform | string? | platform | "android" |
| Manufacturer | string? | manufacturer | max 64 |
| Model | string? | model | max 64 |
| OsVersion | string? | os_version | max 32 |
| AppVersion | string? | app_version | max 32 |
| InstallId | string? | install_id | max 64, app-generated random id |
| PairingCodeHash | string? | pairing_code_hash | SHA-256 base64; null once consumed |
| PairingCodeExpiresAt | DateTimeOffset? | pairing_code_expires_at | |
| PairedAt | DateTimeOffset? | paired_at | null = never enrolled |
| IsActive | bool | is_active | soft-disable |
| CreatedAt | DateTimeOffset | created_at | |
| LastSeenAt | DateTimeOffset? | last_seen_at | updated on every accepted ingest (max recordedAt) |
| LastBatteryPercent | int? | last_battery_percent | |
| LastLocationId | long? | last_location_id | denormalised pointer to newest `location_records` row (plain column + index, no FK constraint) |

### DeviceSession → table `device_sessions`
| C# property | type | column | notes |
|---|---|---|---|
| Id | Guid | id | PK — this value is the JWT `jti` |
| DeviceId | Guid | device_id | FK → child_devices, cascade delete, index |
| IssuedAt | DateTimeOffset | issued_at | |
| ExpiresAt | DateTimeOffset | expires_at | |
| RevokedAt | DateTimeOffset? | revoked_at | |
| RevokedReason | string? | revoked_reason | max 128 |
| EnrolledUserAgent | string? | enrolled_user_agent | max 256 |

### LocationRecord → table `location_records`
| C# property | type | column | notes |
|---|---|---|---|
| Id | long | id | PK, bigserial (`ValueGeneratedOnAdd`) |
| DeviceId | Guid | device_id | FK → child_devices, cascade delete |
| ClientId | Guid | client_id | idempotency key generated on device |
| Latitude | double | latitude | |
| Longitude | double | longitude | |
| AccuracyMeters | double | accuracy_meters | |
| AltitudeMeters | double? | altitude_meters | |
| SpeedMetersPerSecond | double? | speed_mps | |
| BearingDegrees | double? | bearing_degrees | |
| BatteryPercent | int? | battery_percent | 0..100 |
| IsCharging | bool? | is_charging | |
| Provider | LocationProvider | provider | stored as `smallint` via `HasConversion<short>()` |
| RecordedAt | DateTimeOffset | recorded_at | device clock, when the fix was taken |
| ReceivedAt | DateTimeOffset | received_at | server clock |

Indexes (exact names):
* `ix_location_records_device_id_client_id` — **UNIQUE** on (device_id, client_id) ← idempotency
* `ix_location_records_device_id_recorded_at` on (device_id, recorded_at DESC)
* `ix_child_devices_parent_id`
* `ix_parents_email_normalized` — UNIQUE

### Enums
```csharp
public enum LocationProvider : short { Unknown = 0, Gps = 1, Network = 2, Fused = 3, Passive = 4 }
public enum DeviceStatus { NeverReported = 0, Online = 1, Idle = 2, Offline = 3 }
```
Wire representation: **camelCase strings** — `"unknown" | "gps" | "network" | "fused" | "passive"`,
`"neverReported" | "online" | "idle" | "offline"` (register `JsonStringEnumConverter(JsonNamingPolicy.CamelCase)`).

### Status / staleness rule (ONE definition, server-computed)
`ParentalTrack.Domain.DeviceStatusCalculator` — pure static, used by every read path:

```csharp
public static DeviceStatus Evaluate(DateTimeOffset? lastSeenAt, DateTimeOffset now,
                                    int onlineThresholdSeconds, int staleThresholdSeconds)
```
* `lastSeenAt == null` → `NeverReported`
* age &lt;= onlineThreshold (default **180 s**) → `Online`
* age &lt;= staleThreshold (default **600 s**) → `Idle`
* otherwise → `Offline`

`isStale == status is Offline or NeverReported`. The API returns `status`, `isStale`,
`secondsSinceUpdate` so the UI never re-derives thresholds (it may re-tick the clock locally
using the thresholds returned by `GET /api/v1/config`).

---

## 2. HTTP API (exact)

Auth schemes (see §4):
* `parent` — `Authorization: Bearer <parentAccessToken>` (policy name `ParentPolicy`)
* `device` — `Authorization: Bearer <deviceToken>` (policy name `DevicePolicy`)
* `anon` — no auth (rate limited)

### 2.1 Auth module — `/api/v1/auth`

| Method | Path | Auth | Body | 2xx |
|---|---|---|---|---|
| POST | `/api/v1/auth/register` | anon | `RegisterRequest` | 201 `AuthResponse` |
| POST | `/api/v1/auth/login` | anon | `LoginRequest` | 200 `AuthResponse` |
| POST | `/api/v1/auth/refresh` | anon | `RefreshRequest` | 200 `AuthResponse` |
| POST | `/api/v1/auth/logout` | anon | `RefreshRequest` | 204 |
| GET | `/api/v1/auth/me` | parent | – | 200 `ParentDto` |

```jsonc
// RegisterRequest
{ "email": "parent@example.com", "password": "min 10 chars", "displayName": "Alex" }
// LoginRequest
{ "email": "parent@example.com", "password": "..." }
// RefreshRequest
{ "refreshToken": "opaque-base64url" }
// AuthResponse
{
  "accessToken": "eyJ...",
  "refreshToken": "opaque-base64url",
  "expiresAtUtc": "2026-08-25T11:15:30.000Z",
  "parent": { "id": "uuid", "email": "parent@example.com", "displayName": "Alex" }
}
// ParentDto
{ "id": "uuid", "email": "...", "displayName": "...", "createdAt": "...Z" }
```

Registration is gated by `Auth:AllowSelfRegistration` (default `true` in Development, `false`
otherwise → 403 ProblemDetails).

### 2.2 Device enrollment — device-facing, lives in the **Devices** module

| Method | Path | Auth | Body | 2xx |
|---|---|---|---|---|
| POST | `/api/v1/devices/enroll` | anon (rate-limited `enroll`: 5/min/IP) | `EnrollRequest` | 200 `EnrollResponse` |
| GET | `/api/v1/devices/me` | device | – | 200 `DeviceSelfDto` |

```jsonc
// EnrollRequest
{ "pairingCode": "AB3D-9KMP", "installId": "...", "manufacturer": "Google",
  "model": "Pixel 7", "osVersion": "14", "appVersion": "1.0.0" }
// EnrollResponse
{ "deviceId": "uuid", "childName": "Sam", "deviceToken": "eyJ...",
  "tokenExpiresAtUtc": "2027-08-25T00:00:00.000Z",
  "tracking": { "intervalSeconds": 60, "fastestIntervalSeconds": 30,
                "minDistanceMeters": 25, "batchMaxSize": 100, "uploadIntervalSeconds": 120 } }
// DeviceSelfDto
{ "deviceId": "uuid", "childName": "Sam", "isActive": true,
  "tracking": { "intervalSeconds": 60, "fastestIntervalSeconds": 30,
                "minDistanceMeters": 25, "batchMaxSize": 100, "uploadIntervalSeconds": 120 } }
```
**Route ordering note:** `/api/v1/devices/enroll` and `/api/v1/devices/me` must be registered so they
are not captured by `/api/v1/devices/{deviceId}` — use a `Guid` route constraint
(`{deviceId:guid}`) on all parent device routes.

Pairing code: 8 chars from alphabet `ABCDEFGHJKLMNPQRSTUVWXYZ23456789` (no I/O/0/1), displayed as
`XXXX-XXXX`. The server accepts it with or without the dash, case-insensitively (normalise by
uppercasing and stripping non-alphanumerics before hashing). Codes expire after
`Devices:PairingCodeTtlMinutes` (default 60) and are single-use (hash cleared on success).

Failure: unknown/expired/consumed code → `400` ProblemDetails, `title: "Invalid pairing code"`.

### 2.3 Device management — `/api/v1/devices` (parent)

| Method | Path | Body | 2xx |
|---|---|---|---|
| GET | `/api/v1/devices` | – | 200 `DeviceSummaryDto[]` |
| POST | `/api/v1/devices` | `CreateDeviceRequest` | 201 `DeviceDetailDto` (includes plaintext `pairingCode`) |
| GET | `/api/v1/devices/{deviceId:guid}` | – | 200 `DeviceDetailDto` (`pairingCode` = null) |
| PATCH | `/api/v1/devices/{deviceId:guid}` | `UpdateDeviceRequest` | 200 `DeviceDetailDto` |
| DELETE | `/api/v1/devices/{deviceId:guid}` | – | 204 (hard delete, cascades locations) |
| POST | `/api/v1/devices/{deviceId:guid}/pairing-code` | – | 200 `PairingCodeDto` |
| POST | `/api/v1/devices/{deviceId:guid}/revoke` | – | 204 (revokes all sessions; device gets 401 next call) |

Every parent-scoped route MUST filter by `parentId` taken from the JWT. A device belonging to
another parent returns **404** (never 403 — do not leak existence).

```jsonc
// CreateDeviceRequest
{ "childName": "Sam", "deviceLabel": "Sam's phone" }
// UpdateDeviceRequest  (all optional)
{ "childName": "Sam", "deviceLabel": "Sam's phone", "isActive": true }
// PairingCodeDto
{ "pairingCode": "AB3D-9KMP", "expiresAtUtc": "2026-08-25T11:15:30.000Z" }

// DeviceSummaryDto
{
  "id": "uuid", "childName": "Sam", "deviceLabel": "Sam's phone",
  "platform": "android", "model": "Pixel 7", "isActive": true,
  "isPaired": true, "hasActiveSession": true,
  "status": "online", "isStale": false,
  "lastSeenAt": "2026-08-25T10:15:30.000Z", "secondsSinceUpdate": 42,
  "batteryPercent": 78,
  "lastLocation": null
}
// DeviceDetailDto = DeviceSummaryDto + these extra members:
//   "createdAt": "...Z", "pairedAt": "...Z"|null,
//   "pairingCode": "AB3D-9KMP"|null, "pairingCodeExpiresAtUtc": "...Z"|null,
//   "appVersion": "1.0.0"|null, "osVersion": "14"|null
```
`lastLocation` is a `LocationPointDto` or `null`.

### 2.4 Ingestion — `/api/v1/ingest` (device)

| Method | Path | Auth | Body | 2xx |
|---|---|---|---|---|
| POST | `/api/v1/ingest/locations` | device (rate-limited `ingest`) | `IngestRequest` | **202** `IngestResponse` |

```jsonc
// IngestRequest — batch so the app can flush its offline queue
{ "points": [ {
    "clientId": "uuid",
    "latitude": 12.9716, "longitude": 77.5946,
    "accuracyMeters": 8.5,
    "altitudeMeters": 910.2,
    "speedMetersPerSecond": 1.4,
    "bearingDegrees": 173.0,
    "batteryPercent": 78,
    "isCharging": false,
    "provider": "gps",
    "recordedAt": "2026-08-25T10:15:30.123Z"
} ] }
// IngestResponse
{ "accepted": 12, "duplicates": 1, "rejected": 0, "serverTimeUtc": "2026-08-25T10:15:31.000Z" }
```
Rules:
* Max **200** points per request (`Ingestion:MaxBatchSize`); more → 400.
* Per-point validation: lat ∈ [-90,90], lon ∈ [-180,180], accuracy ∈ [0,10000], battery ∈ [0,100],
  `recordedAt` not older than 24 h and not more than 5 min in the future (clock skew).
  Invalid points are counted in `rejected` and skipped — **never fail the whole batch**
  (the device would retry forever).
* Duplicates (same `device_id` + `client_id`) are silently counted in `duplicates`.
* Returns **202** as soon as the batch is queued to the in-process channel; a `BackgroundService`
  performs the DB write. Channel is bounded (`Ingestion:QueueCapacity`, default 10 000) with
  `BoundedChannelFullMode.Wait`; if it cannot be enqueued within 2 s → **503** so the device retries.
* Revoked / inactive device → 401.

### 2.5 History & current location — parent

| Method | Path | 2xx |
|---|---|---|
| GET | `/api/v1/devices/{deviceId:guid}/location/current` | 200 `LocationSnapshotDto` \| **204** if never reported |
| GET | `/api/v1/devices/{deviceId:guid}/locations` | 200 `LocationHistoryResponse` |

History query params: `from` (ISO-8601, default `now-24h`), `to` (default `now`),
`limit` (default 1000, max 5000), `order` (`asc`\|`desc`, default `asc`),
`minAccuracyMeters` (optional — drop fixes worse than this), `simplify` (bool, default false —
server-side even-stride downsample down to `limit` points, keeping first and last).

```jsonc
// LocationPointDto
{ "id": 1234, "latitude": 12.9716, "longitude": 77.5946, "accuracyMeters": 8.5,
  "altitudeMeters": null, "speedMetersPerSecond": 1.4, "bearingDegrees": 173.0,
  "batteryPercent": 78, "isCharging": false, "provider": "gps",
  "recordedAt": "2026-08-25T10:15:30.123Z", "receivedAt": "2026-08-25T10:15:31.000Z" }

// LocationSnapshotDto
{ "deviceId": "uuid", "childName": "Sam", "status": "online", "isStale": false,
  "secondsSinceUpdate": 42, "serverTimeUtc": "2026-08-25T10:16:12.000Z",
  "location": null }

// LocationHistoryResponse
{ "deviceId": "uuid", "childName": "Sam",
  "fromUtc": "2026-08-24T10:00:00.000Z", "toUtc": "2026-08-25T10:00:00.000Z",
  "count": 250, "totalMatched": 812, "simplified": true,
  "distanceMeters": 4210.5,
  "points": [] }
```
`distanceMeters` = haversine sum over the returned points (`GeoMath.HaversineMeters`).

### 2.6 Config & health

| Method | Path | Auth | Response |
|---|---|---|---|
| GET | `/api/v1/config` | parent | `{ "onlineThresholdSeconds":180, "staleThresholdSeconds":600, "defaultRefreshSeconds":15, "mapTileUrl":"https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png", "mapAttribution":"(c) OpenStreetMap contributors" }` |
| GET | `/health/live` | anon | 200 |
| GET | `/health/ready` | anon | 200 / 503 (DB check) |

`/api/v1/config` is served by the History module (`MapHistoryModule`).

---

## 3. Backend project layout (exact paths)

```
backend/
  ParentalTrack.sln
  Directory.Build.props
  docker-compose.yml
  Dockerfile
  README.md
  requests.http
  src/
    ParentalTrack.Domain/
      ParentalTrack.Domain.csproj
      Entities/{Parent,RefreshToken,ChildDevice,DeviceSession,LocationRecord}.cs
      Enums/{LocationProvider,DeviceStatus}.cs
      DeviceStatusCalculator.cs
      GeoMath.cs                      // static double HaversineMeters(lat1,lon1,lat2,lon2)
    ParentalTrack.Infrastructure/
      ParentalTrack.Infrastructure.csproj
      Persistence/AppDbContext.cs
      Persistence/Configurations/*.cs          // one IEntityTypeConfiguration per entity
      Persistence/AppDbContextFactory.cs       // design-time factory for `dotnet ef`
      Persistence/DbSeeder.cs                  // dev seed parent from config
      DependencyInjection.cs                   // AddInfrastructure(IServiceCollection, IConfiguration)
      Migrations/                              // generated by dotnet ef
    ParentalTrack.Api/
      ParentalTrack.Api.csproj
      Program.cs
      appsettings.json
      appsettings.Development.json
      Options/{JwtOptions,TrackingOptions,IngestionOptions,DevicesOptions,SeedOptions,CorsOptions}.cs
      Common/{ApiResults.cs,CurrentUser.cs,ValidationExtensions.cs}
      Security/{PasswordHasher.cs,TokenService.cs,DeviceSessionValidator.cs,AuthConstants.cs}
      Modules/Auth/{AuthModule.cs,AuthEndpoints.cs,AuthService.cs,AuthDtos.cs}
      Modules/Devices/{DevicesModule.cs,DeviceEndpoints.cs,DeviceService.cs,EnrollmentEndpoints.cs,EnrollmentService.cs,DeviceDtos.cs}
      Modules/Ingestion/{IngestionModule.cs,IngestionEndpoints.cs,LocationIngestQueue.cs,LocationIngestWorker.cs,LocationRetentionWorker.cs,IngestionDtos.cs}
      Modules/History/{HistoryModule.cs,HistoryEndpoints.cs,HistoryService.cs,HistoryDtos.cs}
```

Each module exposes exactly these two extension methods (the composition root calls them):

```csharp
namespace ParentalTrack.Api.Modules.Auth;
public static class AuthModule {
    public static IServiceCollection AddAuthModule(this IServiceCollection services, IConfiguration config);
    public static IEndpointRouteBuilder MapAuthModule(this IEndpointRouteBuilder app);
}
```
…and identically `AddDevicesModule`/`MapDevicesModule`, `AddIngestionModule`/`MapIngestionModule`,
`AddHistoryModule`/`MapHistoryModule`.

Endpoints are **Minimal APIs** grouped with `MapGroup`. No MVC controllers.

Cross-module type ownership (do NOT redefine these elsewhere):
* `ParentalTrack.Api.Modules.Devices.LocationPointDto` — used by Devices, History and Ingestion.
* `ParentalTrack.Api.Modules.Devices.TrackingConfigDto` — used by Devices (enroll) and anywhere else needed.
* `ParentalTrack.Api.Security.AuthConstants` — policy names `ParentPolicy` / `DevicePolicy`,
  claim names `typ`, `pid`, rate-limiter policy names `login` / `enroll` / `ingest`.
* `ParentalTrack.Api.Common.CurrentUser` — extension methods
  `Guid GetParentId(this ClaimsPrincipal)`, `Guid GetDeviceId(this ClaimsPrincipal)`,
  `Guid GetSessionId(this ClaimsPrincipal)`.

Configuration keys (appsettings.json):
```jsonc
{
  "ConnectionStrings": { "Postgres": "Host=localhost;Port=5432;Database=parentaltrack;Username=parentaltrack;Password=parentaltrack" },
  "Jwt": { "Issuer": "parentaltrack", "ParentAudience": "parentaltrack.admin", "DeviceAudience": "parentaltrack.device",
           "SigningKey": "", "ParentAccessTokenMinutes": 60, "RefreshTokenDays": 30, "DeviceTokenDays": 365 },
  "Tracking": { "OnlineThresholdSeconds": 180, "StaleThresholdSeconds": 600, "DefaultRefreshSeconds": 15,
                "IntervalSeconds": 60, "FastestIntervalSeconds": 30, "MinDistanceMeters": 25,
                "BatchMaxSize": 100, "UploadIntervalSeconds": 120,
                "MapTileUrl": "https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png",
                "MapAttribution": "(c) OpenStreetMap contributors" },
  "Ingestion": { "MaxBatchSize": 200, "QueueCapacity": 10000, "WriteBatchSize": 200,
                 "FlushIntervalMilliseconds": 500, "RetentionDays": 90 },
  "Devices": { "PairingCodeTtlMinutes": 60 },
  "Auth": { "AllowSelfRegistration": false },
  "Seed": { "Enabled": false, "ParentEmail": "parent@example.com", "ParentPassword": "ChangeMe123!", "ParentDisplayName": "Demo Parent" },
  "Cors": { "AllowedOrigins": [ "http://localhost:5173" ] }
}
```
`appsettings.Development.json` overrides: `Jwt:SigningKey` = a 64-char dev key,
`Auth:AllowSelfRegistration` = true, `Seed:Enabled` = true.

`Jwt:SigningKey` MUST come from configuration/env (`Jwt__SigningKey`) and the app MUST refuse to
start outside Development when it is missing or shorter than 32 bytes.

---

## 4. Security

### 4.1 Passwords
`ParentalTrack.Api.Security.PasswordHasher` — PBKDF2-HMAC-SHA256, 210 000 iterations,
16-byte salt, 32-byte subkey. Stored format: `pbkdf2-sha256$<iterations>$<saltB64>$<hashB64>`.
Verify with `CryptographicOperations.FixedTimeEquals`.

### 4.2 Tokens
Two JWT audiences, one signing key, `HmacSha256`.

* Parent access token claims: `sub` = parent id, `email`, `name` = display name,
  `aud` = `Jwt:ParentAudience`, custom claim `typ` = `parent`.
* Device token claims: `sub` = device id, `jti` = **DeviceSession.Id**, `pid` = parent id,
  `aud` = `Jwt:DeviceAudience`, custom claim `typ` = `device`.

Refresh tokens are opaque 32 random bytes, base64url, stored **hashed** (SHA-256), rotated on every
refresh (old one revoked). Reuse of an already-revoked refresh token → 401 **and** revoke every
active refresh token for that parent (breach response).

Authorization: one JWT bearer scheme whose `TokenValidationParameters.ValidAudiences` contains both
audiences; `ParentPolicy` requires claim `typ == parent`, `DevicePolicy` requires `typ == device`.
`ClockSkew = TimeSpan.FromSeconds(30)`.

**Revocation**: `DeviceSessionValidator` runs in `JwtBearerEvents.OnTokenValidated` for device
tokens: looks up `device_sessions` by `jti` (cached in `IMemoryCache`, 30 s TTL, key
`devsess:{jti}`). Revoked/expired session, missing session, or `ChildDevice.IsActive == false`
→ `context.Fail(...)` → 401. `POST /devices/{id}/revoke` sets `revoked_at` on all of that device's
sessions **and evicts the cache entries immediately**.

### 4.3 Transport & headers
* HTTPS redirection + HSTS outside Development.
* CORS: only `Cors:AllowedOrigins`, `AllowCredentials = false` (bearer tokens, not cookies).
* Rate limiting (`Microsoft.AspNetCore.RateLimiting`, fixed window): `login` 10/min/IP,
  `enroll` 5/min/IP, `ingest` 120/min partitioned by the device `sub` claim.
* No location endpoint is anonymous. `/api/v1/config` requires parent auth.
* Response header hygiene: `X-Content-Type-Options: nosniff`.

### 4.4 Data minimisation
Only the fields in §1 are stored. No contacts, SMS, call logs, media, audio, or app inventory
anywhere in the system. Location retention: `LocationRetentionWorker` (Ingestion module) deletes
rows older than `Ingestion:RetentionDays` (default 90) every 6 h, logging the deleted count.

---

## 5. Android app (`android/`)

Kotlin 2.x, AGP 8.7+, Gradle KTS + **version catalog** (`gradle/libs.versions.toml`),
Jetpack Compose (Material 3), `minSdk 24`, `targetSdk 35`, `compileSdk 35`,
`applicationId "com.parentaltrack.child"`, versionName `1.0.0`.

No Hilt — use a small hand-rolled `ServiceLocator` object so the build needs no extra annotation
processors. Room is the **only** KSP consumer.

```
android/
  settings.gradle.kts  build.gradle.kts  gradle.properties  gradle/libs.versions.toml
  README.md  .gitignore
  app/
    build.gradle.kts  proguard-rules.pro
    src/main/AndroidManifest.xml
    src/main/res/values/{strings.xml,themes.xml}
    src/main/res/xml/network_security_config.xml
    src/main/res/drawable/ic_stat_location.xml
    src/main/java/com/parentaltrack/child/
      ChildApp.kt                       // Application + WorkManager Configuration.Provider
      MainActivity.kt
      di/ServiceLocator.kt
      data/local/{AppDatabase.kt,PendingLocationDao.kt,PendingLocationEntity.kt}
      data/prefs/{SecurePrefs.kt,TrackingPrefs.kt}
      data/remote/{ApiClient.kt,TrackingApi.kt,Dtos.kt,AuthInterceptor.kt}
      data/repo/{EnrollmentRepository.kt,LocationRepository.kt}
      location/{LocationCollector.kt,BatteryReader.kt,ProviderMapper.kt}
      service/{LocationTrackingService.kt,TrackingNotification.kt,BootReceiver.kt,TrackingController.kt}
      work/{LocationUploadWorker.kt,UploadScheduler.kt}
      ui/{ConsentScreen.kt,PairingScreen.kt,PermissionScreen.kt,StatusScreen.kt,AppNavHost.kt,Theme.kt,MainViewModel.kt}
```

### 5.1 Permissions in the manifest (nothing else)
```xml
<uses-permission android:name="android.permission.INTERNET"/>
<uses-permission android:name="android.permission.ACCESS_NETWORK_STATE"/>
<uses-permission android:name="android.permission.ACCESS_COARSE_LOCATION"/>
<uses-permission android:name="android.permission.ACCESS_FINE_LOCATION"/>
<uses-permission android:name="android.permission.ACCESS_BACKGROUND_LOCATION"/>
<uses-permission android:name="android.permission.FOREGROUND_SERVICE"/>
<uses-permission android:name="android.permission.FOREGROUND_SERVICE_LOCATION"/>
<uses-permission android:name="android.permission.POST_NOTIFICATIONS"/>
<uses-permission android:name="android.permission.RECEIVE_BOOT_COMPLETED"/>
```
Service declaration:
```xml
<service android:name=".service.LocationTrackingService"
         android:foregroundServiceType="location"
         android:exported="false"
         android:stopWithTask="false"/>
```
The launcher activity MUST stay visible — no icon hiding, no device-admin, no accessibility
service, no `QUERY_ALL_PACKAGES`, no other data collection.

### 5.2 Consent + permission flow (order matters)
1. **ConsentScreen** on first launch — plain-language explanation: *this device's location is shared
   with the parent account it is paired to, continuously, including in the background; a permanent
   notification is shown while sharing is on; sharing can be stopped from the app or the
   notification*. Explicit "I understand and agree" button; store `consentAcceptedAt` in prefs.
   Nothing may start before consent.
2. **PairingScreen** — enter the 8-char pairing code → `POST /api/v1/devices/enroll`;
   store `deviceToken` in `EncryptedSharedPreferences`.
3. **PermissionScreen** — request in this exact sequence, each with an in-app rationale first:
   1. `POST_NOTIFICATIONS` (API 33+)
   2. `ACCESS_FINE_LOCATION` + `ACCESS_COARSE_LOCATION` (foreground)
   3. **only after (2) is granted**, `ACCESS_BACKGROUND_LOCATION` as a *separate* request (API 29+).
      On API 30+ the system dialog is not shown — deep-link to
      `Settings.ACTION_APPLICATION_DETAILS_SETTINGS` and tell the user to choose
      "Allow all the time", using `packageManager.backgroundPermissionOptionLabel` (API 30+) for
      the exact on-device wording.
   4. Optional and clearly skippable: battery-optimisation exemption via
      `ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS`.
   Degrade gracefully: foreground-only permission still tracks while the service runs; show a
   warning banner explaining what is missing and what it costs.
4. **StatusScreen** — Start/Stop switch, permission states, last fix time, last successful upload
   time, pending-queue count, paired child name, and "Unpair & delete token".

### 5.3 Tracking
`play-services-location` `FusedLocationProviderClient` with
`LocationRequest.Builder(Priority.PRIORITY_HIGH_ACCURACY, intervalMs)`,
`setMinUpdateIntervalMillis(fastest)`, `setMinUpdateDistanceMeters(minDistance)`,
`setWaitForAccurateLocation(false)`. Interval/distance come from the `tracking` block of
`EnrollResponse`, persisted in `TrackingPrefs` (defaults 60 s / 25 m).

`LocationTrackingService`:
* Calls `ServiceCompat.startForeground(this, NOTIF_ID, notification, FOREGROUND_SERVICE_TYPE_LOCATION)`
  **within 5 s** of `onStartCommand`, before requesting updates.
* Notification channel `location_tracking`, `IMPORTANCE_LOW`, ongoing, text
  "Location sharing is on — your parent can see this device's location", a **Stop sharing** action
  and a content intent to `MainActivity`.
* Catches `ForegroundServiceStartNotAllowedException` (API 31+) and surfaces it in the UI.
* `START_STICKY`. `BootReceiver` restarts the service after `BOOT_COMPLETED` **only if**
  `TrackingPrefs.trackingEnabled == true` and consent + permissions are present.
* Every fix → insert `PendingLocationEntity` in Room, then `UploadScheduler.requestUpload()`.

### 5.4 Offline queue + retry
Room table `pending_locations(id INTEGER PK autoGenerate, clientId TEXT, latitude REAL, longitude REAL,
accuracyMeters REAL, altitudeMeters REAL?, speedMps REAL?, bearingDeg REAL?, batteryPercent INTEGER?,
isCharging INTEGER?, provider TEXT, recordedAtEpochMillis INTEGER, attemptCount INTEGER)`.

`LocationUploadWorker` (`CoroutineWorker`):
* Unique periodic work `location-upload-periodic` every 15 min (`ExistingPeriodicWorkPolicy.KEEP`)
  **plus** a unique one-shot `location-upload-now` (`ExistingWorkPolicy.KEEP`) after each fix.
* `Constraints`: `NetworkType.CONNECTED`.
* Reads up to `batchMaxSize` rows ordered by `recordedAtEpochMillis`, POSTs
  `/api/v1/ingest/locations`, deletes those rows on `202` (accepted *and* duplicate *and* rejected —
  the server already told us it will never take them), retries on 5xx / IO with
  `BackoffPolicy.EXPONENTIAL` 30 s.
* On **401** → clear the token, stop the service, set `TrackingPrefs.revoked = true`, post a
  user-visible notification "Location sharing was turned off by your parent", and reflect it in the UI.
* Queue capped at 10 000 rows — oldest dropped beyond that.
* `recordedAt` serialised as ISO-8601 UTC from `recordedAtEpochMillis`.

### 5.5 Networking
Retrofit + OkHttp + `kotlinx.serialization` (`Json { ignoreUnknownKeys = true; explicitNulls = false }`).
Base URL from `BuildConfig.API_BASE_URL` set in `build.gradle.kts`: `debug` → `"http://10.0.2.2:5080/"`,
`release` → `"https://api.example.com/"` (documented as the single value to change).
`network_security_config.xml` permits cleartext **only** for `10.0.2.2` and `localhost`; release
builds set `cleartextTrafficPermitted="false"`. `AuthInterceptor` adds
`Authorization: Bearer <deviceToken>` from `SecurePrefs`.

---

## 6. Admin web (`admin-web/`)

React 19 + TypeScript + Vite, `react-router-dom` 7, `@tanstack/react-query` 5,
`leaflet` 1.9 + `react-leaflet` 5. Plain CSS — no Tailwind or component framework.
`npm run build` (`tsc -b && vite build`) must pass with zero errors.

```
admin-web/
  package.json  tsconfig.json  tsconfig.app.json  tsconfig.node.json  vite.config.ts  index.html
  .env.example  README.md  .gitignore
  src/
    main.tsx  App.tsx  styles.css  vite-env.d.ts
    api/{client.ts,auth.ts,devices.ts,locations.ts,types.ts}
    auth/{AuthContext.tsx,RequireAuth.tsx}
    hooks/{useDevices.ts,useCurrentLocation.ts,useHistory.ts,useConfig.ts,useNow.ts}
    components/{Layout.tsx,DeviceList.tsx,DeviceCard.tsx,StatusBadge.tsx,MapPanel.tsx,
                HistoryControls.tsx,StaleBanner.tsx,Spinner.tsx}
    pages/{LoginPage.tsx,DashboardPage.tsx,DevicesPage.tsx}
    lib/{time.ts,format.ts}
```

* `vite.config.ts` proxies `/api` → `http://localhost:5080` in dev; production base URL from
  `import.meta.env.VITE_API_BASE_URL` (default `/api`).
* Access token in memory + refresh token in `localStorage` under key `pt.refreshToken`; the single
  `client.ts` fetch wrapper transparently refreshes once on 401, then redirects to `/login`.
* `types.ts` mirrors §2 DTOs exactly (camelCase, same nullability).
* Dashboard: device list on the left, map on the right. Selecting a device shows its marker plus an
  accuracy circle, and the history path when a range is chosen.
* Auto-refresh current locations every `defaultRefreshSeconds` (from `/api/v1/config`, default 15 s)
  via React Query `refetchInterval`; pause when `document.hidden`.
* Stale handling: marker turns grey with a dashed accuracy circle and a `StaleBanner` reads
  "Last known location — device offline since &lt;relative time&gt;". **Never hide the last known fix.**
* History: from/to `datetime-local` inputs plus quick ranges (Last 1 h / 6 h / 24 h / 7 d), calls
  `/locations?from&to&limit=2000&simplify=true`, draws a `Polyline` with start/end markers and shows
  total distance + point count. Empty range → explicit "No location data in this range".
* Devices page: create a child device (show the returned pairing code large, with a copy button and
  its expiry), regenerate code, revoke device, delete device (with confirm).
* Basics: labelled inputs, visible focus states, works down to 1024 px wide; relative times
  ("42 s ago") re-rendered from a shared `useNow(1000)` tick.
* Leaflet marker icon fix is required (default icon URLs break under bundlers) — build
  `L.divIcon` or set `L.Icon.Default` image paths explicitly from imported assets.

---

## 7. Explicitly OUT of scope for this MVP
Geofencing, parent alerts/notifications, multi-parent sharing, iOS, push messaging, marker
clustering, admin roles, audit-log UI, extensive test suites, i18n.
Do not add them. Leave the seams (module boundaries, options) so they can be added later.

---

## 8. Pre-written shared files (DO NOT rewrite or move)

These already exist and are the shared vocabulary every backend agent must consume as-is:

* `backend/src/ParentalTrack.Api/Options/AppOptions.cs` — contains **all** options classes in one
  file: `JwtOptions`, `TrackingOptions`, `IngestionOptions`, `DevicesOptions`, `AuthOptions`,
  `SeedOptions`, `CorsOptions`. Each has a `public const string SectionName`.
  (This replaces the per-file `Options/{...}.cs` listing in §3.)
* `backend/src/ParentalTrack.Api/Security/AuthConstants.cs` — policy names, claim names, token type
  values, rate-limiter policy names, `DeviceSessionCacheKey(Guid)`.
* `backend/src/ParentalTrack.Api/Common/CurrentUser.cs` — `GetParentId`, `GetDeviceId`,
  `GetSessionId`, `GetDeviceParentId`, `TryGetGuid` extension methods on `ClaimsPrincipal`.

Consequence: the JWT bearer handler MUST be configured with `MapInboundClaims = false` so raw
`sub` / `jti` claim names survive.

## 9. Fixed cross-agent signatures (backend)

Implement these exactly — other agents call them:

```csharp
// ParentalTrack.Infrastructure
namespace ParentalTrack.Infrastructure;
public static class DependencyInjection {
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration);
}

namespace ParentalTrack.Infrastructure.Persistence;
public sealed class AppDbContext : DbContext {
    public DbSet<Parent> Parents { get; }
    public DbSet<RefreshToken> RefreshTokens { get; }
    public DbSet<ChildDevice> ChildDevices { get; }
    public DbSet<DeviceSession> DeviceSessions { get; }
    public DbSet<LocationRecord> LocationRecords { get; }
}

public sealed record SeedSettings(bool Enabled, string ParentEmail, string ParentPassword, string ParentDisplayName);
public static class DbSeeder {
    // hashPassword is injected so Infrastructure does not depend on the Api project.
    public static Task SeedAsync(AppDbContext db, SeedSettings settings,
                                 Func<string, string> hashPassword, ILogger logger, CancellationToken ct);
}

// ParentalTrack.Api.Security
public static class PasswordHasher {
    public static string Hash(string password);
    public static bool Verify(string password, string encodedHash);
}

public sealed class TokenService {                 // registered as singleton, ctor takes IOptions<JwtOptions>
    public (string Token, DateTimeOffset ExpiresAt) CreateParentAccessToken(Parent parent);
    public (string Token, DateTimeOffset ExpiresAt) CreateDeviceToken(Guid deviceId, Guid parentId, Guid sessionId);
    public static string CreateOpaqueToken();      // 32 random bytes, base64url
    public static string HashToken(string token);  // SHA-256, base64
}

public sealed class DeviceSessionValidator {       // registered as scoped
    public Task<bool> IsSessionValidAsync(Guid sessionId, CancellationToken ct);
    public void Invalidate(IEnumerable<Guid> sessionIds);
}

// ParentalTrack.Api.Modules.Devices  (owner of these DTOs — reused by Ingestion + History)
public sealed record LocationPointDto(long Id, double Latitude, double Longitude, double AccuracyMeters,
    double? AltitudeMeters, double? SpeedMetersPerSecond, double? BearingDegrees,
    int? BatteryPercent, bool? IsCharging, LocationProvider Provider,
    DateTimeOffset RecordedAt, DateTimeOffset ReceivedAt) {
    public static LocationPointDto FromEntity(LocationRecord record);
}
public sealed record TrackingConfigDto(int IntervalSeconds, int FastestIntervalSeconds,
    int MinDistanceMeters, int BatchMaxSize, int UploadIntervalSeconds) {
    public static TrackingConfigDto FromOptions(TrackingOptions options);
}

// ParentalTrack.Api.Modules.Ingestion
public sealed class LocationIngestQueue {          // registered as singleton
    public ValueTask<bool> EnqueueAsync(IReadOnlyList<LocationRecord> records, CancellationToken ct);
    public IAsyncEnumerable<IReadOnlyList<LocationRecord>> ReadAllAsync(CancellationToken ct);
}
```

## 10. Fixed cross-agent surface (admin-web)

`src/api` and `src/hooks` are authored by one agent; `src/pages` and `src/components` by another.
The exports below are fixed:

```ts
// api/client.ts
export class ApiError extends Error { status: number; problem?: ProblemDetails }
export function apiFetch<T>(path: string, init?: RequestInit & { auth?: boolean }): Promise<T>
export function setAccessToken(token: string | null, expiresAtUtc?: string): void
export function getStoredRefreshToken(): string | null
export function setStoredRefreshToken(token: string | null): void
export function onUnauthorized(handler: () => void): void

// api/auth.ts       login, register, refresh, logout, me
// api/devices.ts    listDevices, getDevice, createDevice, updateDevice, deleteDevice, regeneratePairingCode, revokeDevice
// api/locations.ts  getCurrentLocation, getHistory
// api/types.ts      all DTO interfaces from §2 + DeviceStatus/LocationProvider string unions + AppConfig

// auth/AuthContext.tsx
export function AuthProvider(props: { children: React.ReactNode }): JSX.Element
export function useAuth(): { parent: ParentDto | null; isReady: boolean; isAuthenticated: boolean;
                             login(email: string, password: string): Promise<void>; logout(): Promise<void> }
// auth/RequireAuth.tsx — default-exports a wrapper component

// hooks
export function useDevices(): UseQueryResult<DeviceSummaryDto[]>
export function useDevice(deviceId?: string): UseQueryResult<DeviceDetailDto>
export function useCurrentLocation(deviceId?: string): UseQueryResult<LocationSnapshotDto | null>
export function useHistory(deviceId: string | undefined, range: { fromUtc: string; toUtc: string } | null): UseQueryResult<LocationHistoryResponse>
export function useConfig(): UseQueryResult<AppConfig>
export function useNow(intervalMs?: number): number          // epoch millis, ticking

// lib/time.ts    formatRelative(iso|epoch, now): string; formatAbsolute(iso): string; toLocalInputValue(d): string; fromLocalInputValue(s): string
// lib/format.ts  formatAccuracy(m): string; formatDistance(m): string; formatBattery(p|null): string; formatCoords(lat,lon): string
```

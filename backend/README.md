# ParentalTrack — Backend

ASP.NET Core 10 modular monolith. Five modules (Auth, Devices, Ingestion, History, plus the shared
Security/Common core) live in one process behind one composition root, each behind an
`Add<Module>` / `Map<Module>` pair so any of them can be lifted into its own service later.

* API base path: `/api/v1`
* Ports: `http://localhost:5080`, `https://localhost:7443`
* JSON: camelCase, enums as camelCase strings, timestamps ISO-8601 UTC
* Errors: RFC7807 `application/problem+json` on every failure

---

## Run it

```bash
# 1. PostgreSQL 16 (named volume, healthcheck, published on 5432)
docker compose up -d db

# 2. Schema. Development also applies migrations automatically on startup, so this step is only
#    needed for the first run outside Development or when you want the DB ready up front.
dotnet ef database update \
  --project src/ParentalTrack.Infrastructure \
  --startup-project src/ParentalTrack.Api

# 3. API (profile "http"; use --launch-profile https for the TLS endpoint)
dotnet run --project src/ParentalTrack.Api
```

In Development the app applies pending migrations and seeds the demo parent
(`parent@example.com` / `ChangeMe123!`) on startup, mounts the OpenAPI document at
`/openapi/v1.json` and the Scalar API reference at `/scalar`. `requests.http` walks the whole flow:
register → login → create device → enroll with the pairing code → ingest a point → read current →
read history.

The EF Core tool is installed once per machine with `dotnet tool install --global dotnet-ef`.
New migrations go into the Infrastructure project:

```bash
dotnet ef migrations add <Name> \
  --project src/ParentalTrack.Infrastructure \
  --startup-project src/ParentalTrack.Api \
  --output-dir Migrations
```

### Running the API in Docker too

```bash
JWT_SIGNING_KEY="$(openssl rand -base64 48)" docker compose --profile full up -d --build
```

The container runs as `Production`: it does **not** auto-migrate, and it refuses to start unless
`Jwt__SigningKey` is at least 32 bytes. Apply migrations from the host (step 2 above, pointing at
`localhost:5432`) before starting it.

### Health

| Endpoint | Meaning |
|---|---|
| `GET /health/live` | process is up; never touches the database |
| `GET /health/ready` | `AppDbContext.Database.CanConnectAsync()` — 200 or 503 |

---

## Configuration

Every key below is bound once in `Program.cs` into the option types in `Options/AppOptions.cs` and
validated at startup (`ValidateOnStart`), so a bad value fails the boot instead of the first request.
Environment variables use the double-underscore form, e.g. `Jwt__SigningKey`,
`ConnectionStrings__Postgres`, `Cors__AllowedOrigins__0`.

| Section | Key | Default (`appsettings.json`) | Notes |
|---|---|---|---|
| `ConnectionStrings` | `Postgres` | `Host=localhost;Port=5432;Database=parentaltrack;Username=parentaltrack;Password=parentaltrack` | matches `docker compose up -d db` |
| `Jwt` | `Issuer` | `parentaltrack` | |
| | `ParentAudience` | `parentaltrack.admin` | admin web tokens |
| | `DeviceAudience` | `parentaltrack.device` | Android tokens |
| | `SigningKey` | *(empty)* | **must** come from env/user-secrets; ≥ 32 bytes or the app refuses to start |
| | `ParentAccessTokenMinutes` | `60` | |
| | `RefreshTokenDays` | `30` | opaque, hashed, rotated on every refresh |
| | `DeviceTokenDays` | `365` | revocable per session via `device_sessions` |
| `Tracking` | `OnlineThresholdSeconds` | `180` | ≤ this since last fix ⇒ `online` |
| | `StaleThresholdSeconds` | `600` | ≤ this ⇒ `idle`, beyond ⇒ `offline` (= stale) |
| | `DefaultRefreshSeconds` | `15` | dashboard poll interval |
| | `IntervalSeconds` / `FastestIntervalSeconds` | `60` / `30` | handed to the device at enrollment |
| | `MinDistanceMeters` | `25` | |
| | `BatchMaxSize` / `UploadIntervalSeconds` | `100` / `120` | device upload batching |
| | `MapTileUrl` / `MapAttribution` | OpenStreetMap | returned by `GET /api/v1/config` |
| `Ingestion` | `MaxBatchSize` | `200` | larger batch ⇒ 400 |
| | `QueueCapacity` | `10000` | bounded channel; full for 2 s ⇒ 503 |
| | `WriteBatchSize` / `FlushIntervalMilliseconds` | `200` / `500` | background writer |
| | `RetentionDays` | `90` | retention worker deletes older fixes every 6 h |
| `Devices` | `PairingCodeTtlMinutes` | `60` | single-use code, stored hashed |
| `Auth` | `AllowSelfRegistration` | `false` (`true` in Development) | otherwise `POST /auth/register` ⇒ 403 |
| `Seed` | `Enabled` | `false` (`true` in Development) | demo parent only; never devices or locations |
| | `ParentEmail` / `ParentPassword` / `ParentDisplayName` | `parent@example.com` / `ChangeMe123!` / `Demo Parent` | |
| `Cors` | `AllowedOrigins` | `["http://localhost:5173"]` | credentials stay disabled (bearer tokens, not cookies) |
| `Network` | `KnownProxies` | *(none)* | IPs of the TLS terminator, e.g. `["10.0.0.7"]`; see "Running behind a reverse proxy" |
| | `KnownNetworks` | *(none)* | CIDR form, e.g. `["10.0.0.0/8"]` |
| | `ForwardLimit` | `1` | number of trusted hops in `X-Forwarded-For` |

### Running behind a reverse proxy

The container listens on plain HTTP (`Dockerfile`, `docker-compose.yml`), so TLS is terminated
upstream and `Connection.RemoteIpAddress` is the proxy's address for every request. The `login`
(10/min) and `enroll` (5/min) rate limiters are specified per-IP in contract §4.3, so without
forwarded headers the whole deployment shares one partition and a single noisy client can lock every
parent out of sign-in.

`app.UseForwardedHeaders()` therefore runs as the first middleware, honouring `X-Forwarded-For` and
`X-Forwarded-Proto`. It is only trusted from the addresses listed in `Network:KnownProxies` /
`Network:KnownNetworks`; with nothing configured the ASP.NET Core defaults trust loopback only, so
an untrusted caller can never pick its own rate-limit partition key. **Set these keys to the
terminator's address whenever the API is deployed behind one — and never to a wildcard**, or the
partition key becomes attacker-controlled and the limiter is trivially bypassed.

`Network` is not in the contract §3 key list; it is an addition, not a change to any documented key.

Local development key handling:

```bash
dotnet user-secrets --project src/ParentalTrack.Api set "Jwt:SigningKey" "<64 random characters>"
```

`appsettings.Development.json` ships a fixed 64-character key so a fresh clone runs. It is a
development-only value — never promote it, and never set `ASPNETCORE_ENVIRONMENT=Development` on a
deployed host.

---

## Module map

```
src/ParentalTrack.Domain           entities, enums, DeviceStatusCalculator, GeoMath — no dependencies
src/ParentalTrack.Infrastructure   AppDbContext, entity configurations, migrations, DbSeeder
src/ParentalTrack.Api
  Program.cs                       the only composition root (options, authn/z, rate limits, pipeline)
  Options/AppOptions.cs            all 7 option types, one SectionName each
  Common/                          ApiResults, CurrentUser (tenancy claims), ValidationExtensions
  Security/                        PasswordHasher, TokenService, DeviceSessionValidator, AuthConstants
  Modules/Auth/                    register, login, refresh, logout, me
  Modules/Devices/                 parent device CRUD + pairing codes, device enroll/self
  Modules/Ingestion/               POST /ingest/locations, bounded queue, writer + retention workers
  Modules/History/                 current location, history, GET /config
```

Cross-cutting rules the modules rely on:

* **Tenancy** — every parent-scoped query filters on `CurrentUser.GetParentId()`. Another parent's
  device is a 404, never a 403.
* **Time** — `TimeProvider` is injected everywhere; nothing calls `DateTimeOffset.UtcNow` directly,
  which keeps status and staleness deterministic in tests.
* **Status** — computed once, server-side, by `DeviceStatusCalculator`. Clients render
  `status` / `isStale` / `secondsSinceUpdate` and never re-derive thresholds.
* **Rate limits** — `login` 10/min/IP, `enroll` 5/min/IP, `ingest` 120/min per device (`sub` claim).
  `UseRateLimiter` sits *after* `UseAuthentication` precisely so the ingest partition can read that
  claim; rejections are 429 with `Retry-After` and a ProblemDetails body.
* **Device revocation** — device JWTs carry `jti` = `device_sessions.id`. Every device request runs
  `DeviceSessionValidator` inside `JwtBearerEvents.OnTokenValidated` (30 s memory cache, evicted
  immediately by `POST /devices/{id}/revoke`).

---

## Extracting a module into its own service

Each module is already a vertical slice: endpoints, services and DTOs in one folder, reached only
through `Add<Module>(IServiceCollection, IConfiguration)` and `Map<Module>(IEndpointRouteBuilder)`.
To pull one out:

1. **New host** — create a project, copy `Program.cs` and delete the `Add`/`Map` calls of the modules
   that stay behind. Options binding, JWT setup, rate limiting and health checks are copied as-is;
   they are per-host concerns, not per-module.
2. **Move the folder** — `Modules/<Name>/` moves unchanged, together with the parts of `Security/`
   and `Common/` it uses. `AuthConstants` (claim names, policy names) is the contract that keeps the
   tokens interchangeable between hosts, so it must be shared verbatim — publish it as a small
   package rather than copying it.
3. **Data** — the tables a module owns move with it (`Ingestion`/`History` own `location_records`,
   `Devices` owns `child_devices` + `device_sessions`, `Auth` owns `parents` + `refresh_tokens`).
   Where a slice needs another's data — history reading `child_devices` for `childName`, ingestion
   updating `last_seen_at` — replace the direct query with a call to the owning service, or publish a
   `DeviceUpdated`/`LocationRecorded` event and keep a local projection. Those are the only two
   cross-slice reads in the codebase today.
4. **Ingestion first** — it is the natural first extraction: it already writes through a bounded
   in-process channel (`LocationIngestQueue`) plus a `BackgroundService`. Swap the channel for a real
   broker and the endpoint, worker and retention job move without touching the parent-facing API.
5. **Auth stays shared** — tokens are validated, not introspected, so every extracted host needs only
   the signing key, the issuer and the two audiences. No call back to the Auth service is required on
   the request path.

---

## Contract notes

Implemented as written, with these additions where CONTRACT.md was silent:

* **`POST /api/v1/auth/register` shares the `login` rate-limit policy (10/min/IP).** §4.3 names
  limiters for `login`, `enroll` and `ingest` only, but register is anonymous, costs a full
  210 000-iteration PBKDF2 hash per attempt and answers 409 for an address that already has an
  account — unlimited, that is both an account-enumeration oracle and a CPU amplifier. It matters
  wherever `Auth:AllowSelfRegistration` is true (Development today, one config flag away anywhere
  else). No new policy name was introduced.
* **`IngestResponse.duplicates` counts stored replays too.** §2.4 defines a duplicate as a repeated
  `(device_id, client_id)` pair, so `IngestionEndpoints` resolves the batch's client ids against
  `location_records` before answering and moves the already-stored points out of `accepted`. The
  `ON CONFLICT (device_id, client_id) DO NOTHING` in `LocationIngestWorker` stays as the backstop
  for a replay that races the queue. If that lookup fails (an unreachable database), the batch is
  still queued and still answered 202 — the count is best-effort, the 202 is not.
* **JSON wire format.** Nullable members are serialised as explicit `null`
  (`DefaultIgnoreCondition.Never`) because §0/§2 spell them out and both clients declare them
  present-and-nullable; `DateTimeOffset` is written as `yyyy-MM-ddTHH:mm:ss.fffZ` by
  `UtcDateTimeOffsetJsonConverter` (in `Program.cs`) rather than in System.Text.Json's round-trip
  form, which §0 pins.

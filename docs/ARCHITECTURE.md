# ParentalTrack — Architecture

Scope: the backend, and how the Android app and the admin web app attach to it. Everything here
follows `docs/CONTRACT.md`; where this document names a type, a path or a status code, the contract
is the authority.

---

## 1. Shape of the system

Three deployables and one database:

```
┌──────────────────────┐        HTTPS, Bearer parentAccessToken        ┌──────────────────────────┐
│  admin-web           │ ─────────────────────────────────────────────▶│                          │
│  React 19 + Vite     │◀───────────────────────────────────────────── │  ParentalTrack.Api       │
│  Leaflet map         │        JSON (camelCase, ISO-8601 UTC)         │  ASP.NET Core 10         │
└──────────────────────┘                                               │  modular monolith        │
                                                                       │                          │
┌──────────────────────┐        HTTPS, Bearer deviceToken              │  ┌────────────────────┐  │
│  android child app   │ ─────────────────────────────────────────────▶│  │ Auth | Devices |   │  │
│  Kotlin + Compose    │◀───────────────────────────────────────────── │  │ Ingestion | History│  │
│  foreground service  │        202 / 401 / 503                        │  └────────────────────┘  │
└──────────────────────┘                                               └───────────┬──────────────┘
                                                                                   │ EF Core 10
                                                                                   ▼
                                                                       ┌──────────────────────────┐
                                                                       │ PostgreSQL               │
                                                                       │ parents, refresh_tokens, │
                                                                       │ child_devices,           │
                                                                       │ device_sessions,         │
                                                                       │ location_records         │
                                                                       └──────────────────────────┘
```

The backend is one process containing four feature modules plus the host that composes them. Each
module owns a slice of the schema and exposes exactly two extension methods, called once from
`Program.cs`:

```csharp
services.AddAuthModule(config);      app.MapAuthModule();
services.AddDevicesModule(config);   app.MapDevicesModule();
services.AddIngestionModule(config); app.MapIngestionModule();
services.AddHistoryModule(config);   app.MapHistoryModule();
```

That pair of methods is the entire coupling between the host and a module. Nothing else in the host
knows what a module contains, and no module calls another module's endpoints.

> Terminology note: the contract's header comment says "5 modules = 5 extractable services". Four of
> those are the feature modules under `Modules/`; the fifth extractable unit is the **Admin API
> surface** — the parent-facing read/command surface composed of the parent routes in Devices and
> History plus `GET /api/v1/config`, which is what `admin-web` talks to and the first thing you
> would split out behind a gateway. It is described as its own row in the module map below.

---

## 2. Module map

| Module | Namespace | Owns (tables) | HTTP surface | Auth |
|---|---|---|---|---|
| **Auth** | `ParentalTrack.Api.Modules.Auth` | `parents`, `refresh_tokens` | `POST /api/v1/auth/{register,login,refresh,logout}`, `GET /api/v1/auth/me` | anon (`login` rate-limited 10/min/IP), `ParentPolicy` for `/me` |
| **Devices** | `ParentalTrack.Api.Modules.Devices` | `child_devices`, `device_sessions` | `GET/POST /api/v1/devices`, `GET/PATCH/DELETE /api/v1/devices/{deviceId:guid}`, `POST …/pairing-code`, `POST …/revoke`, `POST /api/v1/devices/enroll`, `GET /api/v1/devices/me` | `ParentPolicy`; enroll is anon (5/min/IP); `/devices/me` is `DevicePolicy` |
| **Ingestion** | `ParentalTrack.Api.Modules.Ingestion` | writes `location_records`, updates the denormalised columns on `child_devices` | `POST /api/v1/ingest/locations` → **202** | `DevicePolicy` (120/min per device `sub`) |
| **History** | `ParentalTrack.Api.Modules.History` | reads `location_records` | `GET /api/v1/devices/{deviceId:guid}/location/current`, `GET /api/v1/devices/{deviceId:guid}/locations`, `GET /api/v1/config` | `ParentPolicy` |
| **Admin API surface** | (composition of Devices + History parent routes) | — | everything under `/api/v1` that carries a parent token | `ParentPolicy` |

Shared, deliberately small, and owned by exactly one place:

* `ParentalTrack.Domain` — entities, `LocationProvider`, `DeviceStatus`, `DeviceStatusCalculator`,
  `GeoMath`. No ASP.NET, no EF Core. Both the ingest write path and every read path derive status
  from the same pure function, so "online" cannot mean two different things.
* `ParentalTrack.Infrastructure` — `AppDbContext` and the entity configurations. All five tables
  live in one `DbContext` because they live in one database; the module boundary is enforced by
  which module touches which `DbSet`, not by separate contexts.
* `ParentalTrack.Api.Modules.Devices.LocationPointDto` and `TrackingConfigDto` — the two DTOs used
  across modules. Ingestion and History consume them; neither re-declares them.
* `ParentalTrack.Api.Security.AuthConstants`, `ParentalTrack.Api.Common.CurrentUser`,
  `ParentalTrack.Api.Options.AppOptions` — pre-written shared vocabulary (contract §8).

### Dependency direction

```
        Api.Modules.{Auth, Devices, Ingestion, History}
                 │              │
                 │              └──▶ Api.Security / Api.Common / Api.Options
                 ▼
        Infrastructure (AppDbContext, EF configurations)
                 ▼
        Domain (entities, enums, DeviceStatusCalculator, GeoMath)
```

Arrows only point down. Domain knows nothing about EF Core; Infrastructure knows nothing about
HTTP; modules know nothing about each other.

---

## 3. Why a modular monolith and not five processes

The MVP is one team, one database, one deployment target, and a load profile of "a handful of
families". Splitting it into five services on day one would buy distribution and pay for it with:

* **Five deployments and five sets of config for one workload.** Every schema change would need a
  coordinated rollout instead of one `dotnet ef database update`.
* **A distributed transaction where a local one suffices.** Enrollment writes `child_devices` and
  `device_sessions` in one `SaveChangesAsync`. Ingest writes `location_records` and updates
  `child_devices.last_seen_at` in the same unit of work. Split across services, both become sagas
  with compensations — the single largest source of bugs in a young system.
* **Network failures in place of method calls.** Every cross-module read becomes a timeout, a
  retry policy and a circuit breaker to reason about.
* **Slower iteration exactly when the domain is least settled.** The contract is v1; the field list
  in §1 will move. Moving a column is a refactor in a monolith and a versioned API change across
  five repos otherwise.

What actually forces a split is *independent scaling* or *independent failure domains*, and this
system has exactly one candidate for that: **Ingestion**, whose write rate scales with the number of
child devices while everything else scales with the far smaller number of parents watching a map.
That is why the ingest path is already queue-separated inside the process (§6) — the seam that
matters is built, and the rest of the boxes are not drawn until something demands them.

So the rule applied here is: **modular now, extractable later, distributed only when measured.**
Each module is written as though it were already a service — its own endpoints, its own service
class, its own options section, no reaching into a neighbour's tables — so that extraction is a
mechanical operation rather than a redesign.

---

## 4. Extraction path per module

For each module: what state it owns, what it would need to call once separated, and where the seam
already sits today.

### 4.1 Auth → Identity service

* **State owned:** `parents`, `refresh_tokens`.
* **Would need to call:** nothing. It is a pure leaf — it reads and writes only its own two tables.
* **Others would need from it:** token *validation*, not token *issuance*. Devices and History
  already validate parent tokens locally against `Jwt:SigningKey` + `Jwt:ParentAudience`, so
  extraction does not create a runtime dependency; it creates a **key distribution** requirement.
* **Existing seam:** `TokenService` is the single place tokens are minted, `PasswordHasher` the
  single place passwords are hashed, and `AuthService` never touches `child_devices`. The only
  cross-cutting fact is the shared HMAC signing key.
* **Extraction steps:** move `Modules/Auth` + `Security/PasswordHasher` + the parent half of
  `TokenService` into a new host; move `parents` and `refresh_tokens` to its database; swap the
  symmetric HMAC key for an asymmetric signing key (RS256/ES256) published as a JWKS endpoint so
  the other services verify without holding a secret; point their `TokenValidationParameters` at
  that JWKS. `refresh_tokens.parent_id` is the only FK crossing the new boundary and it stays
  inside the extracted service.
* **What breaks if you do it naively:** cascade delete of a parent currently removes their devices
  and locations in one statement. After extraction that becomes a `ParentDeleted` event the Devices
  service must consume. Do not extract Auth without deciding who owns account deletion.

### 4.2 Devices → Device registry service

* **State owned:** `child_devices`, `device_sessions`.
* **Would need to call:** Auth, only to *validate* a parent token (see above) — no synchronous call
  if JWKS validation is used. Nothing else.
* **Others would need from it:** Ingestion needs "is this device id active, and which session is
  valid?" on every batch; History needs `child_name`, `last_seen_at`, `last_location_id`,
  `last_battery_percent` for the summary and snapshot DTOs.
* **Existing seam:** `DeviceSessionValidator` already fronts every session lookup with an
  `IMemoryCache` entry (`devsess:{jti}`, 30 s TTL) and already exposes `Invalidate(...)` for the
  revoke path. That is precisely the shape of a remote authorization check: cache-first, explicit
  invalidation, bounded staleness.
* **Extraction steps:** move `Modules/Devices` and `Security/DeviceSessionValidator` out; replace
  the `IMemoryCache` with a distributed cache (Redis) so `POST /devices/{id}/revoke` can evict
  entries other instances hold; expose an internal `GET /devices/{id}/session/{jti}` (or a signed,
  short-lived introspection response) for Ingestion. Keep the 30 s TTL — it is the contract's
  chosen revocation-latency budget and it survives the move.
* **What breaks if you do it naively:** the denormalised `last_seen_at` / `last_location_id` /
  `last_battery_percent` columns live on `child_devices` but are written by Ingestion. Splitting
  Devices from Ingestion turns that write into a message. Either move those three columns to the
  Ingestion side as a `device_state` projection, or accept an at-least-once "device heartbeat"
  event. Decide before, not after.

### 4.3 Ingestion → Ingest service (the one worth extracting first)

* **State owned:** the write path into `location_records`, plus the heartbeat update on
  `child_devices` and the retention deletion.
* **Would need to call:** device-session validation (Devices), nothing else. It does not read
  history, does not serve parents, and never touches `parents`.
* **Others would need from it:** nothing synchronous. History reads the table it writes.
* **Existing seam:** `LocationIngestQueue` — a bounded `Channel<IReadOnlyList<LocationRecord>>`
  with exactly two methods, `EnqueueAsync` and `ReadAllAsync`. The endpoint only ever calls the
  first; `LocationIngestWorker` only ever calls the second. Producer and consumer already share no
  state beyond that channel, so the channel can be replaced by a broker without touching either
  side's logic.
* **Extraction steps:** (1) split vertically first — put `IngestionEndpoints` in an ingest host and
  keep `LocationIngestWorker` in a writer host, with the in-process channel replaced by a durable
  queue (RabbitMQ / SQS / Kafka topic keyed by `device_id`). Keeping the key on `device_id`
  preserves per-device ordering, which the unique index on `(device_id, client_id)` makes
  unnecessary for correctness but useful for the heartbeat update. (2) The endpoint host becomes
  stateless and horizontally scalable; the writer host is the only writer to `location_records`.
* **What you gain immediately:** a queue that survives a process restart. Today an in-flight batch
  in the channel is lost if the process dies — acceptable because the device re-sends anything it
  did not receive a 202 for, and the `(device_id, client_id)` unique index makes the re-send a
  no-op duplicate. That property is exactly what makes the durable-queue swap safe.

### 4.4 History → Query/read service

* **State owned:** nothing. It is read-only over `location_records` and `child_devices`.
* **Would need to call:** Devices, for the ownership check (`does deviceId belong to parentId?`) and
  for `child_name`; that is a single lookup it could cache for seconds.
* **Others would need from it:** nothing.
* **Existing seam:** `HistoryService` takes `deviceId` + `parentId` and returns DTOs; it never
  mutates. `GET /api/v1/config` is served here (`MapHistoryModule`) precisely because it is a read
  concern of the dashboard. Downsampling (`simplify`) and `distanceMeters` (`GeoMath.HaversineMeters`)
  are computed server-side, so the read model is already isolated from the client.
* **Extraction steps:** point it at a read replica (it issues no writes), or at a purpose-built
  store — a time-partitioned table, or TimescaleDB/PostGIS if track queries outgrow the
  `(device_id, recorded_at DESC)` index. Because every response is already a DTO and never an
  entity, the storage shape can change without any client change.
* **What breaks if you do it naively:** a read replica introduces replication lag, so a fix that was
  just ingested may be missing from `/location/current`. Either keep `/location/current` on the
  primary (it is a single indexed row via `last_location_id`) and send only `/locations` to the
  replica, or accept the lag in `secondsSinceUpdate`, which would then no longer match
  `DeviceStatusCalculator`'s inputs.

### 4.5 Admin API surface → BFF / gateway

* **State owned:** none; it is a composition of parent-facing routes.
* **Existing seam:** every parent route already filters by the `parentId` taken from the JWT and
  returns **404** (never 403) for another parent's device, so tenant isolation lives in the route
  handlers and not in a gateway. `admin-web` talks to exactly one origin through the Vite proxy or
  `VITE_API_BASE_URL`.
* **Extraction steps:** put a gateway in front, route `/api/v1/auth/*` to Identity,
  `/api/v1/devices/*` to Devices/History, `/api/v1/ingest/*` to Ingest. Because the web client uses
  one base URL and one fetch wrapper (`api/client.ts`), the front end needs no change at all —
  only the gateway's route table.

---

## 5. Request lifecycles

### 5.1 Ingest — `POST /api/v1/ingest/locations`

```
device                     API process                                        Postgres
  │
  │ POST /api/v1/ingest/locations
  │ Authorization: Bearer <deviceToken>
  │ { "points": [ … ≤200 … ] }
  ├──────────────────▶ ① rate limiter "ingest" (120/min, partitioned by `sub`)
  │                       └ over limit → 429
  │                    ② JWT bearer: signature, exp, aud == Jwt:DeviceAudience
  │                       MapInboundClaims = false, ClockSkew 30 s
  │                    ③ JwtBearerEvents.OnTokenValidated →
  │                       DeviceSessionValidator.IsSessionValidAsync(jti)
  │                       IMemoryCache "devsess:{jti}" (30 s) ─miss─────────────▶ device_sessions
  │                       revoked / expired / missing / device inactive
  │                       → context.Fail() → 401
  │                    ④ DevicePolicy: claim typ == "device"
  │                    ⑤ deviceId = User.GetDeviceId()
  │                    ⑥ batch size > Ingestion:MaxBatchSize (200) → 400 ProblemDetails
  │                    ⑦ per-point validation:
  │                         lat ∈ [-90,90], lon ∈ [-180,180],
  │                         accuracy ∈ [0,10000], battery ∈ [0,100],
  │                         recordedAt ≥ now-24 h and ≤ now+5 min
  │                       invalid → counted in `rejected`, skipped.
  │                       The batch is NEVER failed as a whole.
  │                    ⑧ valid points → LocationRecord entities
  │                       (ReceivedAt = server clock)
  │                    ⑨ LocationIngestQueue.EnqueueAsync(records, ct)
  │                         bounded channel, BoundedChannelFullMode.Wait
  │                         could not enqueue within 2 s → 503
  │◀─────────────────── ⑩ 202 Accepted
  │                       { accepted, duplicates, rejected, serverTimeUtc }
  │
  │                    ── asynchronously, in LocationIngestWorker ──
  │                       reads batches (WriteBatchSize 200, flush every 500 ms)
  │                       INSERT … ON CONFLICT (device_id, client_id) DO NOTHING ▶ location_records
  │                       UPDATE child_devices SET last_seen_at = max(recorded_at),
  │                              last_location_id = …, last_battery_percent = … ▶ child_devices
  │
  └ on 202 the device deletes those rows from its Room queue —
    accepted, duplicate and rejected alike, because the server has
    already told it they will never be taken.
```

Note where `duplicates` comes from: the response counts what the request *itself* contains twice or
what the worker will drop, and the durable guarantee is the **unique index**
`ix_location_records_device_id_client_id` on `(device_id, client_id)`. `clientId` is generated on the
device when the fix is taken, so a retry after a lost 202 is idempotent by construction. This is why
returning 202 before the write is safe: the write cannot be applied twice.

### 5.2 Dashboard poll — `GET /api/v1/devices/{deviceId}/location/current`

```
browser                    API process                                        Postgres
  │ GET /api/v1/devices/{id}/location/current
  │ Authorization: Bearer <parentAccessToken>
  ├──────────────────▶ ① CORS check against Cors:AllowedOrigins (AllowCredentials = false)
  │                    ② JWT bearer: aud == Jwt:ParentAudience
  │                    ③ ParentPolicy: claim typ == "parent"
  │                    ④ parentId = User.GetParentId()
  │                    ⑤ SELECT … FROM child_devices
  │                       WHERE id = @deviceId AND parent_id = @parentId ────────▶ child_devices
  │                       no row → 404 (never 403 — existence is not leaked)
  │                    ⑥ last_location_id IS NULL → 204 No Content
  │                    ⑦ SELECT … FROM location_records WHERE id = @lastLocationId ▶ location_records
  │                    ⑧ status = DeviceStatusCalculator.Evaluate(
  │                          lastSeenAt, now,
  │                          Tracking:OnlineThresholdSeconds (180),
  │                          Tracking:StaleThresholdSeconds  (600))
  │                       isStale = status is Offline or NeverReported
  │◀─────────────────── ⑨ 200 LocationSnapshotDto
  │                       { deviceId, childName, status, isStale,
  │                         secondsSinceUpdate, serverTimeUtc, location }
  │
  └ React Query refetchInterval = defaultRefreshSeconds from GET /api/v1/config
    (15 s), paused while document.hidden. `useNow(1000)` re-renders the
    relative time between polls without re-deriving the status.
```

The `last_location_id` pointer on `child_devices` is why the hot path is two single-row lookups
instead of a `MAX(recorded_at)` scan per device. It is a denormalisation the write path maintains,
and it is a plain indexed column with no FK constraint so the retention worker can delete old rows
without fighting a foreign key.

History (`GET …/locations`) follows the same first five steps, then ranges over
`ix_location_records_device_id_recorded_at`, applies `minAccuracyMeters`, applies the even-stride
`simplify` downsample (keeping first and last) when asked, and sums `GeoMath.HaversineMeters` over
the points it is about to return — so `distanceMeters` always describes the polyline the client will
actually draw.

---

## 6. The ingest queue, and why the endpoint returns 202

`LocationIngestQueue` wraps a `System.Threading.Channels.Channel<IReadOnlyList<LocationRecord>>`
created with `BoundedChannelOptions(Ingestion:QueueCapacity /* 10 000 */)` and
`BoundedChannelFullMode.Wait`. It is registered as a **singleton** and exposes only:

```csharp
ValueTask<bool> EnqueueAsync(IReadOnlyList<LocationRecord> records, CancellationToken ct);
IAsyncEnumerable<IReadOnlyList<LocationRecord>> ReadAllAsync(CancellationToken ct);
```

`LocationIngestWorker` is a `BackgroundService` that consumes `ReadAllAsync`, accumulates up to
`Ingestion:WriteBatchSize` (200) records or `Ingestion:FlushIntervalMilliseconds` (500 ms) of
waiting, then performs one write per flush inside a scoped `AppDbContext`.

Why it is built this way:

* **The request path does no I/O it does not have to.** Validation and mapping are CPU-bound and
  bounded by `MaxBatchSize`. The database write — the only slow, contended, failure-prone part — is
  moved off the request. A phone on a bad cellular link is not also holding a database connection.
* **Bounded, not unbounded.** An unbounded queue converts a database outage into an
  out-of-memory crash that loses everything buffered. Capacity 10 000 with `Wait` means back
  pressure reaches the endpoint instead: `EnqueueAsync` blocks, the 2-second budget expires, and the
  endpoint answers **503**. A 503 is a truthful answer — "I cannot take this right now" — and the
  Android `LocationUploadWorker` treats 5xx as retryable with exponential backoff from 30 s. The
  data waits on the device, which has durable storage (Room) and no memory pressure, instead of in
  server RAM.
* **Batched writes.** Fixes arrive as many small bursts from many devices; Postgres is far happier
  with one 200-row insert than 200 single-row inserts. The flush interval caps the added latency at
  half a second.
* **202 is the honest status code.** 201 Created would assert a row exists at a URL; at the moment
  the response is written, it does not. 200 OK would assert the work is done. **202 Accepted** means
  exactly what happened: the request was understood, validated, and accepted for processing. The
  body tells the caller how the batch was classified (`accepted`, `duplicates`, `rejected`) so the
  device can clear its queue with certainty, and `serverTimeUtc` lets it measure its own clock skew.
* **Accepting is safe because ingest is idempotent.** If the process dies with batches still in the
  channel, those fixes are lost from the server's point of view — but the device only deletes rows
  after it sees a 202, and any re-sent point collides with the unique
  `(device_id, client_id)` index and is counted as a duplicate. The worst case is a re-send, never a
  double-write and never silent divergence.
* **Rejected points are not an error.** A single point with a corrupt latitude must not fail the
  batch: the device would retry the same batch forever and never make progress. It is counted in
  `rejected` and dropped. Validation failure of the *envelope* (over 200 points, malformed JSON) is
  a 400, because retrying that unchanged is also futile and the device must be told the request
  itself was wrong.

`LocationRetentionWorker` lives in the same module and on the same principle: a `BackgroundService`
that every 6 hours deletes `location_records` older than `Ingestion:RetentionDays` (default 90) and
logs the deleted count. Retention is a property of the ingest pipeline, not a cron job bolted on
outside it, so it moves with the module when Ingestion is extracted.

---

## 7. Cross-cutting decisions worth remembering

| Decision | Where | Why |
|---|---|---|
| Minimal APIs, `MapGroup` per module, no controllers | `Modules/*/…Endpoints.cs` | The route table for a module is one readable file; extraction is copy-the-file |
| One `DbContext`, snake_case columns declared explicitly | `Infrastructure/Persistence` | One database, one migration history; explicit `ToTable`/`HasColumnName` so nothing depends on a naming convention that could change |
| `Guid` ids for people/devices, `long` for location rows | contract §0 | Ids that cross a trust boundary should not be guessable or enumerable; location rows are internal, high-volume and benefit from a compact monotonic key |
| Ownership check returns **404**, never 403 | every parent route | 403 confirms a device id exists under some other parent. 404 leaks nothing |
| Status thresholds only on the server | `DeviceStatusCalculator` | Two implementations of "offline" would drift; `/api/v1/config` publishes the thresholds so the UI can tick a clock, not re-decide |
| RFC7807 `ProblemDetails` for every error | host pipeline | One error shape for the fetch wrapper and one for the Android client; `ApiError.problem` in `admin-web` depends on it |
| `MapInboundClaims = false` | JWT bearer setup | Keeps raw `sub` / `jti` so `CurrentUser` extensions and `DeviceSessionValidator` read the claims the contract names |
| Device revocation checked per request, cached 30 s | `DeviceSessionValidator` | A 365-day device token with no revocation check is a permanent key. The cache bounds the database cost; the explicit `Invalidate` bounds the revocation latency to "immediately on this instance" |
| Nothing but location is collected | Android manifest + schema | Data minimisation is enforced by there being no permission and no column for anything else — see `PRIVACY-AND-CONSENT.md` |

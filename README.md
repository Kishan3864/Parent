# ParentalTrack

A consent-based family location-sharing system. A child's Android phone shares its GPS location with
the parent account it was explicitly paired to; the parent sees the current position and the recent
track on a web map. Nothing else is collected — no contacts, no messages, no call logs, no media, no
app inventory, no microphone.

Three components and one contract:

* `backend/` — ASP.NET Core 10 modular monolith (`net10.0`), PostgreSQL, Minimal APIs.
* `android/` — Kotlin child app (Compose UI, foreground location service, Room offline queue).
* `admin-web/` — React 19 + Vite + Leaflet parent dashboard.
* `docs/CONTRACT.md` — the binding interface contract. It is the source of truth for every path,
  DTO field, status code and signature. If this README and the contract ever disagree, the contract
  wins.

---

## The MVP flow, end to end

```
 Parent (admin-web)                Backend (ParentalTrack.Api)              Child device (android)
 ------------------                ---------------------------              ----------------------
 1. register / login  ─────────▶   POST /api/v1/auth/login
                                   → accessToken (JWT, 60 min)
                                     + refreshToken (opaque, 30 d)

 2. create child device ───────▶   POST /api/v1/devices
                                   → DeviceDetailDto incl. plaintext
                                     pairingCode "AB3D-9KMP" (TTL 60 min)

 3. read the code out, hand the phone over ────────────────────────────────▶ 4. ConsentScreen
                                                                               "I understand and agree"

                                   POST /api/v1/devices/enroll  ◀─────────── 5. PairingScreen: type code
                                   → deviceToken (JWT, 365 d)
                                     + tracking config

                                                                            6. PermissionScreen
                                                                               notifications → fine location
                                                                               → background location
                                                                               ("Allow all the time")

                                                                            7. Start sharing
                                                                               foreground service starts,
                                                                               persistent notification shown,
                                                                               FusedLocationProvider streams fixes

                                                                            8. each fix → Room queue
                                                                               (pending_locations)

                                   POST /api/v1/ingest/locations ◀────────── 9. LocationUploadWorker flushes
                                        (Bearer deviceToken)                    a batch (≤ 100 points)
                                   → 202 Accepted { accepted, duplicates,
                                                    rejected, serverTimeUtc }
                                          │
                                          ▼
                                   LocationIngestQueue (bounded Channel)
                                          │
                                          ▼
                                   LocationIngestWorker (BackgroundService)
                                   inserts into PostgreSQL location_records,
                                   updates child_devices.last_seen_at /
                                   last_location_id / last_battery_percent

 10. dashboard polls  ─────────▶   GET /api/v1/devices/{id}/location/current
     every 15 s                    → LocationSnapshotDto (status, isStale,
                                     secondsSinceUpdate, location)
                                   GET /api/v1/devices/{id}/locations?from&to
                                   → LocationHistoryResponse (points, distanceMeters)

 11. map renders marker + accuracy circle + history polyline.
     Grey marker + StaleBanner when the device is offline — the last known
     fix is never hidden.
```

Two things in that flow shape the whole design and are worth calling out here:

* **Ingest returns 202, not 201.** The endpoint validates the batch, converts it to entities, hands
  it to an in-process bounded channel and returns immediately. A `BackgroundService` does the
  database write. The device is never made to wait on Postgres, and a slow disk cannot turn into a
  retry storm from every phone at once. If the queue cannot accept the batch within 2 seconds the
  endpoint returns **503** so the device keeps its rows and retries later.
* **Staleness is computed on the server, once.** `DeviceStatusCalculator.Evaluate` is the only
  definition of `online` / `idle` / `offline`, and every read path uses it. The web app renders what
  it is told (`status`, `isStale`, `secondsSinceUpdate`) and only re-ticks the clock locally using
  the thresholds returned by `GET /api/v1/config`.

---

## Components

| Component | Path | Stack | Responsibility |
|---|---|---|---|
| API host | `backend/src/ParentalTrack.Api` | ASP.NET Core 10, Minimal APIs | Composition root: hosts the four modules, JWT auth, CORS, rate limiting, health checks |
| Auth module | `…/Api/Modules/Auth` | — | Register, login, refresh-token rotation, logout, `/auth/me` |
| Devices module | `…/Api/Modules/Devices` | — | Parent CRUD over child devices, pairing codes, enrollment, device revoke; owns `LocationPointDto` and `TrackingConfigDto` |
| Ingestion module | `…/Api/Modules/Ingestion` | — | `POST /ingest/locations`, bounded channel, write worker, 90-day retention worker |
| History module | `…/Api/Modules/History` | — | Current snapshot, history query with downsampling and haversine distance, `GET /config` |
| Domain | `backend/src/ParentalTrack.Domain` | net10.0 class library | Entities, enums, `DeviceStatusCalculator`, `GeoMath` — no framework dependencies |
| Infrastructure | `backend/src/ParentalTrack.Infrastructure` | EF Core 10 + Npgsql | `AppDbContext`, entity configurations, migrations, dev seeder |
| Database | `backend/docker-compose.yml` | PostgreSQL | `parents`, `refresh_tokens`, `child_devices`, `device_sessions`, `location_records` |
| Child app | `android/app` | Kotlin 2.x, Compose, Room, WorkManager | Consent, pairing, permissions, foreground location service, offline queue, batched upload |
| Parent dashboard | `admin-web` | React 19, Vite, React Query, Leaflet | Login, device list, live map, history playback, device management |

### Ports

| What | URL |
|---|---|
| API (HTTP) | `http://localhost:5080` |
| API (HTTPS) | `https://localhost:7443` |
| Admin web dev server | `http://localhost:5173` (proxies `/api` → `:5080`) |
| API as seen from the Android emulator | `http://10.0.2.2:5080` (debug builds only) |
| PostgreSQL | `localhost:5432`, database `parentaltrack` |

---

## Quickstart (about 10 minutes)

Prerequisites: .NET 10 SDK, Node 20+, Docker Desktop, Android Studio with an API 34/35 emulator
image. Full detail — certificates, the end-to-end verification walkthrough and troubleshooting —
is in [`docs/RUNBOOK.md`](docs/RUNBOOK.md).

```bash
# 1. Database (from the repo root)
cd backend
docker compose up -d db
docker compose ps                 # expect the db container to be healthy

# 2. Schema
dotnet tool install --global dotnet-ef        # once, if you do not already have it
dotnet ef database update \
  --project src/ParentalTrack.Infrastructure \
  --startup-project src/ParentalTrack.Api

# 3. API  (Development: self-registration on, dev parent seeded, dev signing key)
dotnet run --project src/ParentalTrack.Api
# → http://localhost:5080 and https://localhost:7443
# → GET http://localhost:5080/health/ready must return 200

# 4. Parent dashboard (new terminal, from the repo root)
cd admin-web
npm install
npm run dev
# → http://localhost:5173 — log in as parent@example.com / ChangeMe123!

# 5. Child app
#    Open the `android` folder in Android Studio, let Gradle sync, start an
#    API 34+ emulator, then Run the `app` configuration.
#    Debug builds already point at http://10.0.2.2:5080 — nothing to configure.
```

Then: create a device in the dashboard, copy the pairing code, and in the app accept the consent
screen, enter the code, grant notifications + location + "Allow all the time", and press
**Start sharing**. In the emulator use *Extended controls → Location* to set a position or play a
route. Within a minute the dashboard marker appears and the status badge turns green.

Run `dotnet dev-certs https --trust` once if you want your browser to trust
`https://localhost:7443`. The dashboard dev server reaches the API over the Vite proxy on the HTTP
port, so this is optional for the quickstart.

---

## What this is **not**

ParentalTrack is a **consent-based family safety tool**. It is designed to be — and must be deployed
as — something the tracked person knows about and has agreed to.

* **It is not covert monitoring.** The launcher icon is always visible, the app can be uninstalled
  like any other app, and while sharing is on Android shows a permanent notification that the app
  never attempts to hide. That notification is a feature, not a bug — see
  [`docs/PRIVACY-AND-CONSENT.md`](docs/PRIVACY-AND-CONSENT.md).
* **It is not a general-purpose surveillance platform.** No contacts, SMS, call logs, browsing
  history, photos, audio, keystrokes or installed-app list are read, stored or transmitted anywhere
  in the system. The stored fields are exactly those listed in `docs/CONTRACT.md` §1, and nothing
  in the Android manifest could collect more.
* **It is not for tracking other adults.** Putting a location tracker on another adult's phone
  without their knowledge and consent is unlawful in most jurisdictions. Laws differ — check yours.
* **It is not an emergency service.** Location quality depends on GPS conditions, battery,
  connectivity and OS power management. Never make a safety-critical decision on the assumption that
  the last fix is current.
* **It is not production-hardened yet.** This is an MVP. Certificate pinning, refresh-token binding,
  vault-managed secrets, an audit log, per-parent rate limits and backup encryption are all still
  open; the list and the reasoning are in [`docs/SECURITY.md`](docs/SECURITY.md).
* **It does not do geofencing, alerts, multi-parent sharing, iOS or push messaging.** Those are
  explicitly out of scope for this MVP (contract §7). The module seams exist so they can be added
  later without a rewrite.

---

## Documentation

| Document | What is in it |
|---|---|
| [`docs/CONTRACT.md`](docs/CONTRACT.md) | The binding contract: paths, DTOs, status codes, signatures |
| [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) | Module map, why a modular monolith, extraction path per module, data-flow diagram, request lifecycles, the ingest queue |
| [`docs/RUNBOOK.md`](docs/RUNBOOK.md) | Prerequisites, migrations, running each component, HTTPS certs, Android `API_BASE_URL`, end-to-end verification, troubleshooting |
| [`docs/PRIVACY-AND-CONSENT.md`](docs/PRIVACY-AND-CONSENT.md) | Data inventory, retention, who sees what, consent copy, revoke and delete, legal framing |
| [`docs/SECURITY.md`](docs/SECURITY.md) | Threat model, what is implemented today, gaps to close before production |

# ParentalTrack — Runbook

How to get the three components running, how to prove end to end that a location made it from a
phone to a map, and what to do when it did not. Paths are relative to the repo root
(`c:\location`, git-bash `/c/location`).

---

## 1. Prerequisites

| Tool | Version | Check |
|---|---|---|
| .NET SDK | 10.0 | `dotnet --list-sdks` |
| `dotnet-ef` | 10.x | `dotnet ef --version` — install with `dotnet tool install --global dotnet-ef` |
| Docker Desktop | current | `docker compose version` |
| Node.js | 20 LTS or newer | `node -v` / `npm -v` |
| Android Studio | current stable | includes AGP 8.7+ and JDK 17 |
| Android emulator image | API 34 or 35, **with Google Play services** | required — the app uses `FusedLocationProviderClient` |
| Git | any | |

Notes:

* The emulator image **must** include Google APIs / Play services. A plain AOSP image has no fused
  location provider and the app will never produce a fix.
* Port 5432 must be free, or change the host port mapping in `backend/docker-compose.yml` and the
  `ConnectionStrings:Postgres` port to match.
* JDK: let Android Studio use its bundled JDK 17. Do not point `JAVA_HOME` at the .NET-era JDK you
  may have installed for something else.

---

## 2. Database

### 2.1 Start PostgreSQL

```bash
cd backend
docker compose up -d db
docker compose ps
```

Expected: one container running, state `healthy`. The compose file provisions database
`parentaltrack` with user/password `parentaltrack` / `parentaltrack`, matching the default
`ConnectionStrings:Postgres` in `appsettings.json`:

```
Host=localhost;Port=5432;Database=parentaltrack;Username=parentaltrack;Password=parentaltrack
```

Verify connectivity:

```bash
docker compose exec db psql -U parentaltrack -d parentaltrack -c "select version();"
```

### 2.2 Apply migrations

From `backend/`:

```bash
dotnet ef database update \
  --project src/ParentalTrack.Infrastructure \
  --startup-project src/ParentalTrack.Api
```

`--project` is where the migrations and the design-time `AppDbContextFactory` live; `--startup-project`
is where configuration is read from. Both flags are required — running it from either project alone
will fail to resolve the connection string.

Expected result — five tables and the four named indexes:

```bash
docker compose exec db psql -U parentaltrack -d parentaltrack -c "\dt"
# parents, refresh_tokens, child_devices, device_sessions, location_records
docker compose exec db psql -U parentaltrack -d parentaltrack -c "\di"
# ix_parents_email_normalized               UNIQUE
# ix_child_devices_parent_id
# ix_location_records_device_id_client_id   UNIQUE
# ix_location_records_device_id_recorded_at
```

### 2.3 Creating a new migration (only when the schema changes)

```bash
dotnet ef migrations add <Name> \
  --project src/ParentalTrack.Infrastructure \
  --startup-project src/ParentalTrack.Api \
  --output-dir Migrations
```

Review the generated `Up`/`Down` before committing. Never edit an already-applied migration; add a
new one.

### 2.4 Resetting

```bash
docker compose down -v      # destroys the volume and all location data
docker compose up -d db
dotnet ef database update --project src/ParentalTrack.Infrastructure --startup-project src/ParentalTrack.Api
```

---

## 3. Running each component

### 3.1 API

```bash
cd backend
dotnet run --project src/ParentalTrack.Api
```

Development defaults (from `appsettings.Development.json`): `Jwt:SigningKey` is a 64-character dev
key, `Auth:AllowSelfRegistration` is `true`, `Seed:Enabled` is `true` — so a parent account
`parent@example.com` / `ChangeMe123!` is created on first start.

Listens on `http://localhost:5080` and `https://localhost:7443`.

Smoke-test:

```bash
curl -i http://localhost:5080/health/live      # 200
curl -i http://localhost:5080/health/ready     # 200 (503 means the DB check failed)
```

Outside Development the app **refuses to start** unless `Jwt:SigningKey` is supplied and is at least
32 bytes. Supply it as an environment variable — never in a committed file:

```bash
# bash
export Jwt__SigningKey="$(openssl rand -base64 48)"
export ASPNETCORE_ENVIRONMENT=Production
```

```powershell
# PowerShell
$env:Jwt__SigningKey = [Convert]::ToBase64String((1..48 | ForEach-Object { Get-Random -Max 256 }))
$env:ASPNETCORE_ENVIRONMENT = "Production"
```

Changing the signing key invalidates every issued token: parents must log in again and **every child
device must be re-paired**. Treat it as a destructive operation.

### 3.2 Admin web

```bash
cd admin-web
npm install
npm run dev            # http://localhost:5173
```

The dev server proxies `/api` to `http://localhost:5080`, so no CORS configuration is needed in
development. For a production build set `VITE_API_BASE_URL` (default `/api`) in `.env` — copy
`.env.example` — and run `npm run build`, which runs `tsc -b && vite build` and must complete with
zero errors.

If you serve the built assets from a different origin than the API, add that origin to
`Cors:AllowedOrigins` in the API configuration. `AllowCredentials` is false by design — the app
sends bearer tokens, not cookies.

### 3.3 Android child app

1. Open the `android` folder in Android Studio (open the folder itself, not the repo root).
2. Let Gradle sync. The version catalog is `android/gradle/libs.versions.toml`.
3. Start an API 34/35 emulator with Google Play services.
4. Run the `app` configuration.

Debug builds are pre-configured for the emulator; see §5 before running on a physical device.

---

## 4. HTTPS certificates

### 4.1 Development

```bash
dotnet dev-certs https --trust
```

Trusts the ASP.NET Core development certificate so `https://localhost:7443` opens without a browser
warning. On Windows this prompts once; on macOS it asks for your password; on Linux `--trust` is a
no-op for some browsers and you may need to import the exported `.pem` manually.

Regenerate if it expired or is broken:

```bash
dotnet dev-certs https --clean
dotnet dev-certs https --trust
```

The dev certificate is valid for `localhost` only. It is **not** valid for `10.0.2.2`, which is why
debug Android builds use `http://10.0.2.2:5080` and `network_security_config.xml` permits cleartext
for exactly `10.0.2.2` and `localhost` and nothing else.

### 4.2 Production

Do not use `dotnet dev-certs` and do not terminate TLS in Kestrel with a self-signed certificate.
Put a reverse proxy in front:

1. Terminate TLS at nginx / Caddy / Traefik / an ALB with a certificate from a public CA
   (Let's Encrypt via ACME is sufficient), on the real hostname the Android release build points at.
2. Proxy to Kestrel over HTTP on the internal network, forwarding
   `X-Forwarded-For`, `X-Forwarded-Proto` and `X-Forwarded-Host`, and enable
   `UseForwardedHeaders` in the API so the rate limiter partitions by the real client IP rather than
   the proxy's.
3. Keep HTTPS redirection and HSTS enabled outside Development (they already are).
4. Renew automatically. A device token is valid for 365 days but a phone that cannot complete a TLS
   handshake simply queues locally and, past 10 000 rows, starts dropping the oldest — an expired
   certificate is silent data loss.
5. Self-signed certificates on a physical test device require installing the CA in the device's
   user trust store **and** an `<certificates src="user"/>` entry in the network security config for
   that build type. Prefer a real certificate; it is less work than debugging pinning failures.

---

## 5. Configuring the Android `API_BASE_URL`

The base URL is a single `buildConfigField` in `android/app/build.gradle.kts`:

```kotlin
buildTypes {
    debug {
        buildConfigField("String", "API_BASE_URL", "\"http://10.0.2.2:5080/\"")
    }
    release {
        buildConfigField("String", "API_BASE_URL", "\"https://api.example.com/\"")
    }
}
```

Rules:

* **Trailing slash is required** — Retrofit resolves relative paths against it and drops the last
  path segment if it is missing.
* `10.0.2.2` is the emulator's alias for the host machine's loopback. It is meaningless on a
  physical device.
* **Physical device on the same Wi-Fi:** set the debug value to your machine's LAN address, e.g.
  `"http://192.168.1.42:5080/"`, add that address to the `cleartextTrafficPermitted` domain list in
  `app/src/main/res/xml/network_security_config.xml`, ensure the API listens on all interfaces
  (`ASPNETCORE_URLS=http://0.0.0.0:5080`), and open the port in your firewall. This is for local
  testing only.
* **Release:** change `https://api.example.com/` to your real host. Release builds set
  `cleartextTrafficPermitted="false"`; a plain-HTTP release URL will fail every request with a
  `CLEARTEXT communication not permitted` error, by design.
* After changing the value, rebuild — `BuildConfig` is generated at compile time. A "Run" without a
  rebuild keeps the old URL.

---

## 6. End-to-end verification walkthrough

Do this in order the first time. Each step lists what you should observe; if an observation is
missing, stop there and go to §7 rather than continuing.

**Step 0 — infrastructure**

```bash
cd backend && docker compose up -d db
dotnet ef database update --project src/ParentalTrack.Infrastructure --startup-project src/ParentalTrack.Api
dotnet run --project src/ParentalTrack.Api
```

*Observe:* `/health/ready` returns 200. Startup log shows the seeded parent (first run only) and the
Ingestion background workers starting.

**Step 1 — parent login**

Open `http://localhost:5173`, log in as `parent@example.com` / `ChangeMe123!`.

*Observe:* redirect to the dashboard. In DevTools → Application → Local Storage there is a
`pt.refreshToken` key. The access token is held in memory only and is intentionally not there.

**Step 2 — create a child device**

Devices page → create a device with a child name and a label.

*Observe:* HTTP **201**, and the pairing code shown large, formatted `XXXX-XXXX`, with its expiry
(60 minutes by default). The alphabet excludes I, O, 0 and 1, so nothing you read out is ambiguous.
Re-opening the device later shows `pairingCode: null` — the plaintext code is returned exactly once,
at creation and at regeneration.

**Step 3 — consent on the child device**

Launch the app on the emulator.

*Observe:* the **ConsentScreen** first, before anything else. It explains that this device's
location is shared continuously with the paired parent account including in the background, that a
permanent notification is shown while sharing is on, and that sharing can be stopped from the app or
from the notification. Nothing starts until "I understand and agree" is tapped.

**Step 4 — pair**

Enter the code (with or without the dash, any case).

*Observe:* the app moves to the permission screen and shows the child name that the parent typed in
step 2 — that name came back in `EnrollResponse`, which proves the round trip. Server side:

```bash
docker compose exec db psql -U parentaltrack -d parentaltrack \
  -c "select child_name, paired_at, pairing_code_hash from child_devices;"
```

`paired_at` is set and `pairing_code_hash` is now **null** — the code was single-use.

**Step 5 — permissions, in order**

*Observe:* an in-app rationale before each system dialog, in this sequence:

1. Notifications (API 33+).
2. Fine + coarse location.
3. Background location — requested **only after** (2) was granted, as a separate step. On API 30+
   there is no system dialog: the app deep-links to the app's settings page and tells you to choose
   the option the OS calls "Allow all the time" (it reads the exact wording from
   `packageManager.backgroundPermissionOptionLabel`).
4. Optional, clearly skippable: battery-optimisation exemption.

If you grant only foreground location, the app still works while the service is running and shows a
warning banner explaining what is missing and what it costs. That is expected behaviour, not a bug.

**Step 6 — start sharing**

Press the Start switch on the StatusScreen.

*Observe:* within a second, a persistent notification in the shade reading "Location sharing is on —
your parent can see this device's location", with a **Stop sharing** action. It is `IMPORTANCE_LOW`
and ongoing; it cannot be swiped away while the service runs. Its presence is the visible proof that
tracking is active.

**Step 7 — produce a fix**

Emulator: *Extended controls (…) → Location* → set a latitude/longitude and click **Set location**,
or load a GPX/KML route and play it.

*Observe on the device:* the StatusScreen "last fix" time updates, and the pending-queue count goes
up by one and then back to zero as the upload worker flushes.

*Observe on the API:* a `POST /api/v1/ingest/locations` with response **202**. Then:

```bash
docker compose exec db psql -U parentaltrack -d parentaltrack \
  -c "select count(*), max(recorded_at) from location_records;"
docker compose exec db psql -U parentaltrack -d parentaltrack \
  -c "select last_seen_at, last_battery_percent, last_location_id from child_devices;"
```

Both should be populated within a second or two of the 202 — the write is done by the background
worker, which flushes every 500 ms or every 200 records.

**Step 8 — current location on the map**

Back in the dashboard, select the device.

*Observe:* a marker at the coordinates you set, with an accuracy circle around it, and a green
`online` badge with a relative time that ticks each second. The dashboard polls
`/location/current` every 15 seconds (`defaultRefreshSeconds` from `/api/v1/config`) and pauses while
the tab is hidden. Before the first fix arrives the endpoint returns **204** and the UI says the
device has never reported.

**Step 9 — staleness**

Stop sharing on the device (or turn off its network) and wait.

*Observe:* after 180 seconds the badge leaves `online`; after 600 seconds it becomes `offline`, the
marker turns grey with a dashed accuracy circle, and the `StaleBanner` reads "Last known location —
device offline since …". The last known fix stays on the map. It is never hidden.

**Step 10 — history**

Move the emulator location a few times, then pick "Last 1 h" in the history controls.

*Observe:* a polyline with start and end markers, a point count and a total distance. The request is
`/locations?from&to&limit=2000&simplify=true`; the distance is the haversine sum over exactly the
points that were returned, so it always matches the line you can see. An empty range renders an
explicit "No location data in this range".

**Step 11 — offline queue**

Turn the emulator to airplane mode, set two or three new locations, then turn networking back on.

*Observe:* the pending-queue count on the StatusScreen rises while offline and drains after
reconnecting (the upload worker has a `NetworkType.CONNECTED` constraint and is also retried on a
15-minute periodic schedule). No fixes are lost, and no duplicates appear on the map — resends
collide with the unique `(device_id, client_id)` index and come back counted as `duplicates`.

**Step 12 — revocation**

In the dashboard, revoke the device.

*Observe:* the next upload gets **401**. The app then clears the stored token, stops the service,
removes the persistent notification, and posts a notification reading "Location sharing was turned
off by your parent"; the UI reflects the revoked state. Server side, every row in `device_sessions`
for that device has a `revoked_at`, and the cached session entries were evicted immediately rather
than waiting out the 30-second TTL.

**Step 13 — deletion**

Delete the device in the dashboard.

*Observe:* HTTP **204**, the device disappears from the list, and its location rows are gone:

```bash
docker compose exec db psql -U parentaltrack -d parentaltrack \
  -c "select count(*) from location_records;"
```

The delete is a hard delete and cascades. There is no recycle bin.

---

## 7. Troubleshooting

| Symptom | Likely cause | Check | Fix |
|---|---|---|---|
| **No location arriving** — dashboard shows "never reported" or a stale fix, device says sharing is on | The service never got a fix (emulator has no location set; AOSP image without Play services) | StatusScreen "last fix" is empty; no `POST /ingest/locations` in the API log | Set a location in *Extended controls → Location*; use a Google APIs emulator image; outdoors on a real device wait 30–60 s for a first GPS lock |
| | Fixes are queued but not uploading | Pending-queue count keeps rising | Check network; the worker requires `NetworkType.CONNECTED`. Force it by toggling sharing off/on. Wrong `API_BASE_URL` also looks exactly like this — see below |
| | Wrong base URL | `adb logcat -s OkHttp` shows connection refused / unknown host | `10.0.2.2` for the emulator, LAN IP for a physical device, trailing slash required, rebuild after changing (§5) |
| | Cleartext blocked | Logcat: `CLEARTEXT communication to … not permitted` | Add the host to `network_security_config.xml` (debug only) or use HTTPS |
| | API not reachable from the emulator | `curl http://localhost:5080/health/live` works on the host but the app cannot connect | The API must listen on the right interface: `ASPNETCORE_URLS=http://0.0.0.0:5080` for a physical device; `10.0.2.2` already reaches host loopback for the emulator |
| | Points rejected by validation | 202 response body has `rejected > 0` | Device clock skew: `recordedAt` must be within the last 24 h and no more than 5 min in the future. Fix the emulator's clock (`adb shell date`) or enable automatic time |
| **Device gets 401 on ingest** | Device was revoked by the parent | `select revoked_at, revoked_reason from device_sessions where device_id = …` | Intended. Re-pair with a fresh pairing code |
| | Device row is inactive | `select is_active from child_devices where id = …` | PATCH the device with `isActive: true` |
| | `Jwt:SigningKey` changed since enrollment | API restarted with a different key / a generated key | Re-pair every device. Pin the key in configuration so this cannot happen accidentally |
| | Token expired | `exp` in the device token (365 days) | Re-pair |
| | Clock skew between API host and token issuance | More than 30 s (`ClockSkew`) | Sync the server clock (NTP) |
| **Enrollment fails: "Invalid pairing code"** | Code expired (60 min), already consumed, or mistyped | `select pairing_code_expires_at, pairing_code_hash from child_devices` | Regenerate the code in the dashboard. Codes are single-use; the hash is cleared on success. Dashes and case do not matter, but I/O/0/1 are not in the alphabet — re-read the code |
| **Map is blank** (grey or empty panel) | Leaflet CSS not loaded | No tiles, controls unstyled | `leaflet/dist/leaflet.css` must be imported |
| | Default marker icons 404 under the bundler | Console shows missing `marker-icon.png` | Use the `L.divIcon` / explicit `L.Icon.Default` image path fix — this is required, not optional |
| | Map container has zero height | Container renders but no tiles | The map element needs an explicit height in CSS |
| | Tile server unreachable | Network tab shows failed tile requests | Check outbound access to the `mapTileUrl` from `/api/v1/config` |
| | No location yet | `/location/current` returns **204** | Expected — complete step 7 first |
| | 401 on every API call | Access token expired and the refresh failed | The fetch wrapper refreshes once on 401 then redirects to `/login`; log in again. If `pt.refreshToken` is stale, clear it |
| **Background permission never offered** | Foreground location was not granted first | Settings → Apps → ParentalTrack → Permissions | Android only offers "Allow all the time" after fine/coarse is granted — grant step 5.2 first |
| | API 30+ shows no dialog at all | This is the platform behaviour | The app deep-links to app settings; choose the option labelled by `backgroundPermissionOptionLabel` (usually "Allow all the time") manually |
| | Permission was permanently denied | The dialog no longer appears | Only settings can change it now; the app cannot re-prompt |
| | `minSdk`-related confusion | API 28 or lower | `ACCESS_BACKGROUND_LOCATION` does not exist before API 29; foreground location covers background use there |
| **Foreground service start denied** | `ForegroundServiceStartNotAllowedException` (API 31+): start was attempted from the background | Logcat around `LocationTrackingService.onStartCommand` | Start sharing from the visible app (a user tap), not from a background trigger. The app catches this and surfaces it in the UI |
| | Location permission missing when the service starts | `SecurityException` on `requestLocationUpdates` | Grant location first; the permission screen gates the Start switch |
| | Notification permission denied (API 33+) | Service runs but no notification appears | Grant `POST_NOTIFICATIONS`; without it the ongoing notification is not shown, which defeats the visibility guarantee |
| | Service killed by the OEM after a while | Aggressive battery management (common on some vendors) | Grant the optional battery-optimisation exemption; `START_STICKY` plus `BootReceiver` handle reboots, but vendor kills need the exemption |
| **API returns 503 on ingest** | Ingest queue full — the writer cannot keep up or the database is down | `/health/ready`, API logs from `LocationIngestWorker` | Fix the database first. The device retries with backoff and keeps its rows; no data is lost |
| **API refuses to start** | `Jwt:SigningKey` missing or under 32 bytes outside Development | Startup log | Set `Jwt__SigningKey` in the environment (§3.1) |
| | Cannot connect to Postgres | `docker compose ps`, `/health/ready` = 503 | Start the db container; check the port mapping matches the connection string |
| | Pending migration | EF error on first query | Run `dotnet ef database update` (§2.2) |
| **`dotnet ef` cannot find the context** | Wrong project flags | — | Always pass both `--project src/ParentalTrack.Infrastructure` and `--startup-project src/ParentalTrack.Api` |
| **CORS error in the browser** | Origin not allowed | Console: blocked by CORS policy | In dev use the Vite proxy (`npm run dev`); in production add the origin to `Cors:AllowedOrigins` |
| **429 responses** | Rate limits: `login` 10/min/IP, `enroll` 5/min/IP, `ingest` 120/min per device | Response headers / API log | Wait out the window. Repeated enroll 429s during testing usually mean a retry loop on a bad code |

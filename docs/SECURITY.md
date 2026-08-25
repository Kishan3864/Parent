# ParentalTrack — Security

The asset this system protects is a precise, continuous movement history of a child. That is a
higher-value target than the size of the codebase suggests, and it shapes what an MVP is allowed to
skip. This document states the threat model, what is actually implemented today, and — explicitly —
what is not yet good enough for production.

Implementation details referenced here are fixed by `docs/CONTRACT.md` §4.

---

## 1. Threat model

Trust boundaries: the child device (attacker-reachable hardware), the public internet, the API
process, the database, and the parent's browser. The server operator is trusted — this is a
self-hosted system and the database holds everything in cleartext to the operator by design.

### 1.1 Stolen device token

**Attack.** Someone extracts the device JWT — from a rooted or unlocked phone, an ADB backup, a
device handed on without a wipe, or a proxied TLS session on a device where they control the trust
store — and uses it to submit fake locations for that child, or to call `GET /api/v1/devices/me`.

**Why it matters.** The token is valid for 365 days and authorises writes. Forged fixes are worse
than absent ones: a parent acting on a location that says "at school" is worse off than one who can
see the device is offline.

**Mitigated today.** Tokens live in `EncryptedSharedPreferences` (Android Keystore-backed), never in
logs or plaintext files. Transport is HTTPS with HSTS outside Development; the release build sets
`cleartextTrafficPermitted="false"` so a downgrade to HTTP fails rather than silently succeeding.
Every request re-validates the session against `device_sessions`, so the token can be killed at any
moment (`POST /devices/{id}/revoke`). The token's blast radius is deliberately narrow: it can write
locations for one device and read that device's own record. It cannot read history, cannot read any
other device, and cannot touch a parent account — the ingest and device-self routes are the entire
device-scoped surface.

**Residual risk.** A live token on a compromised device is usable until someone notices and revokes.
Detection is manual. See gaps: certificate pinning, audit log, ingest anomaly detection.

### 1.2 Revoked child device that keeps sending

**Attack.** A child (or whoever holds the phone) keeps the app running after the parent revokes it,
or re-installs the APK with the old token, and continues to submit — or, in the mirror-image case,
the parent expects revocation to be instant and it is not.

**Mitigated today.** `DeviceSessionValidator` runs inside `JwtBearerEvents.OnTokenValidated` for
every device token. It looks up the session by `jti` and calls `context.Fail(...)` — a 401, not a
partial success — when the session is revoked, expired or missing, or when
`ChildDevice.IsActive == false`. `POST /devices/{id}/revoke` stamps `revoked_at` on every session
for that device **and evicts the cache entries immediately**, so revocation is effective on that
instance at once rather than after the cache TTL. The Android client then treats 401 as terminal:
it clears the token, stops the service, sets `revoked = true`, and tells the child that sharing was
turned off by their parent.

**Residual risk.** The cache is `IMemoryCache`, per process. With more than one API instance,
revocation is effective immediately on the instance that handled the revoke and within 30 seconds
elsewhere. That bound is a deliberate choice, not an oversight, but it does not survive horizontal
scaling — see gaps.

### 1.3 Tenant isolation (one parent reading another family's child)

**Attack.** An authenticated parent guesses or obtains another family's `deviceId` and requests its
current location, its history, or mutates it.

**Mitigated today.** Every parent-scoped route filters by the `parentId` taken from the JWT — the
device id from the URL is never trusted on its own. A device belonging to another parent returns
**404, never 403**, so the API does not even confirm that the id exists. Device ids are `Guid`s, so
they are not enumerable. There is no admin role and no cross-parent view anywhere in the API.

**Residual risk.** This invariant lives in each route handler rather than in a single enforced
filter. It is the single most important invariant in the system, and the thing most likely to be
forgotten by a future endpoint. Any new parent-scoped route must be reviewed for it explicitly.

### 1.4 Replayed or forged ingest

**Attack.** Someone captures a valid ingest request and replays it repeatedly to inflate the
database, or crafts batches with absurd coordinates or timestamps to poison the history or the
"last seen" state.

**Mitigated today.**

* **Replay is a no-op.** `clientId` is generated on the device per fix, and
  `ix_location_records_device_id_client_id` is UNIQUE on `(device_id, client_id)`. A replayed batch
  is counted as `duplicates` and inserts nothing. The same property is what makes returning 202
  before the write safe.
* **Content is validated per point**: latitude ∈ [-90, 90], longitude ∈ [-180, 180], accuracy ∈
  [0, 10 000], battery ∈ [0, 100], and `recordedAt` no older than 24 hours and no more than 5
  minutes in the future. Out-of-range points are counted in `rejected` and dropped — never accepted,
  and never allowed to fail the whole batch (a device that cannot make progress retries forever).
* **Volume is bounded**: at most 200 points per request, `ingest` is rate-limited to 120 requests
  per minute partitioned by the device `sub` claim, and the ingest channel is bounded at 10 000
  batches with back pressure that surfaces as a 503 rather than an out-of-memory crash.
* **A replay cannot be used to make a device look online forever** beyond the 24-hour `recordedAt`
  window, because `last_seen_at` tracks the maximum accepted `recordedAt`, and stale timestamps are
  rejected.

**Residual risk.** A holder of a valid token can still submit *plausible but false* locations. No
amount of transport security fixes that; it is the same problem as §1.1 and it is bounded by
revocation.

### 1.5 Brute-forced or credential-stuffed parent login

**Attack.** Password spraying against `/api/v1/auth/login`, or reuse of credentials leaked
elsewhere. Success gives full access to a child's live location and history.

**Mitigated today.** Passwords are PBKDF2-HMAC-SHA256 with **210 000 iterations**, a 16-byte salt
and a 32-byte subkey, stored as `pbkdf2-sha256$<iterations>$<saltB64>$<hashB64>` and verified with
`CryptographicOperations.FixedTimeEquals` — so both offline cracking and timing analysis are
expensive. The `login` endpoint is rate-limited to 10 requests per minute per IP. Registration is
gated by `Auth:AllowSelfRegistration`, which defaults to **false** outside Development, so a public
deployment is not an open sign-up form. Minimum password length is 10 characters.

**Residual risk.** Rate limiting is per IP, so a distributed attempt across many addresses is not
slowed. There is no account lockout, no breach-password check, no MFA, and no notification on
successful login from a new location. See gaps.

### 1.6 Stale JWT after revocation (parent side)

**Attack.** A parent access token issued before an account or permission change keeps working until
it expires — for instance after a password reset following a compromise, or if a refresh token was
stolen from `localStorage` by an XSS payload.

**Mitigated today.** Parent access tokens are short-lived (60 minutes). Refresh tokens are opaque
32-byte random values, base64url, stored **hashed** (SHA-256) and never in cleartext, and are
**rotated on every refresh** — the old one is revoked as the new one is issued. Reuse of an
already-revoked refresh token is treated as a breach signal: the request gets 401 **and every active
refresh token for that parent is revoked**, forcing a fresh login everywhere. In the browser the
access token is held in memory only; only the refresh token is in `localStorage` (`pt.refreshToken`),
and a single fetch wrapper refreshes once on a 401 and otherwise redirects to `/login`.

**Residual risk.** Within the 60-minute window an access token is not revocable — there is no
per-request session check on the parent side as there is for devices. Refresh tokens are bearer
tokens not bound to a client, so a stolen one works from anywhere until it is used and rotated (at
which point the reuse detection fires, but only *after* the damage). See gaps.

### 1.7 Other cases considered

| Threat | Position today |
|---|---|
| Pairing-code guessing | 8 characters from a 32-symbol alphabet (≈ 40 bits), single-use, 60-minute TTL, hashed at rest, `enroll` limited to 5/min/IP. Enumeration is impractical within the TTL |
| Pairing code in transit / on screen | Shown once to the parent, read out in person. Never logged, never retrievable after creation — `GET /devices/{id}` returns `pairingCode: null` |
| CSRF | Not applicable: bearer tokens, `AllowCredentials = false`, no cookie auth |
| XSS in the dashboard | React escapes by default; no `dangerouslySetInnerHTML` in this codebase. An XSS bug would still expose the refresh token in `localStorage` — see gaps |
| SQL injection | EF Core with parameterised queries throughout; no raw SQL string concatenation |
| MIME sniffing | `X-Content-Type-Options: nosniff` on responses |
| Denial of service via giant batches | 200-point cap → 400, bounded channel → 503, per-device ingest rate limit |
| Information leak via error bodies | RFC 7807 `ProblemDetails` with generic titles; no stack traces outside Development |
| Weak signing key | The app refuses to start outside Development when `Jwt:SigningKey` is missing or shorter than 32 bytes |

---

## 2. What is implemented today

A checklist, all per contract §4:

**Passwords** — PBKDF2-HMAC-SHA256, 210 000 iterations, 16-byte salt, 32-byte subkey, versioned
storage format, fixed-time comparison.

**Tokens** — Two audiences (`Jwt:ParentAudience`, `Jwt:DeviceAudience`) over one HMAC-SHA256 signing
key. Parent access token: `sub` = parent id, plus `email`, `name`, `typ = parent`, 60 minutes.
Device token: `sub` = device id, `jti` = `DeviceSession.Id`, `pid` = parent id, `typ = device`, 365
days. One JWT bearer scheme validating both audiences, `ClockSkew = 30 s`,
`MapInboundClaims = false` so raw `sub`/`jti` survive. `ParentPolicy` requires `typ == parent`;
`DevicePolicy` requires `typ == device`.

**Refresh tokens** — 32 random bytes, base64url, stored SHA-256 hashed, rotated on every refresh,
reuse of a revoked token revokes the parent's entire active set.

**Device revocation** — Per-request session validation in `OnTokenValidated`, `IMemoryCache` with a
30-second TTL keyed `devsess:{jti}`, immediate eviction on revoke, and `ChildDevice.IsActive`
checked on the same path.

**Transport** — HTTPS redirection and HSTS outside Development. CORS restricted to
`Cors:AllowedOrigins` with `AllowCredentials = false`. Android release builds forbid cleartext
entirely; the debug network security config permits it only for `10.0.2.2` and `localhost`.

**Rate limiting** — Fixed window: `login` 10/min/IP, `enroll` 5/min/IP, `ingest` 120/min per device
`sub`.

**Authorisation** — No location endpoint is anonymous, including `/api/v1/config`. Every
parent-scoped route filters by the JWT's `parentId` and returns 404 for a foreign device.

**Data minimisation** — Only the fields in contract §1 exist. `LocationRetentionWorker` deletes
location rows older than `Ingestion:RetentionDays` (default 90) every 6 hours and logs the count.

**Headers** — `X-Content-Type-Options: nosniff`.

---

## 3. Gaps to close before production

None of the following is implemented. Each is a real gap, not a nice-to-have, and this list is the
production readiness checklist.

### 3.1 Certificate pinning on the Android client
Today the app trusts the system CA store. A user-installed CA — a corporate MDM profile, or an
attacker with device access — lets an interceptor read the device token and rewrite locations in
flight. **Close it by** pinning the leaf or intermediate public key in `network_security_config.xml`
(release build type only) with at least one backup pin, and shipping a rotation plan: a pin outlives
a certificate, so a pinned app with no backup pin bricks itself at renewal.

### 3.2 Refresh-token binding
Refresh tokens are unbound bearer values in `localStorage`. Anyone who obtains one — XSS, a shared
machine, a synced browser profile — can mint access tokens until the next rotation trips reuse
detection. **Close it by** binding the token to a client fingerprint (DPoP or a `cnf` claim), or at
minimum storing it in a `HttpOnly`, `Secure`, `SameSite=Strict` cookie with a CSRF token, and
recording device/IP metadata on each refresh so an anomaly can force re-authentication.

### 3.3 Secrets in a vault
`Jwt:SigningKey` and the database password come from configuration and environment variables today.
That is adequate for a laptop and inadequate for a server: environment variables leak into process
listings, crash dumps, container inspection output and CI logs. **Close it by** moving them to a
managed secret store (Azure Key Vault, AWS Secrets Manager, HashiCorp Vault, or at least
`dotnet user-secrets` in dev and a mounted secrets file in prod), and add key rotation — note that
rotating the JWT signing key today invalidates every device pairing, so rotation needs a
two-key overlap (accept old, sign with new) before it is operationally usable.

### 3.4 Audit log
There is no record of who did what. A parent revoking a device, deleting a device, regenerating a
pairing code, logging in from a new IP, or a device enrolling — none of it is retained beyond
application logs. After an incident there would be nothing to reconstruct. **Close it by** adding an
append-only `audit_events` table (actor type and id, action, subject, IP, user agent, timestamp)
written on every mutating parent action and on enrollment and revocation, retained separately from
location data and excluded from the 90-day location retention sweep.

### 3.5 Per-parent rate limits and abuse detection
Rate limits are per IP for `login` and `enroll`, and per device for `ingest`. An authenticated
parent has no limit at all: they can hammer history queries (`limit` up to 5000 points) or create
devices without bound. **Close it by** adding a per-parent partitioned limiter on the read and
management routes, a cap on devices per parent, and alerting on the patterns that matter —
repeated failed logins for one account, a device whose `rejected` count spikes, a device that
suddenly reports impossible speeds.

### 3.6 Backup encryption
Database backups contain complete movement histories of children. If they are unencrypted on disk or
in object storage, the 90-day retention promise in `PRIVACY-AND-CONSENT.md` is not true and the
backup is the softest target in the system. **Close it by** enabling encryption at rest for the
database volume and for backup artefacts, encrypting backups with a key held outside the backup
system, restricting who can restore, testing restores, and aligning backup retention with the
location retention window.

### 3.7 Also open, and worth listing honestly
* **Multi-instance revocation** — replace `IMemoryCache` with a distributed cache so revocation is
  immediate across every API instance, not just the one that served the revoke (§1.2).
* **MFA and breach-password checks for parents** — the account guards a child's live location; a
  password alone is thin. Account lockout or progressive delay after repeated failures too.
* **Account recovery** — there is no password reset flow. Adding one adds an attack surface that
  must be designed with the same care as login.
* **Security headers beyond `nosniff`** — a Content-Security-Policy, `Referrer-Policy` and
  `Permissions-Policy` on the dashboard, which also reduces the impact of §3.2.
* **Dependency and container scanning in CI** — plus a documented patch cadence.
* **Structured, PII-aware logging** — never log tokens, pairing codes or raw coordinates; today this
  is a convention, not something enforced by a redaction layer.
* **Penetration test of the tenant-isolation invariant** (§1.3) before any deployment serving more
  than one family.

---

## 4. Reporting a vulnerability

This is a self-hosted MVP with no public deployment. If you find a flaw, do not open a public issue
with reproduction steps against a live instance: contact the repository owner privately, include the
affected endpoint or file and a minimal reproduction, and allow time for a fix before disclosure.
Anything in §1 that turns out not to hold — particularly tenant isolation or device-token scope —
should be treated as critical.

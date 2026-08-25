# ParentalTrack — Privacy and Consent

ParentalTrack shares one thing: where a phone is. It is built for a family that has talked about it,
where the person carrying the phone knows the app is there, can see it running, and can turn it off.
That is not a disclaimer bolted on at the end — it is the reason several design decisions look the
way they do, and this document explains both.

---

## 1. Data inventory — exactly what is stored

This is the complete list. It mirrors `docs/CONTRACT.md` §1; there is no other table and no other
column anywhere in the system.

### 1.1 Location data — `location_records`

The only table that describes a person's movements.

| Field | Type | What it is | Why it exists |
|---|---|---|---|
| `id` | bigserial | Row id | Primary key |
| `device_id` | uuid | Which child device produced the fix | Scoping and ownership |
| `client_id` | uuid | Idempotency key generated on the device when the fix is taken | Makes a retried upload a no-op instead of a duplicate point |
| `latitude`, `longitude` | double | The position | The entire purpose of the product |
| `accuracy_meters` | double | Radius the OS reports for that fix | Drawn as the accuracy circle; a 500 m circle must not be read as a 5 m one |
| `altitude_meters` | double? | Altitude, when the OS provides one | Supplied by the fix; stored as-is |
| `speed_mps` | double? | Speed, when the OS provides one | Supplied by the fix |
| `bearing_degrees` | double? | Heading, when the OS provides one | Supplied by the fix |
| `battery_percent` | int? (0–100) | Battery level at the moment of the fix | So a parent can tell "phone is off" from "child stopped moving" |
| `is_charging` | bool? | Charging state at the moment of the fix | Same reason |
| `provider` | smallint enum | `unknown`/`gps`/`network`/`fused`/`passive` | Explains why accuracy is what it is |
| `recorded_at` | timestamptz | Device clock at the fix | The time the position is true for |
| `received_at` | timestamptz | Server clock on write | Distinguishes "old fix, just uploaded" from "current" |

### 1.2 Child device — `child_devices`

| Field | What it is |
|---|---|
| `id`, `parent_id` | Device identity and which parent account it belongs to |
| `child_name` | A display name **typed by the parent**. The app never reads the device owner's name, account or profile |
| `device_label` | Free-text label typed by the parent, e.g. "Sam's phone" |
| `platform`, `manufacturer`, `model`, `os_version`, `app_version` | Coarse device description sent at enrollment, so a parent can tell two phones apart and support can interpret behaviour |
| `install_id` | A random id the app generates for itself. **Not** an advertising id, IMEI, serial, MAC or Android ID |
| `pairing_code_hash`, `pairing_code_expires_at` | SHA-256 of the one-time pairing code; cleared the moment the code is used |
| `paired_at`, `is_active`, `created_at` | Enrollment and enable/disable state |
| `last_seen_at`, `last_battery_percent`, `last_location_id` | A copy of the newest fix's timing, battery and row pointer, so the dashboard's live view is two indexed lookups rather than a scan |

### 1.3 Parent account — `parents`, `refresh_tokens`

| Field | What it is |
|---|---|
| `email`, `email_normalized` | Login identity |
| `password_hash` | PBKDF2-HMAC-SHA256, 210 000 iterations, 16-byte salt, 32-byte subkey. The password itself is never stored |
| `display_name`, `is_active`, `created_at` | Account basics |
| `refresh_tokens.token_hash`, `expires_at`, `created_at`, `revoked_at` | Session continuity. Only a SHA-256 hash of the token is stored, never the token |

### 1.4 Device sessions — `device_sessions`

`id` (which is the device token's `jti`), `device_id`, `issued_at`, `expires_at`, `revoked_at`,
`revoked_reason`, `enrolled_user_agent`. This table is what makes "revoke this device" take effect
within seconds instead of waiting out a 365-day token.

### 1.5 What is **not** collected — and cannot be

No contacts. No SMS or messaging content. No call logs. No microphone or audio. No camera or photos.
No screen content, keystrokes or clipboard. No browsing history. No installed-app inventory. No
advertising identifier, IMEI, serial number, MAC address or Android ID. No Wi-Fi network scan
results. No accounts on the device.

This is enforced structurally, in two places at once:

* **There is no column for any of it.** The tables above are the whole schema.
* **There is no permission for any of it.** The Android manifest declares exactly nine permissions —
  internet, network state, coarse location, fine location, background location, foreground service,
  foreground service (location), post notifications, and receive-boot-completed. There is no
  `QUERY_ALL_PACKAGES`, no `READ_CONTACTS`, no `READ_SMS`, no accessibility service, no device-admin
  receiver, and no icon hiding. An app cannot read what it has not been granted.

No third-party analytics, crash reporting or advertising SDK is present in the child app. Map tiles
are fetched by the **parent's browser** from the tile server named in `GET /api/v1/config` (by
default OpenStreetMap); that request comes from the parent's device, and the child's coordinates are
never sent to the tile provider — only the map view the parent is looking at.

---

## 2. Retention

* Location history is kept for **90 days** and then deleted permanently.
* The value is `Ingestion:RetentionDays` in the API configuration. It is a deployment decision;
  shorten it if you have no reason for 90 days.
* `LocationRetentionWorker` (Ingestion module) runs every 6 hours, deletes `location_records` older
  than the retention window, and logs how many rows it removed. It is part of the ingest pipeline,
  not an external cron job, so it cannot be forgotten when the service is deployed somewhere new.
* Deletion is a real `DELETE`. There is no archive table and no soft-delete flag on location rows.
* Other data is kept while the account and device exist, and disappears with them (§4).
* Backups are the exception you must handle yourself: whatever your database backup schedule is, it
  holds location rows for as long as its own retention allows. Align backup retention with this
  policy, or you have a 90-day promise and a two-year reality.

---

## 3. Who can see what

| Who | Can see |
|---|---|
| **The parent account the device is paired to** | That device's current location, its history within the retention window, its battery, and the device description. Nothing about any other family |
| **Any other parent account** | Nothing. Every parent-scoped route filters by the `parentId` in the JWT, and a device belonging to someone else returns **404**, never 403 — the API does not confirm that another parent's device id exists |
| **The child on the paired device** | That sharing is on (permanent notification), when the last fix was taken, when the last upload succeeded, how many fixes are queued, and which parent name the device is paired to. The app shows the child the same facts it sends |
| **Anyone with the device token** | That device's own record and the ability to submit locations for it. Tokens live in `EncryptedSharedPreferences`; a stolen token is treated as a real risk and covered in `SECURITY.md` |
| **Whoever operates the server** | Everything in the database. This is a self-hosted system: the operator is a trusted party by construction. Treat the database as containing precise movement histories of minors, because it does |

There is no admin role, no support back door, no cross-family view, and no analytics pipeline.
Multi-parent sharing is deliberately out of scope for this MVP — a device belongs to exactly one
parent account.

---

## 4. Consent, revocation and deletion

### 4.1 The consent screen on the child device

`ConsentScreen` is the **first** screen on first launch. It appears before pairing, before any
permission request, and before anything can be started. In substance it says:

> **Before you turn this on**
>
> This app shares this phone's location with the parent account you are about to pair it to.
>
> * Your location is shared **continuously**, including while the app is closed and in the
>   background.
> * While sharing is on, a **permanent notification** is shown in your notification shade. It cannot
>   be hidden. If you do not see it, sharing is not running.
> * Your parent can see where this phone is now, and where it has been for the last 90 days.
> * **Nothing else is collected.** Not your messages, contacts, calls, photos, browsing or apps —
>   only location, battery level and the type of phone.
> * You can stop sharing at any time from this app or from the notification, and you can uninstall
>   the app like any other app.
>
> [ I understand and agree ]

Accepting stores `consentAcceptedAt` in the app's preferences. Nothing — no pairing, no permission
request, no service — happens before that tap.

This screen is the product's ethical position expressed as code: the person being located reads,
in plain language, what will happen, and has to act to allow it.

### 4.2 The persistent notification is a feature, not a bug

While the location service runs, Android shows an ongoing notification reading "Location sharing is
on — your parent can see this device's location", with a **Stop sharing** action and a tap target
that opens the app.

The platform requires it for a `location`-typed foreground service. We would keep it if it were
optional. It is the continuous, unforgeable, on-device signal that the app is doing what it said it
would do — the difference between a family safety tool and stalkerware is precisely that the
tracked person can tell it is running. Any change that hides, suppresses or disguises that
notification is out of bounds for this project.

For the same reason: the launcher icon stays visible, the app appears normally in the app list, the
app never requests device-admin or accessibility privileges, and it can be uninstalled without a
password.

### 4.3 Stopping and revoking

| Who | Action | Effect |
|---|---|---|
| Child | **Stop sharing** in the app or from the notification | The foreground service stops immediately, the notification disappears, no further fixes are collected. Queued fixes already recorded may still upload |
| Child | **Unpair & delete token** on the status screen | The device token is erased from `EncryptedSharedPreferences` and sharing stops. The device can no longer submit locations without being paired again with a new code |
| Child | Uninstall the app | Everything the app stored on the device, including the queue and token, is removed by the OS |
| Parent | `POST /api/v1/devices/{id}/revoke` (Revoke in the dashboard) | Every session for that device gets `revoked_at`, cached entries are evicted immediately, and the device's next call gets **401**. The app then clears its token, stops the service, and notifies the child that sharing was turned off by their parent |
| Parent | `PATCH /api/v1/devices/{id}` with `isActive: false` | Device is soft-disabled; its calls are rejected without destroying history |

Revoking or stopping does **not** delete history that was already recorded. That is deletion, below.

### 4.4 Deleting the data

* **Delete a device:** `DELETE /api/v1/devices/{id}` → **204**. This is a hard delete and cascades:
  the device row, its sessions, and **all** of its location records are gone. There is no recycle
  bin and no undo.
* **Delete a parent account:** deleting a parent cascades to its refresh tokens, its devices, their
  sessions and all their location records. In this MVP that is a database operation on the
  self-hosted instance, not a self-service button.
* **Everything older than the retention window** disappears on its own (§2).
* If a child asks for their history to be deleted, the honest answer is that deleting the device
  does exactly that, immediately and completely. There is no hidden copy — apart from your database
  backups, which is why §2 asks you to align their retention.

---

## 5. Legal and ethical framing

**Install it with the child's knowledge, or do not install it.** The consent screen, the permanent
notification, the visible icon and the working stop button are only meaningful if the conversation
happened first. A tool that a child cannot see is a different product with a different name, and
this is not it.

**A few concrete lines this project does not cross:**

* No hidden or renamed launcher icon, no "stealth mode", no disguised app name.
* No suppressing, delaying or faking the foreground-service notification.
* No device-admin or accessibility privileges to resist uninstallation.
* No collection of anything beyond §1, however easy it would be to add a column.
* No sharing, sale or third-party analytics of location data.

**Age and authority matter.** Parents and guardians generally may set conditions for a minor
child's phone use, including location sharing — but "generally" is doing real work in that sentence.
The older the child, the more the balance shifts towards their own privacy interest, and in several
jurisdictions a teenager has rights over their personal data that a parent cannot simply waive. Age
of consent for data processing under the GDPR, for instance, is set nationally between 13 and 16.

**Monitoring another adult without their consent is a different thing entirely, and is generally
unlawful.** Partners, adult children, employees, housemates: tracking an adult's location without
their knowledge and agreement can constitute stalking, unlawful surveillance or an interception
offence, and can carry criminal liability in many places. Do not use this software that way. If you
are considering it, that is the signal to stop, not to configure it more carefully.

**Laws differ by jurisdiction.** Consent requirements, minimum ages, data-protection duties,
retention limits, breach-notification obligations and the rules for employment or shared-custody
contexts all vary by country and often by state or province. Nothing in this repository is legal
advice. If you are deploying this beyond your own household — for a school, an organisation, or
anyone else's family — get advice that applies where you are, and write down the lawful basis you
are relying on before the first device is paired.

**A last practical note.** If the child asks to see what is stored about them, show them. Everything
in §1 is a short list and a couple of SQL queries. A family safety tool that cannot survive that
conversation is not a family safety tool.

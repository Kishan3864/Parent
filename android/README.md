# ParentalTrack — child app (Android)

Kotlin / Jetpack Compose app that runs on the child's phone. It does one job: after the child
has read and accepted a consent screen and the phone has been paired to a parent account, it
shares this device's location with that account, and shows a permanent notification the whole
time it is doing so.

`applicationId` / `namespace`: `com.parentaltrack.child` · `minSdk 24` · `targetSdk 35` ·
`compileSdk 35` · `versionName 1.0.0`.

---

## Requirements

| Tool | Version |
|---|---|
| JDK | 17 (AGP 8.9 will not run on 11, and the module targets JVM 17) |
| Android Studio | Meerkat (2024.3.1) or newer |
| Android Gradle Plugin | 8.9.2 (pinned in `gradle/libs.versions.toml`) |
| Gradle | 8.11.1 or newer — the minimum AGP 8.9 accepts |
| Kotlin | 2.1.20, with KSP `2.1.20-2.0.1` |

Every dependency version lives in `gradle/libs.versions.toml`. Two of them are coupled and must
never be bumped alone:

* **KSP** is versioned `<kotlin>-<ksp-build>`. If you change `kotlin`, change `ksp` to the
  matching build or the Gradle sync fails immediately.
* The **Compose compiler** ships with the Kotlin compiler, so
  `org.jetbrains.kotlin.plugin.compose` reuses the `kotlin` version reference.

Room is the only KSP consumer — there is no Hilt and no other annotation processor. Dependencies
are wired by hand in `di/ServiceLocator.kt`.

### Gradle wrapper

The wrapper is not committed. Generate it once before the first command-line build:

```bash
cd android
gradle wrapper --gradle-version 8.11.1 --distribution-type bin
```

Opening the project in Android Studio does the same thing on first sync, so you only need this
for a headless/CI build.

---

## Opening the project

1. Android Studio → **Open**, and pick the `android/` directory (not the repository root — the
   Gradle build lives in `android/`).
2. Let the first Gradle sync finish. It downloads the Android SDK 35 platform and build tools if
   they are missing.
3. Select the `debug` build variant (the default) and run the `app` configuration on an emulator
   or a device.

For the debug build to reach a backend, run the API from `backend/` first — it must be listening
on `http://localhost:5080` on the machine hosting the emulator.

---

## Pointing the app at a real server

There is exactly one value to change. In `app/build.gradle.kts`:

```kotlin
release {
    buildConfigField("String", "API_BASE_URL", "\"https://api.example.com/\"")
    buildConfigField("boolean", "ALLOW_CLEARTEXT", "false")
    manifestPlaceholders["usesCleartextTraffic"] = "false"
}
```

Replace `https://api.example.com/` with your API origin. Keep the **trailing slash** — Retrofit
requires it on a base URL — and keep the scheme **https**.

The debug build type points at `http://10.0.2.2:5080/`, which is the Android emulator's alias for
the host machine's loopback interface. It is a debug-only convenience:

* `app/src/main/res/xml/network_security_config.xml` refuses cleartext HTTP everywhere except
  `10.0.2.2`, `localhost` and `127.0.0.1`.
* The release build additionally sets `android:usesCleartextTraffic="false"` (via the
  `usesCleartextTraffic` manifest placeholder) and `BuildConfig.ALLOW_CLEARTEXT = false`.
* Because the release base URL is https, the loopback exception is unreachable in a release
  build. If you want it gone from the shipped APK entirely, delete the `<domain-config>` block
  from `network_security_config.xml`.

Release builds have minification and resource shrinking enabled and **no signing config** — wire
up your own keystore before shipping. Do not commit it; `.gitignore` already excludes `*.jks` and
`*.keystore`.

---

## Permission flow

Permissions are requested one at a time, in this order, and each one is preceded by an in-app
screen explaining what it is for. Nothing is requested — and nothing is collected — before
consent is recorded.

1. **Consent** (`ConsentScreen`). Plain-language explanation of what is shared, that it continues
   in the background, that a permanent notification is shown while it does, and that sharing can
   be stopped from the app or from the notification. Requires an explicit
   "I understand and agree"; the timestamp is stored in preferences.
2. **Pairing** (`PairingScreen`). The 8-character code the parent generated in their dashboard is
   posted to `POST /api/v1/devices/enroll`. The returned device token is stored in
   `EncryptedSharedPreferences`.
3. **Permissions** (`PermissionScreen`), strictly in this sequence:
   1. `POST_NOTIFICATIONS` (API 33+) — without it the ongoing notification is hidden.
   2. `ACCESS_FINE_LOCATION` + `ACCESS_COARSE_LOCATION` — the location that is shared.
   3. `ACCESS_BACKGROUND_LOCATION` (API 29+), as a **separate** request and only once step 2 was
      granted. On API 30+ the system shows no dialog, so the app deep-links to
      `Settings.ACTION_APPLICATION_DETAILS_SETTINGS` and quotes
      `PackageManager.getBackgroundPermissionOptionLabel()` for the exact on-device wording of
      the "allow all the time" option.
   4. Optional and clearly skippable: the battery-optimisation exemption
      (`ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS`).
4. **Status** (`StatusScreen`). Start/stop switch, per-permission state, last fix time, last
   successful upload time, pending-queue size, the paired child name, and
   "Unpair & delete token".

Anything denied degrades rather than blocks: foreground-only location still tracks while the
service is running, and the screen shows a banner naming what is missing and what it costs.
`TrackingController.canStart` gates on consent, pairing and foreground location only —
`POST_NOTIFICATIONS` is deliberately not a gate, because the foreground service starts without it
and only its ongoing notification goes unseen, which the status screen says plainly.

The launcher entry stays visible under its real name. There is no icon hiding, no device-admin
component, no accessibility service, and no `QUERY_ALL_PACKAGES`.

---

## What this app collects — and what it does not

**Collected, and sent to the paired parent account only:**

* Latitude, longitude and accuracy of each location fix.
* Where the platform supplies them: altitude, speed, bearing.
* Battery percentage and charging state, so the parent can tell a flat phone from a stopped app.
* The location provider that produced the fix (gps / network / fused / passive).
* When the fix was taken, as a UTC timestamp.
* Device manufacturer, model, OS version, app version and a random install id, captured once
  during pairing so the parent can tell their children's phones apart.

**Never collected, anywhere in this app:** messages or SMS, contacts, call logs, photos, video,
audio or the microphone, clipboard contents, browsing history, keystrokes, screen contents, the
list of installed or running apps, or any advertising identifier. The manifest requests no
permission that would make any of that possible — read
`app/src/main/AndroidManifest.xml`, it is short.

Location fixes are queued locally in a Room table (`pending_locations`, capped at 10 000 rows,
oldest dropped first) and deleted from the device as soon as the server acknowledges them. The
device token lives in `EncryptedSharedPreferences` and is deleted by "Unpair & delete token".
Backup is disabled (`android:allowBackup="false"`), so neither the queue nor the token leaves the
device through a cloud backup.

If the parent revokes the device, the next upload returns `401`: the app deletes its token, stops
the service, and tells the child that sharing was turned off by their parent.

---

## Source layout

```
app/src/main/java/com/parentaltrack/child/
  ChildApp.kt                 Application + WorkManager Configuration.Provider
  MainActivity.kt             single Compose activity
  di/                         hand-rolled ServiceLocator
  data/local/                 Room database, pending-location queue
  data/prefs/                 EncryptedSharedPreferences + tracking settings
  data/remote/                Retrofit API, DTOs, auth interceptor
  data/repo/                  enrollment and location repositories
  location/                   fused-location collector, battery reader, provider mapping
  service/                    foreground service, notification, boot receiver, controller
  work/                       upload worker and its scheduler
  ui/                         Compose screens, navigation, theme, view model
```

Resources owned by the build: `res/values/strings.xml` (all user-facing copy, grouped by the
prefix scheme documented at the top of that file), `res/values/themes.xml`
(`Theme.ParentalTrack`, a `Theme.Material3.DayNight.NoActionBar`),
`res/drawable/ic_stat_location.xml` (notification small icon) and
`res/xml/network_security_config.xml`.

No launcher icon is committed — Android falls back to the system default. Add
`res/mipmap-anydpi-v26/ic_launcher.xml` (plus densities) and an `android:icon` attribute on
`<application>` when you have artwork.

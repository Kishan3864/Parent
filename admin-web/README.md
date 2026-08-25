# ParentalTrack — Admin Web

Parent dashboard for ParentalTrack: React 19 + TypeScript + Vite, `react-router-dom` 7,
`@tanstack/react-query` 5, Leaflet 1.9 / react-leaflet 5, plain CSS.

## Requirements

* Node `^20.19.0 || >=22.12.0` (developed against Node 22.22 / npm 10.9)
* The ParentalTrack API running on `http://localhost:5080`

## Running against the API

```bash
cd admin-web
npm install
npm run dev          # http://localhost:5173
```

The dev server proxies `/api` to `http://localhost:5080` with `changeOrigin: true`, so the browser
only ever talks to `localhost:5173` and there is no CORS involved in development. `/health/live`
and `/health/ready` are **not** proxied — hit the API directly for those.

Log in with a parent account. In Development the API seeds one (`Seed:*` in `appsettings.Development.json`)
and allows self-registration (`Auth:AllowSelfRegistration`).

```bash
npm run build        # tsc -b && vite build  -> dist/
npm run preview      # serve the production build locally
```

`npm run build` must finish with zero TypeScript errors: `tsconfig.app.json` enables `strict`,
`noUnusedLocals`, `noUnusedParameters` and `noFallthroughCasesInSwitch`.

## Environment variables

Copy `.env.example` to `.env` (or `.env.local`) to override anything. Only `VITE_`-prefixed
variables are exposed to the browser bundle — never put a secret in one.

| Variable | Default | Meaning |
|---|---|---|
| `VITE_API_BASE_URL` | `/api` | Origin prefix of the API, **without** the `/v1` version segment. Dev: leave as `/api` and let the Vite proxy forward it. Prod: e.g. `https://api.example.com/api`. |

## API path convention (decision)

The contract spells endpoints out in full (`/api/v1/devices`). This app treats
`VITE_API_BASE_URL` as the **origin prefix** and every caller passes a **version-relative** path:

```ts
apiFetch<DeviceSummaryDto[]>('/v1/devices');   // -> /api/v1/devices
```

`api/auth.ts`, `api/devices.ts`, `api/locations.ts` and `hooks/useConfig.ts` all follow this.
As a safety net `apiFetch` also accepts a fully qualified `/api/v1/...` path whenever the resolved
base already ends in `/api`; it strips the duplicate prefix instead of producing `/api/api/v1/...`.

## Auth model

* `POST /api/v1/auth/login` returns an **access token** (JWT, ~60 min), an opaque **refresh token**
  (single-use, rotated on every refresh) and the `ParentDto`.
* The access token is held **in memory only** — never in storage — and attached as
  `Authorization: Bearer <token>` to every authenticated request. `apiFetch` authenticates by
  default; `auth: false` opts out (login / register / refresh / logout).
* The refresh token is stored in `localStorage` under **`pt.refreshToken`**. It is the only thing
  that survives a reload.
* On mount `AuthProvider` exchanges the stored refresh token for a fresh session. `isReady` stays
  `false` until that settles, so `RequireAuth` renders a spinner instead of flashing `/login` at an
  already-authenticated parent. The exchange is shared process-wide: refresh tokens are single-use,
  and spending the stored one twice (React StrictMode double-mounts effects in dev) would look like
  token reuse to the API and revoke the parent's whole token family.
* A renewal is scheduled 60 s before the access token expires.
* On a `401` for an authenticated request the client performs **one** refresh, replays the original
  request once, and if that still fails clears both tokens and invokes the `onUnauthorized` handler
  — `AuthProvider` wipes the session and `RequireAuth` redirects to `/login`. Concurrent 401s all
  await the same in-flight refresh instead of stampeding the endpoint.
* If the startup refresh fails for a **network** reason the stored token is kept: only an answer
  from the API proves it dead.
* Error bodies (`application/problem+json`) are parsed into `ApiError.problem`. Tokens are never
  logged.

## Live data

`useDevices()` and `useCurrentLocation()` poll every `AppConfig.defaultRefreshSeconds`
(`GET /api/v1/config`, default 15 s, floored at 5 s). Their `refetchInterval` returns `false` while
`document.hidden`, so a background tab issues no requests; a `visibilitychange` listener invalidates
the queries on return, which refetches immediately and re-arms the interval.

`useNow(1000)` drives relative timestamps from a single shared interval per cadence, started on the
first subscriber and cleared when the last one unmounts.

## Source map

| Path | Owner |
|---|---|
| `src/api`, `src/auth`, `src/hooks`, `src/lib`, `src/App.tsx`, `src/main.tsx` | this scaffold |
| `src/pages`, `src/components`, `src/styles.css` | UI agent |

`App.tsx` mounts `QueryClientProvider` -> `BrowserRouter` -> `AuthProvider` and the routes
`/login` (public), `/` and `/devices` (behind `RequireAuth`, inside `Layout`); any other path
redirects to `/`.

## Contract notes

Implemented as written, with these clarifications where CONTRACT.md left a choice open:

* `secondsSinceUpdate` is typed `number | null` on `DeviceSummaryDto` / `LocationSnapshotDto`,
  since a device that has never reported has no age.
* **Deviation from section 10 — `useAuth().parent` is `AuthParentDto | null`, not `ParentDto | null`.**
  The contract is inconsistent here: section 2.1 defines `AuthResponse.parent` as
  `{ id, email, displayName }` while `ParentDto` (`GET /auth/me`) additionally carries `createdAt`,
  and section 6 pins `types.ts` to section 2's shape. `types.ts` therefore declares a separate
  `AuthParentDto` and `AuthResponse.parent` uses it, so the type no longer asserts a `createdAt` the
  server never sends on login/register/refresh. `ParentDto` is unchanged and is still what
  `api/auth.ts` `me()` returns. Nothing renders `createdAt`; only `displayName` is consumed.
* **Addition to section 10 — `api/client.ts` also exports `ensureFreshAccessToken()` and
  `getAccessTokenExpiry()`.** Every refresh in the app has to share one single-flight guard:
  refresh tokens are single-use, and two callers posting the same token look like reuse to the API,
  which revokes every refresh token the parent holds. `AuthContext.renew()` (the scheduled
  pre-expiry renewal) goes through `ensureFreshAccessToken()` instead of calling
  `POST /v1/auth/refresh` itself, so it shares `inFlightRefresh` with the 401 retry path. The fixed
  exports listed in section 10 are unchanged.
* The React Query cache is cleared on every session identity change (`session.clear()` and at the
  start of `login()`). Query keys are identity-free, so without this the next parent to sign in on
  the same browser would be shown the previous parent's cached devices and locations until the
  refetch landed.
* `GET /api/v1/devices/{id}/location/current` answering **204** resolves to `null` (not an error)
  through `getCurrentLocation` and `useCurrentLocation`.
* `fromLocalInputValue` returns `''` for an empty or malformed input value, so callers can skip a
  half-typed `datetime-local` field. It converts the input's **local** wall clock to a UTC ISO
  string via the `Date(y, m, d, ...)` constructor rather than string parsing.
* `components/Layout.tsx` has no fixed prop shape in the contract, so `App.tsx` widens its type and
  passes `<Outlet/>` as children — correct whether `Layout` renders `children` or its own `Outlet`.

import type { AuthResponse, ProblemDetails } from './types';

/**
 * Single fetch wrapper for the whole app.
 *
 * Path convention: callers pass VERSION-RELATIVE paths ("/v1/devices"). The origin prefix comes
 * from VITE_API_BASE_URL (default "/api", proxied to the API by Vite in dev), so the request URL
 * becomes "/api/v1/devices" — the path spelled out in CONTRACT.md section 2.
 *
 * Tokens: the access token lives in memory only; the opaque refresh token lives in localStorage
 * under "pt.refreshToken". Neither is ever logged.
 */

const REFRESH_TOKEN_STORAGE_KEY = 'pt.refreshToken';

/** Refresh this long before expiry rather than spending a round-trip on a certain 401. */
const EXPIRY_SKEW_MS = 5_000;

const API_BASE_URL = resolveBaseUrl();

function resolveBaseUrl(): string {
  const configured = import.meta.env.VITE_API_BASE_URL?.trim();
  const base = configured !== undefined && configured !== '' ? configured : '/api';
  return base.endsWith('/') ? base.slice(0, -1) : base;
}

function buildUrl(path: string): string {
  const suffix = path.startsWith('/') ? path : `/${path}`;
  // Tolerate a fully qualified "/api/v1/..." path when the base already ends in /api, so the two
  // spellings of the same endpoint can never produce "/api/api/v1/...".
  if (API_BASE_URL.endsWith('/api') && suffix.startsWith('/api/')) {
    return `${API_BASE_URL}${suffix.slice(4)}`;
  }
  return `${API_BASE_URL}${suffix}`;
}

export class ApiError extends Error {
  readonly status: number;
  readonly problem?: ProblemDetails;

  constructor(status: number, message: string, problem?: ProblemDetails) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.problem = problem;
  }
}

let accessToken: string | null = null;
let accessTokenExpiresAtMs: number | null = null;
let unauthorizedHandler: (() => void) | null = null;
let inFlightRefresh: Promise<boolean> | null = null;

/** Stores the access token in memory. Passing expiresAtUtc enables pre-emptive refresh. */
export function setAccessToken(token: string | null, expiresAtUtc?: string): void {
  accessToken = token;
  if (token === null || expiresAtUtc === undefined) {
    accessTokenExpiresAtMs = null;
    return;
  }
  const parsed = Date.parse(expiresAtUtc);
  accessTokenExpiresAtMs = Number.isNaN(parsed) ? null : parsed;
}

export function getStoredRefreshToken(): string | null {
  try {
    return window.localStorage.getItem(REFRESH_TOKEN_STORAGE_KEY);
  } catch {
    // Storage can be unavailable (private mode, blocked site data): treat it as "no session".
    return null;
  }
}

export function setStoredRefreshToken(token: string | null): void {
  try {
    if (token === null) {
      window.localStorage.removeItem(REFRESH_TOKEN_STORAGE_KEY);
    } else {
      window.localStorage.setItem(REFRESH_TOKEN_STORAGE_KEY, token);
    }
  } catch {
    // Without storage the session simply does not survive a reload; nothing else to do.
  }
}

/** Registers the callback invoked once a request is definitively unauthenticated. */
export function onUnauthorized(handler: () => void): void {
  unauthorizedHandler = handler;
}

/** Epoch millis the in-memory access token expires at, or null when it carries no expiry. */
export function getAccessTokenExpiry(): number | null {
  return accessTokenExpiresAtMs;
}

/**
 * Rotates the refresh token through the same single-flight guard the 401 path uses, and resolves
 * true once a valid access token is in memory.
 *
 * Every refresh in the app MUST go through here. Refresh tokens are single-use: two callers that
 * post the same token look like token reuse to the API, which answers by revoking every refresh
 * token the parent has — logging the whole account out of a child-safety console.
 */
export function ensureFreshAccessToken(): Promise<boolean> {
  return refreshOnce();
}

function clearSession(): void {
  setAccessToken(null);
  setStoredRefreshToken(null);
}

function isAccessTokenExpiring(): boolean {
  return accessTokenExpiresAtMs !== null && accessTokenExpiresAtMs - Date.now() <= EXPIRY_SKEW_MS;
}

/**
 * At most one refresh runs at a time: concurrent 401s all await the same promise instead of
 * stampeding the endpoint and burning the single-use refresh token more than once.
 */
function refreshOnce(): Promise<boolean> {
  const existing = inFlightRefresh;
  if (existing !== null) {
    return existing;
  }
  const started = runRefresh().finally(() => {
    inFlightRefresh = null;
  });
  inFlightRefresh = started;
  return started;
}

/** Deliberately bypasses apiFetch: this IS the 401 handler, so it must not recurse through it. */
async function runRefresh(): Promise<boolean> {
  const refreshToken = getStoredRefreshToken();
  if (refreshToken === null) {
    return false;
  }

  try {
    const response = await fetch(buildUrl('/v1/auth/refresh'), {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
      body: JSON.stringify({ refreshToken }),
    });
    if (!response.ok) {
      return false;
    }
    const auth = (await response.json()) as AuthResponse;
    setAccessToken(auth.accessToken, auth.expiresAtUtc);
    setStoredRefreshToken(auth.refreshToken);
    return true;
  } catch {
    // Network failure: keep the stored token so a later attempt can still recover the session.
    return false;
  }
}

function send(path: string, init: RequestInit, withAuth: boolean): Promise<Response> {
  const headers = new Headers(init.headers);
  if (!headers.has('Accept')) {
    headers.set('Accept', 'application/json');
  }
  if (init.body !== undefined && init.body !== null && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json');
  }
  if (withAuth && accessToken !== null) {
    headers.set('Authorization', `Bearer ${accessToken}`);
  }
  return fetch(buildUrl(path), { ...init, headers });
}

async function toApiError(response: Response): Promise<ApiError> {
  let message = `Request failed (HTTP ${response.status})`;
  let problem: ProblemDetails | undefined;

  // Covers both application/json and application/problem+json.
  if ((response.headers.get('Content-Type') ?? '').includes('json')) {
    try {
      const body: unknown = await response.json();
      if (typeof body === 'object' && body !== null) {
        problem = body as ProblemDetails;
        message = problem.detail ?? problem.title ?? message;
      }
    } catch {
      // Malformed error body: the status-based message is still meaningful.
    }
  }

  return new ApiError(response.status, message, problem);
}

async function readBody<T>(response: Response): Promise<T> {
  if (response.status === 204 || response.status === 205) {
    return undefined as unknown as T;
  }
  const text = await response.text();
  if (text === '') {
    return undefined as unknown as T;
  }
  try {
    return JSON.parse(text) as T;
  } catch {
    throw new ApiError(response.status, 'The server returned a malformed response.');
  }
}

/**
 * Performs an API request. "auth" defaults to true.
 *
 * A 401 on an authenticated request triggers one refresh and one replay; if that also fails the
 * tokens are cleared and the onUnauthorized handler fires. Bodies must be replayable
 * (string or undefined) — never pass a stream.
 */
export async function apiFetch<T>(
  path: string,
  init: RequestInit & { auth?: boolean } = {},
): Promise<T> {
  const { auth = true, ...requestInit } = init;

  if (auth && accessToken !== null && isAccessTokenExpiring()) {
    await refreshOnce();
  }

  let response = await send(path, requestInit, auth);

  if (response.status === 401 && auth) {
    const refreshed = await refreshOnce();
    if (refreshed) {
      response = await send(path, requestInit, auth);
    }
    if (!refreshed || response.status === 401) {
      clearSession();
      unauthorizedHandler?.();
      throw await toApiError(response);
    }
  }

  if (!response.ok) {
    throw await toApiError(response);
  }

  return readBody<T>(response);
}

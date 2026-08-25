import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react';
import {
  login as loginRequest,
  logout as logoutRequest,
  refresh as refreshRequest,
} from '../api/auth';
import { useQueryClient } from '@tanstack/react-query';
import {
  ApiError,
  ensureFreshAccessToken,
  getAccessTokenExpiry,
  getStoredRefreshToken,
  onUnauthorized,
  setAccessToken,
  setStoredRefreshToken,
} from '../api/client';
import type { AuthParentDto, AuthResponse } from '../api/types';

export interface AuthContextValue {
  /**
   * The identity embedded in the auth response (contract §2.1): id/email/displayName. Deviates
   * from §10's `ParentDto | null` because §2.1 does not put `createdAt` in `AuthResponse`; see
   * "Contract deviations" in the README.
   */
  parent: AuthParentDto | null;
  isReady: boolean;
  isAuthenticated: boolean;
  login(email: string, password: string): Promise<void>;
  logout(): Promise<void>;
}

const AuthContext = createContext<AuthContextValue | null>(null);

/** Renew the access token this long before it expires. */
const REFRESH_LEAD_MS = 60_000;
const MIN_REFRESH_DELAY_MS = 1_000;

type RestoreResult =
  | { status: 'authenticated'; auth: AuthResponse }
  | { status: 'anonymous' }
  | { status: 'unavailable' };

/**
 * The mount-time restore is shared process-wide: refresh tokens are single-use and rotated, so
 * spending the stored one twice (React StrictMode double-mounts effects in dev) would look like
 * token reuse to the API and revoke every refresh token of the parent.
 */
let restoreOnce: Promise<RestoreResult> | null = null;

function restoreSession(): Promise<RestoreResult> {
  const existing = restoreOnce;
  if (existing !== null) {
    return existing;
  }

  const started = (async (): Promise<RestoreResult> => {
    const refreshToken = getStoredRefreshToken();
    if (refreshToken === null) {
      return { status: 'anonymous' };
    }
    try {
      return { status: 'authenticated', auth: await refreshRequest(refreshToken) };
    } catch (error) {
      // Only an answer from the API proves the token is dead; a network failure must not
      // destroy a session that is still valid.
      return error instanceof ApiError ? { status: 'anonymous' } : { status: 'unavailable' };
    }
  })();

  restoreOnce = started;
  return started;
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [parent, setParent] = useState<AuthParentDto | null>(null);
  const [isReady, setIsReady] = useState(false);
  const refreshTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const queryClient = useQueryClient();

  // Built once: it only closes over refs, the (stable) state setters and the query client.
  const session = useMemo(() => {
    const cancelTimer = (): void => {
      if (refreshTimerRef.current !== null) {
        clearTimeout(refreshTimerRef.current);
        refreshTimerRef.current = null;
      }
    };

    const scheduleRefreshAt = (expiresAt: number): void => {
      cancelTimer();
      if (Number.isNaN(expiresAt)) {
        return;
      }
      const delay = Math.max(expiresAt - Date.now() - REFRESH_LEAD_MS, MIN_REFRESH_DELAY_MS);
      refreshTimerRef.current = setTimeout(() => {
        void renew();
      }, delay);
    };

    const apply = (auth: AuthResponse): void => {
      setAccessToken(auth.accessToken, auth.expiresAtUtc);
      setStoredRefreshToken(auth.refreshToken);
      setParent(auth.parent);
      scheduleRefreshAt(Date.parse(auth.expiresAtUtc));
    };

    const clear = (): void => {
      cancelTimer();
      restoreOnce = null;
      setAccessToken(null);
      setStoredRefreshToken(null);
      setParent(null);
      // Devices, child names and last known locations are cached under identity-free keys
      // (['devices'], ['devices', id, 'location', 'current']). Dropping them with the session is
      // what stops the next parent to sign in on this browser from seeing the previous one's
      // children while the refetch is still in flight.
      queryClient.clear();
    };

    /**
     * Renewal goes through the client's single-flight guard rather than posting the stored refresh
     * token itself: the timer, the pre-emptive refresh inside apiFetch and the 401 retry then all
     * await one rotation instead of racing to spend the same single-use token, which the server
     * treats as reuse and answers by revoking every refresh token this parent holds.
     */
    const renew = async (): Promise<void> => {
      if (getStoredRefreshToken() === null) {
        clear();
        return;
      }
      const refreshed = await ensureFreshAccessToken();
      const expiresAt = getAccessTokenExpiry();
      if (!refreshed || expiresAt === null) {
        clear();
        return;
      }
      scheduleRefreshAt(expiresAt);
    };

    return { apply, clear, cancelTimer };
  }, [queryClient]);

  useEffect(() => {
    // A request that stays unauthorized after a refresh drops us back to the login route.
    onUnauthorized(session.clear);

    let cancelled = false;
    void (async () => {
      const result = await restoreSession();
      if (cancelled) {
        return;
      }
      if (result.status === 'authenticated') {
        session.apply(result.auth);
      } else if (result.status === 'anonymous') {
        session.clear();
      }
      // "unavailable": the API could not be reached, so the stored token is left in place for a
      // later attempt; the user is simply not authenticated yet.
      setIsReady(true);
    })();

    return () => {
      cancelled = true;
      session.cancelTimer();
    };
  }, [session]);

  const login = useCallback(
    async (email: string, password: string): Promise<void> => {
      const auth = await loginRequest(email, password);
      // Belt and braces alongside the clear() in session.clear(): a tab that reached the login form
      // without a sign-out (a hard 401, a restored-then-rejected session) can still be holding the
      // previous parent's cached devices.
      queryClient.clear();
      session.apply(auth);
    },
    [queryClient, session],
  );

  const logout = useCallback(async (): Promise<void> => {
    const refreshToken = getStoredRefreshToken();
    session.clear();
    if (refreshToken !== null) {
      try {
        await logoutRequest(refreshToken);
      } catch {
        // Server-side revocation is best effort: the client session is already gone.
      }
    }
  }, [session]);

  const value = useMemo<AuthContextValue>(
    () => ({ parent, isReady, isAuthenticated: parent !== null, login, logout }),
    [parent, isReady, login, logout],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext);
  if (context === null) {
    throw new Error('useAuth must be used inside <AuthProvider>.');
  }
  return context;
}

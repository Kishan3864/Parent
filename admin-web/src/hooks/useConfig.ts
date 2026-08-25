import { useEffect, useRef } from 'react';
import { useQuery, useQueryClient, type QueryKey, type UseQueryResult } from '@tanstack/react-query';
import { apiFetch } from '../api/client';
import type { AppConfig } from '../api/types';

/** Used until GET /api/v1/config answers (and if it ever answers with a nonsensical value). */
export const DEFAULT_REFRESH_SECONDS = 15;
const MIN_REFRESH_SECONDS = 5;

/** Server-owned thresholds, tile URL and refresh cadence. Changes rarely, so it is cached hard. */
export function useConfig(): UseQueryResult<AppConfig> {
  return useQuery({
    queryKey: ['config'],
    queryFn: () => apiFetch<AppConfig>('/v1/config'),
    staleTime: 5 * 60 * 1000,
    gcTime: 30 * 60 * 1000,
  });
}

/** Poll cadence for the live queries, in milliseconds. */
export function usePollingIntervalMs(): number {
  const { data } = useConfig();
  const seconds = data?.defaultRefreshSeconds ?? DEFAULT_REFRESH_SECONDS;
  return Math.max(seconds, MIN_REFRESH_SECONDS) * 1000;
}

/**
 * Live queries stop polling while the tab is hidden (their refetchInterval returns false), so on
 * the way back the data is stale and the interval is not running. Invalidating on visibilitychange
 * refetches immediately and re-arms the interval.
 */
export function useInvalidateOnVisible(queryKey: QueryKey): void {
  const queryClient = useQueryClient();
  const queryKeyRef = useRef(queryKey);

  useEffect(() => {
    queryKeyRef.current = queryKey;
  });

  useEffect(() => {
    const handleVisibilityChange = (): void => {
      if (!document.hidden) {
        void queryClient.invalidateQueries({ queryKey: queryKeyRef.current });
      }
    };
    document.addEventListener('visibilitychange', handleVisibilityChange);
    return () => document.removeEventListener('visibilitychange', handleVisibilityChange);
  }, [queryClient]);
}

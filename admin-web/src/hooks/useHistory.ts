import { useQuery, type UseQueryResult } from '@tanstack/react-query';
import { getHistory } from '../api/locations';
import type { LocationHistoryResponse } from '../api/types';

/** Both bounds are ISO-8601 UTC strings, as produced by fromLocalInputValue(). */
export interface HistoryRange {
  fromUtc: string;
  toUtc: string;
}

/** Point budget per CONTRACT.md section 6; the server downsamples to it when simplify=true. */
export const HISTORY_LIMIT = 2000;

/** Track for one device over one range. Idle until both a device and a range are chosen. */
export function useHistory(
  deviceId: string | undefined,
  range: HistoryRange | null,
): UseQueryResult<LocationHistoryResponse> {
  return useQuery({
    queryKey: ['devices', deviceId, 'locations', range?.fromUtc ?? null, range?.toUtc ?? null],
    queryFn: () =>
      getHistory(deviceId as string, {
        from: (range as HistoryRange).fromUtc,
        to: (range as HistoryRange).toUtc,
        limit: HISTORY_LIMIT,
        order: 'asc',
        simplify: true,
      }),
    enabled: deviceId !== undefined && deviceId !== '' && range !== null,
    // A closed past range does not change; do not re-fetch it on every remount.
    staleTime: 60 * 1000,
  });
}

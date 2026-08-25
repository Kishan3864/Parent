import { useQuery, type UseQueryResult } from '@tanstack/react-query';
import { getCurrentLocation } from '../api/locations';
import type { LocationSnapshotDto } from '../api/types';
import { useInvalidateOnVisible, usePollingIntervalMs } from './useConfig';

/**
 * Live snapshot for one device. Resolves to null (not an error) when the API answers 204 because
 * the device has never reported a fix. Polls on the configured cadence, never while hidden.
 */
export function useCurrentLocation(deviceId?: string): UseQueryResult<LocationSnapshotDto | null> {
  const intervalMs = usePollingIntervalMs();
  const queryKey = ['devices', deviceId, 'location', 'current'];
  useInvalidateOnVisible(queryKey);

  return useQuery({
    queryKey,
    queryFn: () => getCurrentLocation(deviceId as string),
    enabled: deviceId !== undefined && deviceId !== '',
    refetchInterval: () => (document.hidden ? false : intervalMs),
    refetchIntervalInBackground: false,
  });
}

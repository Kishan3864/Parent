import { useQuery, type UseQueryResult } from '@tanstack/react-query';
import { getDevice, listDevices } from '../api/devices';
import type { DeviceDetailDto, DeviceSummaryDto } from '../api/types';
import { useInvalidateOnVisible, usePollingIntervalMs } from './useConfig';

/**
 * Root key of every device-scoped query. Invalidating it after a create/update/delete refreshes
 * the list, the detail queries and the current-location queries in one call.
 */
export const devicesQueryKey = ['devices'];

/** The dashboard list. Polls on the configured cadence, but never while the tab is hidden. */
export function useDevices(): UseQueryResult<DeviceSummaryDto[]> {
  const intervalMs = usePollingIntervalMs();
  useInvalidateOnVisible(devicesQueryKey);

  return useQuery({
    queryKey: devicesQueryKey,
    queryFn: () => listDevices(),
    refetchInterval: () => (document.hidden ? false : intervalMs),
    refetchIntervalInBackground: false,
  });
}

/** One device with its management detail. Idle until a device is selected. */
export function useDevice(deviceId?: string): UseQueryResult<DeviceDetailDto> {
  return useQuery({
    queryKey: ['devices', deviceId],
    queryFn: () => getDevice(deviceId as string),
    enabled: deviceId !== undefined && deviceId !== '',
  });
}

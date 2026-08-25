import type { DeviceSummaryDto } from '../api/types';
import { useNow } from '../hooks/useNow';
import { DeviceCard } from './DeviceCard';

interface DeviceListProps {
  devices: DeviceSummaryDto[];
  selectedDeviceId: string | null;
  onSelect: (deviceId: string) => void;
}

/**
 * Selectable list of the parent's devices. One shared clock tick drives every card, so the
 * relative times stay in step and only a single timer runs.
 */
export function DeviceList({ devices, selectedDeviceId, onSelect }: DeviceListProps) {
  const now = useNow(1000);

  return (
    <ul className="device-list">
      {devices.map((device) => (
        <li key={device.id}>
          <DeviceCard
            device={device}
            selected={device.id === selectedDeviceId}
            now={now}
            onSelect={onSelect}
          />
        </li>
      ))}
    </ul>
  );
}

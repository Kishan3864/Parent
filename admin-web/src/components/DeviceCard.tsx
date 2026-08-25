import type { DeviceSummaryDto } from '../api/types';
import { formatAccuracy, formatBattery } from '../lib/format';
import { formatAbsolute, formatRelative } from '../lib/time';
import { StatusBadge } from './StatusBadge';

/** Below this the battery reading is called out rather than shown as ordinary metadata. */
const LOW_BATTERY_PERCENT = 20;

interface DeviceCardProps {
  device: DeviceSummaryDto;
  selected: boolean;
  /** Ticking epoch millis from useNow, so the relative time re-renders without a refetch. */
  now: number;
  onSelect: (deviceId: string) => void;
}

export function DeviceCard({ device, selected, now, onSelect }: DeviceCardProps) {
  const battery = device.batteryPercent;
  const isLowBattery = battery !== null && battery < LOW_BATTERY_PERCENT;
  const accuracyMeters = device.lastLocation?.accuracyMeters ?? null;
  const subtitle = device.deviceLabel ?? device.model ?? 'Unlabelled device';

  return (
    <button
      type="button"
      className={selected ? 'device-card device-card--selected' : 'device-card'}
      aria-pressed={selected}
      onClick={() => onSelect(device.id)}
    >
      <span className="device-card__head">
        <span className="device-card__name">{device.childName}</span>
        <StatusBadge status={device.status} />
      </span>

      <span className="device-card__label">{subtitle}</span>

      <span className="device-card__meta">
        <span className={isLowBattery ? 'meta meta--alert' : 'meta'}>
          Battery {formatBattery(battery)}
          {isLowBattery ? ' - low' : ''}
        </span>
        <span className="meta">
          {accuracyMeters === null ? 'No fix yet' : `Accuracy ${formatAccuracy(accuracyMeters)}`}
        </span>
      </span>

      <span
        className="device-card__updated"
        title={device.lastSeenAt === null ? undefined : formatAbsolute(device.lastSeenAt)}
      >
        {device.lastSeenAt === null
          ? 'never reported'
          : `updated ${formatRelative(device.lastSeenAt, now)}`}
      </span>
    </button>
  );
}

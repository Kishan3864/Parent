import type { DeviceStatus } from '../api/types';

/** Wording for the four server-computed states (CONTRACT.md section 1). */
const STATUS_LABELS: Record<DeviceStatus, string> = {
  online: 'Online',
  idle: 'Idle',
  offline: 'Offline',
  neverReported: 'Never reported',
};

export function StatusBadge({ status }: { status: DeviceStatus }) {
  return <span className={`status-badge status-badge--${status}`}>{STATUS_LABELS[status]}</span>;
}

const UNKNOWN = 'n/a';

/** GPS accuracy radius, e.g. "±8 m" / "±1.2 km". */
export function formatAccuracy(meters: number | null | undefined): string {
  if (meters === null || meters === undefined || !Number.isFinite(meters)) {
    return UNKNOWN;
  }
  const value = Math.max(meters, 0);
  return value < 1000 ? `±${Math.round(value)} m` : `±${(value / 1000).toFixed(1)} km`;
}

/** Travelled distance, e.g. "820 m" / "4.21 km". */
export function formatDistance(meters: number | null | undefined): string {
  if (meters === null || meters === undefined || !Number.isFinite(meters)) {
    return UNKNOWN;
  }
  const value = Math.max(meters, 0);
  return value < 1000 ? `${Math.round(value)} m` : `${(value / 1000).toFixed(2)} km`;
}

/** Battery level, e.g. "78%". Devices that never reported one show "n/a". */
export function formatBattery(percent: number | null | undefined): string {
  if (percent === null || percent === undefined || !Number.isFinite(percent)) {
    return UNKNOWN;
  }
  return `${Math.round(Math.min(Math.max(percent, 0), 100))}%`;
}

/** Fixed 5-decimal coordinates (~1 m resolution), e.g. "12.97160, 77.59460". */
export function formatCoords(latitude: number, longitude: number): string {
  if (!Number.isFinite(latitude) || !Number.isFinite(longitude)) {
    return UNKNOWN;
  }
  return `${latitude.toFixed(5)}, ${longitude.toFixed(5)}`;
}

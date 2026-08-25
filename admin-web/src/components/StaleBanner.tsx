import { formatAbsolute, formatRelative } from '../lib/time';

interface StaleBannerProps {
  lastSeenAt: string | null;
  /** Ticking epoch millis from useNow. */
  now: number;
}

/**
 * Sits above the map whenever the fix is stale. It explains the marker; it never replaces it -
 * the last known location stays on the map (CONTRACT.md section 6).
 */
export function StaleBanner({ lastSeenAt, now }: StaleBannerProps) {
  if (lastSeenAt === null) {
    return (
      <p className="stale-banner" role="status">
        <span className="stale-banner__dot" aria-hidden="true" />
        This device has never reported a location.
      </p>
    );
  }

  return (
    <p className="stale-banner" role="status">
      <span className="stale-banner__dot" aria-hidden="true" />
      <span>
        Last known location - device offline since{' '}
        <time dateTime={lastSeenAt} title={formatAbsolute(lastSeenAt)}>
          {formatRelative(lastSeenAt, now)}
        </time>
      </span>
    </p>
  );
}

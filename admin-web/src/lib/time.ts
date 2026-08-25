const SECOND_MS = 1_000;
const MINUTE_MS = 60 * SECOND_MS;
const HOUR_MS = 60 * MINUTE_MS;
const DAY_MS = 24 * HOUR_MS;

/** Anything more recent than this reads as "just now"; also absorbs small clock skew. */
const JUST_NOW_MS = 5 * SECOND_MS;

const absoluteFormatter = new Intl.DateTimeFormat(undefined, {
  dateStyle: 'medium',
  timeStyle: 'medium',
});

function toEpochMillis(value: string | number | Date | null | undefined): number | null {
  if (value === null || value === undefined) {
    return null;
  }
  if (value instanceof Date) {
    return Number.isNaN(value.getTime()) ? null : value.getTime();
  }
  if (typeof value === 'number') {
    return Number.isFinite(value) ? value : null;
  }
  const parsed = Date.parse(value);
  return Number.isNaN(parsed) ? null : parsed;
}

/**
 * "just now" | "42 s ago" | "7 min ago" | "3 h ago" | "2 d ago".
 * Pass the ticking value from useNow() as `now` so relative times re-render on their own.
 */
export function formatRelative(
  value: string | number | Date | null | undefined,
  now: number = Date.now(),
): string {
  if (value === null || value === undefined) {
    return 'never';
  }
  const at = toEpochMillis(value);
  if (at === null) {
    return 'unknown';
  }

  const elapsed = now - at;
  if (elapsed < JUST_NOW_MS) {
    return 'just now';
  }
  if (elapsed < MINUTE_MS) {
    return `${Math.floor(elapsed / SECOND_MS)} s ago`;
  }
  if (elapsed < HOUR_MS) {
    return `${Math.floor(elapsed / MINUTE_MS)} min ago`;
  }
  if (elapsed < DAY_MS) {
    return `${Math.floor(elapsed / HOUR_MS)} h ago`;
  }
  return `${Math.floor(elapsed / DAY_MS)} d ago`;
}

/** Absolute local date + time, e.g. "25 Aug 2026, 10:15:30". */
export function formatAbsolute(value: string | number | Date | null | undefined): string {
  const at = toEpochMillis(value);
  return at === null ? 'unknown' : absoluteFormatter.format(new Date(at));
}

/**
 * Formats an instant for the value of an <input type="datetime-local">, which is LOCAL wall-clock
 * time with no zone: "YYYY-MM-DDTHH:mm". Returns "" when the input cannot be interpreted.
 */
export function toLocalInputValue(value: Date | string | number): string {
  const at = toEpochMillis(value);
  if (at === null) {
    return '';
  }
  const date = new Date(at);
  const pad = (part: number): string => String(part).padStart(2, '0');
  return (
    `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}` +
    `T${pad(date.getHours())}:${pad(date.getMinutes())}`
  );
}

/**
 * Inverse of toLocalInputValue: reads the LOCAL wall-clock value of a datetime-local input and
 * returns the UTC ISO-8601 string the API expects. Returns "" for an empty or malformed value.
 *
 * The components are fed to the Date(y, m, d, ...) constructor, which interprets them in the
 * browser's zone — parsing the string directly would depend on engine-specific rules.
 */
export function fromLocalInputValue(value: string): string {
  const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})(?::(\d{2}))?$/.exec(value.trim());
  if (match === null) {
    return '';
  }

  const [, year, month, day, hours, minutes, seconds] = match;
  const local = new Date(
    Number(year),
    Number(month) - 1,
    Number(day),
    Number(hours),
    Number(minutes),
    seconds === undefined ? 0 : Number(seconds),
    0,
  );
  return Number.isNaN(local.getTime()) ? '' : local.toISOString();
}

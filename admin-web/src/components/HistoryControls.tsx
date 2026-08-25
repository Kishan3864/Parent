import { useEffect, useId, useState } from 'react';
import type { LocationHistoryResponse } from '../api/types';
import type { HistoryRange } from '../hooks/useHistory';
import { formatDistance } from '../lib/format';
import { fromLocalInputValue, toLocalInputValue } from '../lib/time';
import { ErrorNote, Spinner } from './Spinner';

const HOUR_MS = 60 * 60 * 1000;

const QUICK_RANGES: ReadonlyArray<{ label: string; hours: number }> = [
  { label: 'Last 1 h', hours: 1 },
  { label: 'Last 6 h', hours: 6 },
  { label: 'Last 24 h', hours: 24 },
  { label: 'Last 7 d', hours: 24 * 7 },
];

/** Local wall-clock time of an instant, for the "08:15 - 17:40" half of the summary line. */
function clockTime(iso: string): string {
  const at = Date.parse(iso);
  if (Number.isNaN(at)) {
    return '--:--';
  }
  const date = new Date(at);
  return `${String(date.getHours()).padStart(2, '0')}:${String(date.getMinutes()).padStart(2, '0')}`;
}

interface HistoryControlsProps {
  range: HistoryRange | null;
  onRangeChange: (range: HistoryRange | null) => void;
  /** The loaded track, or undefined when no range is active. */
  history: LocationHistoryResponse | undefined;
  isLoading: boolean;
  error: unknown;
  disabled: boolean;
}

export function HistoryControls({
  range,
  onRangeChange,
  history,
  isLoading,
  error,
  disabled,
}: HistoryControlsProps) {
  const fromId = useId();
  const toId = useId();
  const [fromInput, setFromInput] = useState('');
  const [toInput, setToInput] = useState('');
  const [inputError, setInputError] = useState<string | null>(null);
  const [activeQuickHours, setActiveQuickHours] = useState<number | null>(null);

  // The range is owned by the page; the inputs follow it so quick ranges and Clear stay in sync.
  useEffect(() => {
    if (range === null) {
      setFromInput('');
      setToInput('');
      return;
    }
    setFromInput(toLocalInputValue(new Date(range.fromUtc)));
    setToInput(toLocalInputValue(new Date(range.toUtc)));
  }, [range]);

  function applyQuickRange(hours: number): void {
    const to = Date.now();
    setActiveQuickHours(hours);
    setInputError(null);
    onRangeChange({
      fromUtc: new Date(to - hours * HOUR_MS).toISOString(),
      toUtc: new Date(to).toISOString(),
    });
  }

  function applyInputs(nextFrom: string, nextTo: string): void {
    setFromInput(nextFrom);
    setToInput(nextTo);
    setActiveQuickHours(null);

    if (nextFrom === '' || nextTo === '') {
      setInputError(null);
      return;
    }

    const fromUtc = fromLocalInputValue(nextFrom);
    const toUtc = fromLocalInputValue(nextTo);
    if (fromUtc === '' || toUtc === '') {
      setInputError('Enter a complete date and time for both ends of the range.');
      return;
    }
    if (Date.parse(fromUtc) >= Date.parse(toUtc)) {
      setInputError('The start of the range must be before its end.');
      return;
    }

    setInputError(null);
    onRangeChange({ fromUtc, toUtc });
  }

  function clearRange(): void {
    setActiveQuickHours(null);
    setInputError(null);
    onRangeChange(null);
  }

  const points = history?.points ?? [];

  return (
    <section className="panel history" aria-label="Location history">
      <div className="history__row">
        <div className="history__quick" role="group" aria-label="Quick ranges">
          {QUICK_RANGES.map((quick) => (
            <button
              key={quick.hours}
              type="button"
              className="btn btn--sm btn--toggle"
              aria-pressed={activeQuickHours === quick.hours}
              disabled={disabled}
              onClick={() => applyQuickRange(quick.hours)}
            >
              {quick.label}
            </button>
          ))}
          <button
            type="button"
            className="btn btn--sm btn--ghost"
            disabled={disabled || range === null}
            onClick={clearRange}
          >
            Clear
          </button>
        </div>

        <div className="history__inputs">
          <div className="field">
            <label htmlFor={fromId}>From</label>
            <input
              id={fromId}
              type="datetime-local"
              value={fromInput}
              disabled={disabled}
              onChange={(event) => applyInputs(event.target.value, toInput)}
            />
          </div>
          <div className="field">
            <label htmlFor={toId}>To</label>
            <input
              id={toId}
              type="datetime-local"
              value={toInput}
              disabled={disabled}
              onChange={(event) => applyInputs(fromInput, event.target.value)}
            />
          </div>
        </div>
      </div>

      {inputError !== null ? (
        <p className="hint hint--alert" role="alert">
          {inputError}
        </p>
      ) : null}

      <ErrorNote error={error} />

      <div className="history__summary">
        {range === null ? (
          <span className="hint">
            Pick a range to draw the track this device travelled. The live position stays on the map.
          </span>
        ) : isLoading ? (
          <Spinner label="Loading track..." />
        ) : history !== undefined && history.count > 0 && points.length > 0 ? (
          <>
            <span>
              <strong>{history.count}</strong> points,{' '}
              <strong>{formatDistance(history.distanceMeters)}</strong>,{' '}
              {clockTime(points[0].recordedAt)} - {clockTime(points[points.length - 1].recordedAt)}
            </span>
            {history.simplified ? (
              <span className="hint">downsampled from {history.totalMatched} recorded fixes</span>
            ) : null}
          </>
        ) : history !== undefined ? (
          <span className="hint">No location data in this range</span>
        ) : null}
      </div>
    </section>
  );
}

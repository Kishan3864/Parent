import { ApiError } from '../api/client';

/**
 * Inline busy indicator. The bare `.spinner` element is also used on its own by the route guard,
 * so the disc itself carries that class and the wrapper adds the label.
 */
export function Spinner({ label = 'Loading...' }: { label?: string }) {
  return (
    <span className="spinner-inline" role="status" aria-live="polite">
      <span className="spinner" aria-hidden="true" />
      <span>{label}</span>
    </span>
  );
}

/** Turns anything thrown by the API layer into one sentence a parent can act on. */
export function describeError(error: unknown): string {
  if (error instanceof ApiError) {
    const validation = error.problem?.errors;
    if (validation !== undefined) {
      const messages = Object.values(validation)
        .flat()
        .filter((message) => message.trim() !== '');
      if (messages.length > 0) {
        return messages.join(' ');
      }
    }

    const detail = error.problem?.detail?.trim();
    if (detail !== undefined && detail !== '') {
      return detail;
    }

    const title = error.problem?.title?.trim();
    if (title !== undefined && title !== '') {
      return title;
    }

    return error.status === 0
      ? 'The API could not be reached.'
      : `Request failed (HTTP ${error.status}).`;
  }

  if (error instanceof Error && error.message.trim() !== '') {
    return error.message;
  }

  return 'Something went wrong. Please try again.';
}

/** Inline error line. Renders nothing when there is no error, so callers can pass a query error. */
export function ErrorNote({ error }: { error: unknown }) {
  if (error === null || error === undefined) {
    return null;
  }
  return (
    <p className="error-note" role="alert">
      {describeError(error)}
    </p>
  );
}

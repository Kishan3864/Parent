import { useCallback, useSyncExternalStore } from 'react';

interface Ticker {
  now: number;
  listeners: Set<() => void>;
  handle: ReturnType<typeof setInterval> | null;
}

/** One ticker per distinct interval, shared by every component that asks for it. */
const tickers = new Map<number, Ticker>();

function getTicker(intervalMs: number): Ticker {
  const existing = tickers.get(intervalMs);
  if (existing !== undefined) {
    return existing;
  }
  const created: Ticker = { now: Date.now(), listeners: new Set(), handle: null };
  tickers.set(intervalMs, created);
  return created;
}

function subscribe(intervalMs: number, onStoreChange: () => void): () => void {
  const ticker = getTicker(intervalMs);
  ticker.listeners.add(onStoreChange);

  if (ticker.handle === null) {
    // First subscriber: the stored value may be stale from a previous mount.
    ticker.now = Date.now();
    ticker.handle = setInterval(() => {
      ticker.now = Date.now();
      for (const listener of ticker.listeners) {
        listener();
      }
    }, intervalMs);
  }

  return () => {
    ticker.listeners.delete(onStoreChange);
    if (ticker.listeners.size === 0 && ticker.handle !== null) {
      clearInterval(ticker.handle);
      ticker.handle = null;
    }
  };
}

/**
 * Epoch milliseconds that tick, so relative times ("42 s ago") re-render without every component
 * owning a timer. The interval runs only while at least one component is subscribed.
 */
export function useNow(intervalMs: number = 1000): number {
  const subscribeToTicker = useCallback(
    (onStoreChange: () => void) => subscribe(intervalMs, onStoreChange),
    [intervalMs],
  );
  const getSnapshot = useCallback(() => getTicker(intervalMs).now, [intervalMs]);

  return useSyncExternalStore(subscribeToTicker, getSnapshot, getSnapshot);
}

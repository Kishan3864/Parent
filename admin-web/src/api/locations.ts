import { apiFetch } from './client';
import type { LocationHistoryResponse, LocationSnapshotDto } from './types';

/** Query parameters of GET /api/v1/devices/{id}/locations (CONTRACT.md section 2.5). */
export interface HistoryQuery {
  /** ISO-8601 UTC; defaults server-side to now-24h. */
  from?: string;
  /** ISO-8601 UTC; defaults server-side to now. */
  to?: string;
  /** Default 1000, max 5000. */
  limit?: number;
  order?: 'asc' | 'desc';
  /** Drop fixes worse than this accuracy. */
  minAccuracyMeters?: number;
  /** Server-side even-stride downsample to at most `limit` points. */
  simplify?: boolean;
}

/** Returns null when the device has never reported a fix (the API answers 204). */
export async function getCurrentLocation(deviceId: string): Promise<LocationSnapshotDto | null> {
  const snapshot = await apiFetch<LocationSnapshotDto | null>(
    `/v1/devices/${encodeURIComponent(deviceId)}/location/current`,
  );
  return snapshot ?? null;
}

export function getHistory(deviceId: string, query: HistoryQuery = {}): Promise<LocationHistoryResponse> {
  const params = new URLSearchParams();
  if (query.from !== undefined) params.set('from', query.from);
  if (query.to !== undefined) params.set('to', query.to);
  if (query.limit !== undefined) params.set('limit', String(query.limit));
  if (query.order !== undefined) params.set('order', query.order);
  if (query.minAccuracyMeters !== undefined) {
    params.set('minAccuracyMeters', String(query.minAccuracyMeters));
  }
  if (query.simplify !== undefined) params.set('simplify', String(query.simplify));

  const search = params.toString();
  const path = `/v1/devices/${encodeURIComponent(deviceId)}/locations`;
  return apiFetch<LocationHistoryResponse>(search === '' ? path : `${path}?${search}`);
}

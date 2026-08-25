/**
 * Wire DTOs. These mirror CONTRACT.md section 2 exactly: camelCase names, same nullability.
 * All timestamps are ISO-8601 UTC strings with a `Z` suffix and millisecond precision.
 */

/** RFC7807 body returned by every error response (`application/problem+json`). */
export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  /** Populated by ASP.NET model-validation failures: field name -> messages. */
  errors?: Record<string, string[]>;
  /** RFC7807 allows arbitrary extension members. */
  [extension: string]: unknown;
}

export type DeviceStatus = 'neverReported' | 'online' | 'idle' | 'offline';

export type LocationProvider = 'unknown' | 'gps' | 'network' | 'fused' | 'passive';

export interface ParentDto {
  id: string;
  email: string;
  displayName: string;
  createdAt: string;
}

/**
 * The identity embedded in `AuthResponse` (contract §2.1) — id/email/displayName only. It is NOT a
 * `ParentDto`: `createdAt` is carried by `GET /auth/me` alone, so asserting it here would type a
 * value the server never sends.
 */
export interface AuthParentDto {
  id: string;
  email: string;
  displayName: string;
}

export interface RegisterRequest {
  email: string;
  password: string;
  displayName: string;
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface RefreshRequest {
  refreshToken: string;
}

export interface AuthResponse {
  accessToken: string;
  refreshToken: string;
  expiresAtUtc: string;
  parent: AuthParentDto;
}

export interface LocationPointDto {
  id: number;
  latitude: number;
  longitude: number;
  accuracyMeters: number;
  altitudeMeters: number | null;
  speedMetersPerSecond: number | null;
  bearingDegrees: number | null;
  batteryPercent: number | null;
  isCharging: boolean | null;
  provider: LocationProvider;
  recordedAt: string;
  receivedAt: string;
}

export interface DeviceSummaryDto {
  id: string;
  childName: string;
  deviceLabel: string | null;
  platform: string | null;
  model: string | null;
  isActive: boolean;
  isPaired: boolean;
  hasActiveSession: boolean;
  status: DeviceStatus;
  isStale: boolean;
  lastSeenAt: string | null;
  /** Null while the device has never reported a fix. */
  secondsSinceUpdate: number | null;
  batteryPercent: number | null;
  lastLocation: LocationPointDto | null;
}

export interface DeviceDetailDto extends DeviceSummaryDto {
  createdAt: string;
  pairedAt: string | null;
  /** Plaintext only in the response that created or regenerated it; null everywhere else. */
  pairingCode: string | null;
  pairingCodeExpiresAtUtc: string | null;
  appVersion: string | null;
  osVersion: string | null;
}

export interface CreateDeviceRequest {
  childName: string;
  deviceLabel?: string | null;
}

export interface UpdateDeviceRequest {
  childName?: string | null;
  deviceLabel?: string | null;
  isActive?: boolean | null;
}

export interface PairingCodeDto {
  pairingCode: string;
  expiresAtUtc: string;
}

export interface LocationSnapshotDto {
  deviceId: string;
  childName: string;
  status: DeviceStatus;
  isStale: boolean;
  secondsSinceUpdate: number | null;
  serverTimeUtc: string;
  location: LocationPointDto | null;
}

export interface LocationHistoryResponse {
  deviceId: string;
  childName: string;
  fromUtc: string;
  toUtc: string;
  count: number;
  totalMatched: number;
  simplified: boolean;
  distanceMeters: number;
  points: LocationPointDto[];
}

/** GET /api/v1/config — server-owned thresholds so the UI never re-derives them. */
export interface AppConfig {
  onlineThresholdSeconds: number;
  staleThresholdSeconds: number;
  defaultRefreshSeconds: number;
  mapTileUrl: string;
  mapAttribution: string;
}

/** Tracking parameters handed to the child device on enrollment. */
export interface TrackingConfigDto {
  intervalSeconds: number;
  fastestIntervalSeconds: number;
  minDistanceMeters: number;
  batchMaxSize: number;
  uploadIntervalSeconds: number;
}

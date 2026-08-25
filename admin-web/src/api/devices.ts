import { apiFetch } from './client';
import type {
  CreateDeviceRequest,
  DeviceDetailDto,
  DeviceSummaryDto,
  PairingCodeDto,
  UpdateDeviceRequest,
} from './types';

export function listDevices(): Promise<DeviceSummaryDto[]> {
  return apiFetch<DeviceSummaryDto[]>('/v1/devices');
}

export function getDevice(deviceId: string): Promise<DeviceDetailDto> {
  return apiFetch<DeviceDetailDto>(`/v1/devices/${encodeURIComponent(deviceId)}`);
}

/** The returned DeviceDetailDto is the only response that carries the plaintext pairing code. */
export function createDevice(request: CreateDeviceRequest): Promise<DeviceDetailDto> {
  return apiFetch<DeviceDetailDto>('/v1/devices', {
    method: 'POST',
    body: JSON.stringify(request),
  });
}

export function updateDevice(deviceId: string, request: UpdateDeviceRequest): Promise<DeviceDetailDto> {
  return apiFetch<DeviceDetailDto>(`/v1/devices/${encodeURIComponent(deviceId)}`, {
    method: 'PATCH',
    body: JSON.stringify(request),
  });
}

export function deleteDevice(deviceId: string): Promise<void> {
  return apiFetch<void>(`/v1/devices/${encodeURIComponent(deviceId)}`, { method: 'DELETE' });
}

export function regeneratePairingCode(deviceId: string): Promise<PairingCodeDto> {
  return apiFetch<PairingCodeDto>(`/v1/devices/${encodeURIComponent(deviceId)}/pairing-code`, {
    method: 'POST',
  });
}

/** Revokes every session of the device; it receives a 401 on its next call. */
export function revokeDevice(deviceId: string): Promise<void> {
  return apiFetch<void>(`/v1/devices/${encodeURIComponent(deviceId)}/revoke`, { method: 'POST' });
}

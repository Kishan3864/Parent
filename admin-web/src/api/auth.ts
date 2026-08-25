import { apiFetch } from './client';
import type { AuthResponse, LoginRequest, ParentDto, RefreshRequest, RegisterRequest } from './types';

export function login(email: string, password: string): Promise<AuthResponse> {
  const body: LoginRequest = { email, password };
  return apiFetch<AuthResponse>('/v1/auth/login', {
    method: 'POST',
    auth: false,
    body: JSON.stringify(body),
  });
}

export function register(email: string, password: string, displayName: string): Promise<AuthResponse> {
  const body: RegisterRequest = { email, password, displayName };
  return apiFetch<AuthResponse>('/v1/auth/register', {
    method: 'POST',
    auth: false,
    body: JSON.stringify(body),
  });
}

export function refresh(refreshToken: string): Promise<AuthResponse> {
  const body: RefreshRequest = { refreshToken };
  return apiFetch<AuthResponse>('/v1/auth/refresh', {
    method: 'POST',
    auth: false,
    body: JSON.stringify(body),
  });
}

export function logout(refreshToken: string): Promise<void> {
  const body: RefreshRequest = { refreshToken };
  return apiFetch<void>('/v1/auth/logout', {
    method: 'POST',
    auth: false,
    body: JSON.stringify(body),
  });
}

export function me(): Promise<ParentDto> {
  return apiFetch<ParentDto>('/v1/auth/me');
}

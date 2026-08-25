import { useEffect, useId, useState } from 'react';
import type { FormEvent } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import {
  createDevice,
  deleteDevice,
  regeneratePairingCode,
  revokeDevice,
} from '../api/devices';
import type { CreateDeviceRequest, DeviceSummaryDto } from '../api/types';
import { ErrorNote, Spinner } from '../components/Spinner';
import { StatusBadge } from '../components/StatusBadge';
import { devicesQueryKey, useDevices } from '../hooks/useDevices';
import { useNow } from '../hooks/useNow';
import { formatAbsolute, formatRelative } from '../lib/time';

/** A plaintext pairing code, which the API returns exactly once per generation. */
interface PairingCodeView {
  deviceId: string;
  childName: string;
  pairingCode: string;
  expiresAtUtc: string;
}

type CopyState = 'idle' | 'copied' | 'failed';

function formatCountdown(remainingMs: number): string {
  const totalSeconds = Math.max(0, Math.floor(remainingMs / 1000));
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  return `${minutes}:${String(seconds).padStart(2, '0')}`;
}

function pairingStateLabel(device: DeviceSummaryDto): string {
  if (!device.isActive) {
    return 'Disabled';
  }
  if (!device.isPaired) {
    return 'Waiting for pairing';
  }
  return device.hasActiveSession ? 'Paired' : 'Paired - sessions revoked';
}

export default function DevicesPage() {
  const devicesQuery = useDevices();
  const queryClient = useQueryClient();
  const now = useNow(1000);
  const childNameId = useId();
  const deviceLabelId = useId();

  const [childName, setChildName] = useState('');
  const [deviceLabel, setDeviceLabel] = useState('');
  const [pairing, setPairing] = useState<PairingCodeView | null>(null);
  const [copyState, setCopyState] = useState<CopyState>('idle');

  // Every device-scoped query key starts with this root, so one call refreshes the list, the
  // detail queries, the current-location polls and any loaded history.
  function invalidateDevices(): void {
    void queryClient.invalidateQueries({ queryKey: devicesQueryKey });
  }

  const createMutation = useMutation({
    mutationFn: (request: CreateDeviceRequest) => createDevice(request),
    onSuccess: (device) => {
      setChildName('');
      setDeviceLabel('');
      setPairing(
        device.pairingCode !== null && device.pairingCodeExpiresAtUtc !== null
          ? {
              deviceId: device.id,
              childName: device.childName,
              pairingCode: device.pairingCode,
              expiresAtUtc: device.pairingCodeExpiresAtUtc,
            }
          : null,
      );
      invalidateDevices();
    },
  });

  const regenerateMutation = useMutation({
    mutationFn: (device: DeviceSummaryDto) => regeneratePairingCode(device.id),
    onSuccess: (code, device) => {
      setPairing({
        deviceId: device.id,
        childName: device.childName,
        pairingCode: code.pairingCode,
        expiresAtUtc: code.expiresAtUtc,
      });
      invalidateDevices();
    },
  });

  const revokeMutation = useMutation({
    mutationFn: (device: DeviceSummaryDto) => revokeDevice(device.id),
    onSuccess: () => invalidateDevices(),
  });

  const deleteMutation = useMutation({
    mutationFn: (device: DeviceSummaryDto) => deleteDevice(device.id),
    onSuccess: (_result, device) => {
      setPairing((current) => (current?.deviceId === device.id ? null : current));
      invalidateDevices();
    },
  });

  // A freshly issued code must not inherit the previous code's "Copied" confirmation.
  useEffect(() => {
    setCopyState('idle');
  }, [pairing?.pairingCode]);

  function handleCreate(event: FormEvent<HTMLFormElement>): void {
    event.preventDefault();
    const name = childName.trim();
    if (name === '') {
      return;
    }
    const label = deviceLabel.trim();
    createMutation.mutate({ childName: name, deviceLabel: label === '' ? null : label });
  }

  async function copyCode(code: string): Promise<void> {
    // Typed as always present, but absent outside a secure context.
    const clipboard: Clipboard | undefined = navigator.clipboard;
    if (clipboard === undefined) {
      setCopyState('failed');
      return;
    }
    try {
      await clipboard.writeText(code);
      setCopyState('copied');
    } catch {
      // Clipboard access is refused on insecure origins and by some policies; the code is
      // selectable, so tell the parent to copy it by hand instead of failing silently.
      setCopyState('failed');
    }
  }

  function handleRegenerate(device: DeviceSummaryDto): void {
    regenerateMutation.mutate(device);
  }

  function handleRevoke(device: DeviceSummaryDto): void {
    const confirmed = window.confirm(
      `Revoke the device sessions for ${device.childName}?\n\n` +
        'The child app will immediately stop sharing the location of this device and will show ' +
        'that sharing was turned off. To resume, pair the device again with a new code.',
    );
    if (confirmed) {
      revokeMutation.mutate(device);
    }
  }

  function handleDelete(device: DeviceSummaryDto): void {
    const confirmed = window.confirm(
      `Delete the device registered for ${device.childName}?\n\n` +
        'All of its stored location history is deleted permanently and cannot be recovered. ' +
        'The child app will stop sharing.',
    );
    if (confirmed) {
      deleteMutation.mutate(device);
    }
  }

  const devices = devicesQuery.data ?? [];
  const rowError = regenerateMutation.error ?? revokeMutation.error ?? deleteMutation.error;

  const expiresAtMs = pairing === null ? Number.NaN : Date.parse(pairing.expiresAtUtc);
  const hasExpiry = !Number.isNaN(expiresAtMs);
  const remainingMs = expiresAtMs - now;

  return (
    <div className="devices-page">
      <div className="page-head">
        <div>
          <h1>Child devices</h1>
          <p>Pair a device once; it then shares its location with this account until you revoke it.</p>
        </div>
      </div>

      <section className="panel" aria-label="Add a child device">
        <div className="panel__header">
          <h2>Add child device</h2>
        </div>
        <div className="panel__body">
          <form className="create-device-form" onSubmit={handleCreate}>
            <div className="field">
              <label htmlFor={childNameId}>Child name</label>
              <input
                id={childNameId}
                type="text"
                required
                maxLength={128}
                autoComplete="off"
                value={childName}
                disabled={createMutation.isPending}
                onChange={(event) => setChildName(event.target.value)}
              />
            </div>
            <div className="field">
              <label htmlFor={deviceLabelId}>Device label</label>
              <input
                id={deviceLabelId}
                type="text"
                maxLength={128}
                autoComplete="off"
                placeholder="Pixel 7"
                value={deviceLabel}
                disabled={createMutation.isPending}
                onChange={(event) => setDeviceLabel(event.target.value)}
              />
            </div>
            <button
              type="submit"
              className="btn btn--primary"
              disabled={createMutation.isPending || childName.trim() === ''}
            >
              {createMutation.isPending ? 'Creating...' : 'Create and get pairing code'}
            </button>
          </form>

          <ErrorNote error={createMutation.error} />

          {pairing !== null ? (
            <section className="pairing-panel" aria-live="polite">
              <h3>Pairing code for {pairing.childName}</h3>
              <p className="pairing-panel__code">{pairing.pairingCode}</p>

              <div className="pairing-panel__actions">
                <button type="button" className="btn" onClick={() => void copyCode(pairing.pairingCode)}>
                  Copy code
                </button>
                <span
                  className={
                    hasExpiry && remainingMs <= 0
                      ? 'pairing-panel__expiry pairing-panel__expiry--expired'
                      : 'pairing-panel__expiry'
                  }
                >
                  {!hasExpiry
                    ? 'Expiry unknown - regenerate the code if pairing fails.'
                    : remainingMs <= 0
                      ? 'Expired - regenerate the code.'
                      : `Expires in ${formatCountdown(remainingMs)}`}
                </span>
                <button type="button" className="btn btn--ghost btn--sm" onClick={() => setPairing(null)}>
                  Dismiss
                </button>
              </div>

              {copyState === 'copied' ? <p className="hint">Copied to the clipboard.</p> : null}
              {copyState === 'failed' ? (
                <p className="hint hint--alert">
                  Could not copy automatically - select the code above and copy it manually.
                </p>
              ) : null}

              <p className="hint">
                On the child device: open ParentalTrack, accept the consent screen, then enter this
                code on the pairing screen before it expires.
              </p>
            </section>
          ) : null}
        </div>
      </section>

      <section className="panel" aria-label="Devices">
        <div className="panel__header">
          <h2>Devices</h2>
        </div>

        <div className="panel__body">
          <ErrorNote error={devicesQuery.error} />
          <ErrorNote error={rowError} />

          {devicesQuery.isPending ? (
            <Spinner label="Loading devices..." />
          ) : devices.length === 0 ? (
            <p className="hint">
              No devices yet. Create one above to get a pairing code for the child&apos;s phone.
            </p>
          ) : (
            <div className="table-scroll">
              <table className="device-table">
                <thead>
                  <tr>
                    <th scope="col">Child</th>
                    <th scope="col">Device</th>
                    <th scope="col">Status</th>
                    <th scope="col">Last seen</th>
                    <th scope="col">Pairing</th>
                    <th scope="col">Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {devices.map((device) => {
                    const isRegenerating =
                      regenerateMutation.isPending && regenerateMutation.variables?.id === device.id;
                    const isRevoking =
                      revokeMutation.isPending && revokeMutation.variables?.id === device.id;
                    const isDeleting =
                      deleteMutation.isPending && deleteMutation.variables?.id === device.id;
                    const isBusy = isRegenerating || isRevoking || isDeleting;

                    return (
                      <tr key={device.id}>
                        <td className="device-table__child">{device.childName}</td>
                        <td className="device-table__muted">
                          {device.deviceLabel ?? device.model ?? 'Unlabelled device'}
                        </td>
                        <td>
                          <StatusBadge status={device.status} />
                        </td>
                        <td
                          className="device-table__muted"
                          title={
                            device.lastSeenAt === null ? undefined : formatAbsolute(device.lastSeenAt)
                          }
                        >
                          {device.lastSeenAt === null
                            ? 'Never'
                            : formatRelative(device.lastSeenAt, now)}
                        </td>
                        <td className="device-table__muted">{pairingStateLabel(device)}</td>
                        <td>
                          <div className="device-table__actions">
                            <button
                              type="button"
                              className="btn btn--sm"
                              disabled={isBusy}
                              onClick={() => handleRegenerate(device)}
                            >
                              {isRegenerating ? 'Working...' : 'New code'}
                            </button>
                            <button
                              type="button"
                              className="btn btn--sm"
                              disabled={isBusy || !device.hasActiveSession}
                              onClick={() => handleRevoke(device)}
                            >
                              {isRevoking ? 'Working...' : 'Revoke sessions'}
                            </button>
                            <button
                              type="button"
                              className="btn btn--sm btn--danger"
                              disabled={isBusy}
                              onClick={() => handleDelete(device)}
                            >
                              {isDeleting ? 'Deleting...' : 'Delete'}
                            </button>
                          </div>
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          )}
        </div>
      </section>
    </div>
  );
}

import { useEffect, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { DeviceList } from '../components/DeviceList';
import { HistoryControls } from '../components/HistoryControls';
import { MapPanel } from '../components/MapPanel';
import { ErrorNote, Spinner } from '../components/Spinner';
import { useCurrentLocation } from '../hooks/useCurrentLocation';
import { useDevices } from '../hooks/useDevices';
import { useHistory } from '../hooks/useHistory';
import type { HistoryRange } from '../hooks/useHistory';

/** The selection lives in the URL so the view is linkable and survives a reload. */
const DEVICE_PARAM = 'device';

export default function DashboardPage() {
  const devicesQuery = useDevices();
  const [searchParams, setSearchParams] = useSearchParams();
  const [range, setRange] = useState<HistoryRange | null>(null);

  const devices = devicesQuery.data ?? [];
  const requestedId = searchParams.get(DEVICE_PARAM);
  const selectedDevice = devices.find((device) => device.id === requestedId) ?? devices[0] ?? null;
  const selectedId = selectedDevice?.id ?? null;

  // Write the resolved selection back, so a missing or stale ?device= becomes a real one.
  useEffect(() => {
    if (selectedId === null || selectedId === requestedId) {
      return;
    }
    const next = new URLSearchParams(searchParams);
    next.set(DEVICE_PARAM, selectedId);
    setSearchParams(next, { replace: true });
  }, [selectedId, requestedId, searchParams, setSearchParams]);

  const snapshotQuery = useCurrentLocation(selectedId ?? undefined);
  const historyQuery = useHistory(selectedId ?? undefined, range);

  function handleSelect(deviceId: string): void {
    const next = new URLSearchParams(searchParams);
    next.set(DEVICE_PARAM, deviceId);
    setSearchParams(next);
  }

  if (devicesQuery.isPending) {
    return (
      <div className="panel panel__body">
        <Spinner label="Loading devices..." />
      </div>
    );
  }

  if (devicesQuery.isError) {
    return (
      <div className="panel panel__body">
        <ErrorNote error={devicesQuery.error} />
        <div>
          <button type="button" className="btn" onClick={() => void devicesQuery.refetch()}>
            Try again
          </button>
        </div>
      </div>
    );
  }

  if (devices.length === 0) {
    return (
      <div className="empty-state">
        <h2>No child devices yet</h2>
        <p>
          Add a child device to get a pairing code, then enter that code in the ParentalTrack app on
          the child&apos;s phone. Its location appears here once it starts sharing.
        </p>
        <Link className="btn btn--primary" to="/devices">
          Add a child device
        </Link>
      </div>
    );
  }

  return (
    <div className="dashboard">
      <aside className="dashboard__aside">
        <div className="panel">
          <div className="panel__header">
            <h2>Devices</h2>
            <Link className="btn btn--sm btn--ghost" to="/devices">
              Manage
            </Link>
          </div>
          <DeviceList devices={devices} selectedDeviceId={selectedId} onSelect={handleSelect} />
        </div>
      </aside>

      <div className="dashboard__main">
        <MapPanel
          device={selectedDevice}
          snapshot={snapshotQuery.data}
          isSnapshotLoading={selectedId !== null && snapshotQuery.isPending}
          history={range === null ? undefined : historyQuery.data}
        />

        <HistoryControls
          range={range}
          onRangeChange={setRange}
          history={range === null ? undefined : historyQuery.data}
          isLoading={historyQuery.isPending}
          error={range === null ? null : historyQuery.error}
          disabled={selectedId === null}
        />
      </div>
    </div>
  );
}

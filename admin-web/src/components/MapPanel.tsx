import 'leaflet/dist/leaflet.css';

import { useEffect, useMemo, useRef, useState } from 'react';
import { divIcon, latLngBounds } from 'leaflet';
import type { DivIcon, LatLngBounds, LatLngTuple } from 'leaflet';
import { Circle, MapContainer, Marker, Polyline, Popup, TileLayer, useMap } from 'react-leaflet';
import type {
  DeviceSummaryDto,
  LocationHistoryResponse,
  LocationProvider,
  LocationSnapshotDto,
} from '../api/types';
import { useConfig } from '../hooks/useConfig';
import { useNow } from '../hooks/useNow';
import { formatAccuracy, formatBattery, formatCoords } from '../lib/format';
import { formatAbsolute, formatRelative } from '../lib/time';
import { Spinner } from './Spinner';
import { StaleBanner } from './StaleBanner';

/** Used until GET /api/v1/config answers, and if it ever answers without a tile URL. */
const DEFAULT_TILE_URL = 'https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png';
const DEFAULT_ATTRIBUTION = '(c) OpenStreetMap contributors';

/** Whole-world view for a device that has never reported a fix. */
const DEFAULT_CENTER: LatLngTuple = [20, 0];
const DEFAULT_ZOOM = 2;
const FOCUS_ZOOM = 16;

/** Below this the map is not moved: re-centring on GPS jitter makes the map unusable. */
const RECENTRE_THRESHOLD_METERS = 40;

const LIVE_COLOR = '#2f6fed';
const STALE_COLOR = '#7c8798';
const TRACK_COLOR = '#7a3fd8';
const START_COLOR = '#197f45';
const END_COLOR = '#bd2f45';

const PROVIDER_LABELS: Record<LocationProvider, string> = {
  unknown: 'Unknown',
  gps: 'GPS',
  network: 'Network',
  fused: 'Fused',
  passive: 'Passive',
};

/**
 * Leaflet's default marker icons are loaded from image URLs that break under a bundler, so every
 * marker here is a divIcon carrying inline SVG - no image assets are involved.
 */
function pinIcon(fill: string, stroke: string, dashed: boolean): DivIcon {
  const dash = dashed ? ' stroke-dasharray="4 3"' : '';
  return divIcon({
    className: 'pt-marker',
    html:
      '<svg width="26" height="34" viewBox="0 0 26 34" xmlns="http://www.w3.org/2000/svg">' +
      `<path d="M13 33S24 19.6 24 12A11 11 0 1 0 2 12c0 7.6 11 21 11 21z" fill="${fill}" ` +
      `fill-opacity="0.95" stroke="${stroke}" stroke-width="2" stroke-linejoin="round"${dash} />` +
      '<circle cx="13" cy="12" r="4.4" fill="#ffffff" />' +
      '</svg>',
    iconSize: [26, 34],
    iconAnchor: [13, 33],
    popupAnchor: [0, -30],
  });
}

function dotIcon(fill: string): DivIcon {
  return divIcon({
    className: 'pt-marker',
    html:
      '<svg width="18" height="18" viewBox="0 0 18 18" xmlns="http://www.w3.org/2000/svg">' +
      `<circle cx="9" cy="9" r="6" fill="${fill}" stroke="#ffffff" stroke-width="2.5" />` +
      '</svg>',
    iconSize: [18, 18],
    iconAnchor: [9, 9],
  });
}

const LIVE_PIN = pinIcon(LIVE_COLOR, '#1b45a6', false);
const STALE_PIN = pinIcon(STALE_COLOR, '#4a5361', true);
const START_PIN = dotIcon(START_COLOR);
const END_PIN = dotIcon(END_COLOR);

const EARTH_RADIUS_METERS = 6_371_000;

function metersBetween(a: LatLngTuple, b: LatLngTuple): number {
  const toRad = (degrees: number): number => (degrees * Math.PI) / 180;
  const dLat = toRad(b[0] - a[0]);
  const dLon = toRad(b[1] - a[1]);
  const h =
    Math.sin(dLat / 2) ** 2 +
    Math.cos(toRad(a[0])) * Math.cos(toRad(b[0])) * Math.sin(dLon / 2) ** 2;
  return 2 * EARTH_RADIUS_METERS * Math.asin(Math.min(1, Math.sqrt(h)));
}

interface MapControllerProps {
  deviceId: string | null;
  centerLat: number | null;
  centerLon: number | null;
  /** Non-null while a history range is loaded; the view then belongs to the track. */
  trackBounds: LatLngBounds | null;
}

/**
 * Owns every programmatic move of the map. It re-centres on the selected device but stops as soon
 * as the parent pans, so the map never fights the user; selecting another device (or loading a
 * track) is an explicit request for a new view and clears that flag.
 */
function MapController({ deviceId, centerLat, centerLon, trackBounds }: MapControllerProps) {
  const map = useMap();
  const userPannedRef = useRef(false);
  const lastCenterRef = useRef<LatLngTuple | null>(null);

  useEffect(() => {
    const markPanned = (): void => {
      userPannedRef.current = true;
    };
    map.on('dragstart', markPanned);
    return () => {
      map.off('dragstart', markPanned);
    };
  }, [map]);

  useEffect(() => {
    userPannedRef.current = false;
    lastCenterRef.current = null;
  }, [deviceId]);

  useEffect(() => {
    if (centerLat === null || centerLon === null) {
      return;
    }
    const next: LatLngTuple = [centerLat, centerLon];
    const previous = lastCenterRef.current;
    lastCenterRef.current = next;

    if (previous === null) {
      map.setView(next, Math.max(map.getZoom(), FOCUS_ZOOM));
      return;
    }
    // A track owns the viewport while it is displayed, and a panned map owns itself.
    if (trackBounds !== null || userPannedRef.current) {
      return;
    }
    if (metersBetween(previous, next) < RECENTRE_THRESHOLD_METERS) {
      return;
    }
    map.panTo(next);
  }, [map, centerLat, centerLon, trackBounds]);

  useEffect(() => {
    if (trackBounds === null) {
      return;
    }
    map.fitBounds(trackBounds, { padding: [32, 32] });
    userPannedRef.current = false;
  }, [map, trackBounds]);

  return null;
}

interface MapPanelProps {
  device: DeviceSummaryDto | null;
  snapshot: LocationSnapshotDto | null | undefined;
  isSnapshotLoading: boolean;
  /** Only set while a range is active; the caller clears it when the range is cleared. */
  history: LocationHistoryResponse | undefined;
}

export function MapPanel({ device, snapshot, isSnapshotLoading, history }: MapPanelProps) {
  const now = useNow(1000);
  const { data: config } = useConfig();

  const tileUrl = config?.mapTileUrl ?? DEFAULT_TILE_URL;
  const attribution = config?.mapAttribution ?? DEFAULT_ATTRIBUTION;

  // The snapshot is the freshest source; the list row keeps the map populated while it loads.
  const point = snapshot?.location ?? device?.lastLocation ?? null;
  const isStale = snapshot?.isStale ?? device?.isStale ?? false;
  const lastSeenAt = point?.recordedAt ?? device?.lastSeenAt ?? null;

  const trackPositions = useMemo<LatLngTuple[]>(
    () => (history?.points ?? []).map((p): LatLngTuple => [p.latitude, p.longitude]),
    [history],
  );
  const trackBounds = useMemo<LatLngBounds | null>(
    () => (trackPositions.length >= 2 ? latLngBounds(trackPositions) : null),
    [trackPositions],
  );

  // MapContainer reads these once, at mount; every later move goes through MapController.
  const [initialView] = useState(() => ({
    center: point === null ? DEFAULT_CENTER : ([point.latitude, point.longitude] as LatLngTuple),
    zoom: point === null ? DEFAULT_ZOOM : FOCUS_ZOOM,
  }));

  return (
    <section className="map-pane" aria-label="Location map">
      {isStale && point !== null ? <StaleBanner lastSeenAt={lastSeenAt} now={now} /> : null}

      <div className="map-pane__canvas">
        <MapContainer
          className="map-pane__map"
          center={initialView.center}
          zoom={initialView.zoom}
          scrollWheelZoom
        >
          <TileLayer url={tileUrl} attribution={attribution} maxZoom={19} />

          <MapController
            deviceId={device?.id ?? null}
            centerLat={point?.latitude ?? null}
            centerLon={point?.longitude ?? null}
            trackBounds={trackBounds}
          />

          {trackPositions.length >= 2 ? (
            <>
              <Polyline
                positions={trackPositions}
                pathOptions={{ color: TRACK_COLOR, weight: 4, opacity: 0.85 }}
              />
              <Marker position={trackPositions[0]} icon={START_PIN} title="Track start" />
              <Marker
                position={trackPositions[trackPositions.length - 1]}
                icon={END_PIN}
                title="Track end"
              />
            </>
          ) : null}

          {point !== null ? (
            <>
              <Circle
                center={[point.latitude, point.longitude]}
                radius={Math.max(point.accuracyMeters, 1)}
                pathOptions={
                  isStale
                    ? {
                        color: STALE_COLOR,
                        weight: 2,
                        dashArray: '6 6',
                        fillColor: STALE_COLOR,
                        fillOpacity: 0.08,
                      }
                    : { color: LIVE_COLOR, weight: 2, fillColor: LIVE_COLOR, fillOpacity: 0.12 }
                }
              />
              <Marker
                position={[point.latitude, point.longitude]}
                icon={isStale ? STALE_PIN : LIVE_PIN}
                title={device?.childName}
              >
                <Popup>
                  <div className="map-popup">
                    <p className="map-popup__title">{device?.childName ?? 'Last known location'}</p>
                    <dl className="map-popup__grid">
                      <dt>Coordinates</dt>
                      <dd>{formatCoords(point.latitude, point.longitude)}</dd>
                      <dt>Accuracy</dt>
                      <dd>{formatAccuracy(point.accuracyMeters)}</dd>
                      <dt>Provider</dt>
                      <dd>{PROVIDER_LABELS[point.provider]}</dd>
                      <dt>Battery</dt>
                      <dd>
                        {formatBattery(point.batteryPercent)}
                        {point.isCharging === true ? ' (charging)' : ''}
                      </dd>
                      <dt>Time</dt>
                      <dd>
                        <time dateTime={point.recordedAt} title={formatAbsolute(point.recordedAt)}>
                          {formatRelative(point.recordedAt, now)}
                        </time>
                      </dd>
                    </dl>
                  </div>
                </Popup>
              </Marker>
            </>
          ) : null}
        </MapContainer>

        {point === null ? (
          <div className="map-pane__overlay">
            {device === null ? (
              <strong>Select a device to see where it is.</strong>
            ) : isSnapshotLoading ? (
              <Spinner label="Loading location..." />
            ) : (
              <>
                <strong>No location yet</strong>
                <span>
                  {device.childName}&apos;s device has not reported a fix. It appears here as soon
                  as the child app starts sharing.
                </span>
              </>
            )}
          </div>
        ) : null}
      </div>
    </section>
  );
}

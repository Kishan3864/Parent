package com.parentaltrack.child.service

import android.annotation.SuppressLint
import android.app.ForegroundServiceStartNotAllowedException
import android.app.Service
import android.content.Intent
import android.content.pm.ServiceInfo
import android.location.Location
import android.os.Build
import android.os.IBinder
import android.util.Log
import androidx.core.app.ServiceCompat
import com.parentaltrack.child.data.local.PendingLocationEntity
import com.parentaltrack.child.di.ServiceLocator
import com.parentaltrack.child.location.BatteryReader
import com.parentaltrack.child.location.LocationCollector
import com.parentaltrack.child.location.LocationStartResult
import com.parentaltrack.child.location.ProviderMapper
import com.parentaltrack.child.work.UploadScheduler
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.launch
import java.util.UUID

/**
 * Foreground service that collects fixes and queues them for upload (contract §5.3).
 * It only ever runs while its notification is visible.
 */
class LocationTrackingService : Service() {

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val collector by lazy { LocationCollector(applicationContext) }
    private val batteryReader by lazy { BatteryReader(applicationContext) }
    private var collecting = false

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        // The system gives a started foreground service about 5 s to post its notification and
        // kills it otherwise, so this must precede every other piece of work — the stop path
        // included, since that delivery also counts as a start.
        if (!promoteToForeground()) {
            shutdown()
            return START_NOT_STICKY
        }

        return when (intent?.action) {
            ACTION_STOP -> {
                ServiceLocator.trackingPrefs.trackingEnabled = false
                shutdown()
                START_NOT_STICKY
            }
            // A null intent means the system re-created us after START_STICKY.
            else -> if (beginCollecting()) {
                START_STICKY
            } else {
                shutdown()
                START_NOT_STICKY
            }
        }
    }

    override fun onDestroy() {
        stopCollecting()
        scope.cancel()
        super.onDestroy()
    }

    @SuppressLint("InlinedApi") // FOREGROUND_SERVICE_TYPE_LOCATION is a constant; ServiceCompat drops it below 29.
    private fun promoteToForeground(): Boolean =
        try {
            ServiceCompat.startForeground(
                this,
                NOTIF_ID,
                TrackingNotification.build(this),
                ServiceInfo.FOREGROUND_SERVICE_TYPE_LOCATION,
            )
            true
        } catch (e: Exception) {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S &&
                e is ForegroundServiceStartNotAllowedException
            ) {
                recordFailure(
                    "Android would not let location sharing start from the background. " +
                        "Open ParentalTrack and start sharing again.",
                    e,
                )
            } else {
                recordFailure(
                    "Location sharing could not be started: ${e.message ?: e.javaClass.simpleName}",
                    e,
                )
            }
            false
        }

    private fun beginCollecting(): Boolean {
        if (collecting) return true

        val prefs = ServiceLocator.trackingPrefs
        if (!prefs.trackingEnabled) {
            Log.i(TAG, "Location sharing is switched off; not requesting updates")
            return false
        }

        val result = collector.start(
            intervalMs = prefs.intervalSeconds * 1_000L,
            fastestMs = prefs.fastestIntervalSeconds * 1_000L,
            minDistanceM = prefs.minDistanceMeters.toFloat(),
            onFix = ::onFix,
        )
        return when (result) {
            LocationStartResult.Started -> {
                collecting = true
                prefs.lastServiceError = null
                true
            }
            LocationStartResult.PermissionDenied -> {
                recordFailure("Location permission is not granted, so nothing can be shared.", null)
                false
            }
            is LocationStartResult.Failed -> {
                recordFailure("Location updates could not be started: ${result.message}", null)
                false
            }
        }
    }

    private fun onFix(location: Location) {
        val battery = batteryReader.read()
        val row = PendingLocationEntity(
            id = 0L,
            clientId = UUID.randomUUID().toString(),
            latitude = location.latitude,
            longitude = location.longitude,
            // The column is not nullable; a fix without an accuracy estimate is reported as 0.
            accuracyMeters = if (location.hasAccuracy()) location.accuracy.toDouble() else 0.0,
            altitudeMeters = if (location.hasAltitude()) location.altitude else null,
            speedMps = if (location.hasSpeed()) location.speed.toDouble() else null,
            bearingDeg = if (location.hasBearing()) location.bearing.toDouble() else null,
            batteryPercent = battery.percent,
            isCharging = battery.isCharging,
            provider = ProviderMapper.fromLocation(location),
            recordedAtEpochMillis = if (location.time > 0L) location.time else System.currentTimeMillis(),
            attemptCount = 0,
        )

        scope.launch {
            try {
                ServiceLocator.locationRepository.enqueue(row)
                ServiceLocator.trackingPrefs.lastFixAtMillis = System.currentTimeMillis()
                UploadScheduler.requestUpload(applicationContext)
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                Log.e(TAG, "Could not queue the location fix for upload", e)
            }
        }
    }

    private fun stopCollecting() {
        if (!collecting) return
        collector.stop()
        collecting = false
    }

    private fun shutdown() {
        stopCollecting()
        ServiceCompat.stopForeground(this, ServiceCompat.STOP_FOREGROUND_REMOVE)
        stopSelf()
    }

    private fun recordFailure(message: String, cause: Throwable?) {
        if (cause == null) Log.w(TAG, message) else Log.w(TAG, message, cause)
        ServiceLocator.trackingPrefs.lastServiceError = message
    }

    companion object {
        const val ACTION_START = "com.parentaltrack.child.action.START_TRACKING"
        const val ACTION_STOP = "com.parentaltrack.child.action.STOP_TRACKING"
        const val NOTIF_ID = 1001

        private const val TAG = "TrackingService"
    }
}

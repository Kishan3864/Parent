package com.parentaltrack.child.service

import android.content.Context
import android.content.Intent
import android.util.Log
import androidx.core.content.ContextCompat
import com.parentaltrack.child.di.ServiceLocator
import com.parentaltrack.child.location.LocationCollector
import com.parentaltrack.child.work.UploadScheduler

/** Why location sharing can or cannot be started right now. */
sealed interface TrackingStartCheck {
    data object Ready : TrackingStartCheck
    data object ConsentMissing : TrackingStartCheck
    data object NotPaired : TrackingStartCheck
    data object LocationPermissionMissing : TrackingStartCheck
    data class ServiceStartFailed(val message: String) : TrackingStartCheck
}

/**
 * The single entry point the UI (and [BootReceiver]) uses to turn location sharing on and off.
 * Nothing else should start or stop [LocationTrackingService] directly.
 */
object TrackingController {

    private const val TAG = "TrackingController"

    /**
     * Checked in consent → pairing → permission order so the UI can point at the first gap.
     *
     * POST_NOTIFICATIONS is deliberately NOT a gate: a foreground service starts without it, only
     * its notification is not displayed. Blocking here would contradict contract §5.2 ("degrade
     * gracefully"), the "Skip for now" button on the notification stage of PermissionScreen and the
     * app's own warning copy. The missing notification is surfaced as a banner on StatusScreen.
     */
    fun canStart(context: Context): TrackingStartCheck {
        val prefs = ServiceLocator.trackingPrefs
        if ((prefs.consentAcceptedAt ?: 0L) <= 0L) return TrackingStartCheck.ConsentMissing
        if (ServiceLocator.securePrefs.deviceToken.isNullOrBlank()) return TrackingStartCheck.NotPaired
        if (!LocationCollector.hasForegroundLocationPermission(context)) {
            return TrackingStartCheck.LocationPermissionMissing
        }
        return TrackingStartCheck.Ready
    }

    fun start(context: Context): TrackingStartCheck {
        val check = canStart(context)
        if (check != TrackingStartCheck.Ready) {
            Log.i(TAG, "Not starting location sharing: $check")
            return check
        }

        val prefs = ServiceLocator.trackingPrefs
        prefs.trackingEnabled = true

        val intent = Intent(context, LocationTrackingService::class.java)
            .setAction(LocationTrackingService.ACTION_START)
        try {
            ContextCompat.startForegroundService(context, intent)
        } catch (e: Exception) {
            // Android 12+ refuses background foreground-service starts; the switch must not
            // stay on when nothing is actually running.
            prefs.trackingEnabled = false
            val message = e.message ?: e.javaClass.simpleName
            prefs.lastServiceError = message
            Log.e(TAG, "Starting the tracking service failed", e)
            return TrackingStartCheck.ServiceStartFailed(message)
        }

        UploadScheduler.schedulePeriodic(context)
        return TrackingStartCheck.Ready
    }

    fun stop(context: Context) {
        ServiceLocator.trackingPrefs.trackingEnabled = false
        val intent = Intent(context, LocationTrackingService::class.java)
            .setAction(LocationTrackingService.ACTION_STOP)
        try {
            context.startService(intent)
        } catch (e: IllegalStateException) {
            // Nothing was running, so there is nothing to stop; the flag above is what matters.
            // The periodic upload work is left in place so queued fixes still drain.
            Log.i(TAG, "Tracking service was not running", e)
        }
    }
}

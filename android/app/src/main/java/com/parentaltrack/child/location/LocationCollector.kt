package com.parentaltrack.child.location

import android.Manifest
import android.annotation.SuppressLint
import android.content.Context
import android.content.pm.PackageManager
import android.location.Location
import android.os.Looper
import android.util.Log
import androidx.core.content.ContextCompat
import com.google.android.gms.location.FusedLocationProviderClient
import com.google.android.gms.location.Granularity
import com.google.android.gms.location.LocationAvailability
import com.google.android.gms.location.LocationCallback
import com.google.android.gms.location.LocationRequest
import com.google.android.gms.location.LocationResult
import com.google.android.gms.location.LocationServices
import com.google.android.gms.location.Priority

/** Outcome of [LocationCollector.start] — the caller surfaces this instead of crashing. */
sealed interface LocationStartResult {
    data object Started : LocationStartResult
    data object PermissionDenied : LocationStartResult
    data class Failed(val message: String) : LocationStartResult
}

/**
 * Thin wrapper over [FusedLocationProviderClient]. Every entry point checks the runtime
 * permission first and still catches [SecurityException], because the permission can be
 * revoked between the check and the call.
 */
class LocationCollector(context: Context) {

    private val appContext = context.applicationContext

    private val client: FusedLocationProviderClient by lazy {
        LocationServices.getFusedLocationProviderClient(appContext)
    }

    @Volatile
    private var fixListener: ((Location) -> Unit)? = null

    private val callback = object : LocationCallback() {
        override fun onLocationResult(result: LocationResult) {
            val listener = fixListener ?: return
            // The provider may batch several fixes into one delivery; keep them all.
            for (location in result.locations) {
                listener(location)
            }
        }

        override fun onLocationAvailability(availability: LocationAvailability) {
            if (!availability.isLocationAvailable) {
                Log.i(TAG, "Fused provider reports location temporarily unavailable")
            }
        }
    }

    @SuppressLint("MissingPermission")
    fun start(
        intervalMs: Long,
        fastestMs: Long,
        minDistanceM: Float,
        onFix: (Location) -> Unit,
    ): LocationStartResult {
        if (!hasForegroundLocationPermission(appContext)) {
            return LocationStartResult.PermissionDenied
        }

        // The builder rejects a fastest interval above the interval, and server-supplied
        // values are only as trustworthy as the prefs they came from.
        val interval = intervalMs.coerceAtLeast(MIN_INTERVAL_MS)
        val fastest = fastestMs.coerceIn(0L, interval)

        val request = LocationRequest.Builder(Priority.PRIORITY_HIGH_ACCURACY, interval)
            .setMinUpdateIntervalMillis(fastest)
            .setMinUpdateDistanceMeters(minDistanceM.coerceAtLeast(0f))
            .setWaitForAccurateLocation(false)
            .setGranularity(Granularity.GRANULARITY_FINE)
            .build()

        fixListener = onFix
        return try {
            client.requestLocationUpdates(request, callback, Looper.getMainLooper())
            LocationStartResult.Started
        } catch (e: SecurityException) {
            fixListener = null
            Log.w(TAG, "Location permission revoked while starting updates", e)
            LocationStartResult.PermissionDenied
        } catch (e: IllegalStateException) {
            fixListener = null
            Log.e(TAG, "Fused location provider rejected the update request", e)
            LocationStartResult.Failed(e.message ?: e.javaClass.simpleName)
        }
    }

    fun stop() {
        fixListener = null
        try {
            client.removeLocationUpdates(callback)
        } catch (e: IllegalStateException) {
            Log.w(TAG, "Could not remove location updates", e)
        }
    }

    /**
     * Best-effort cached fix so the UI and the service can show something before the first
     * live update arrives. [onResult] is always called, with null when nothing is available.
     */
    @SuppressLint("MissingPermission")
    fun getLastKnownLocation(onResult: (Location?) -> Unit) {
        if (!hasForegroundLocationPermission(appContext)) {
            onResult(null)
            return
        }
        try {
            client.lastLocation
                .addOnSuccessListener { location -> onResult(location) }
                .addOnFailureListener { error ->
                    Log.w(TAG, "Last known location unavailable", error)
                    onResult(null)
                }
        } catch (e: SecurityException) {
            Log.w(TAG, "Location permission revoked while reading the last known location", e)
            onResult(null)
        }
    }

    companion object {
        private const val TAG = "LocationCollector"
        private const val MIN_INTERVAL_MS = 1_000L

        /** Coarse alone still yields usable fixes, so it is enough to run the service. */
        fun hasForegroundLocationPermission(context: Context): Boolean =
            isGranted(context, Manifest.permission.ACCESS_FINE_LOCATION) ||
                isGranted(context, Manifest.permission.ACCESS_COARSE_LOCATION)

        private fun isGranted(context: Context, permission: String): Boolean =
            ContextCompat.checkSelfPermission(context, permission) == PackageManager.PERMISSION_GRANTED
    }
}

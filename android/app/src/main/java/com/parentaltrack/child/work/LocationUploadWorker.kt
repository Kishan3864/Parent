package com.parentaltrack.child.work

import android.Manifest
import android.app.PendingIntent
import android.content.Context
import android.content.pm.PackageManager
import android.os.Build
import android.util.Log
import androidx.core.app.NotificationChannelCompat
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import androidx.core.content.ContextCompat
import androidx.work.CoroutineWorker
import androidx.work.WorkerParameters
import com.parentaltrack.child.R
import com.parentaltrack.child.data.local.PendingLocationEntity
import com.parentaltrack.child.data.remote.ApiClient
import com.parentaltrack.child.data.remote.ProblemDetails
import com.parentaltrack.child.data.repo.LocationRepository
import com.parentaltrack.child.di.ServiceLocator
import com.parentaltrack.child.service.TrackingController
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.serialization.SerializationException
import retrofit2.Response
import java.io.IOException
import java.net.HttpURLConnection

/**
 * Drains the offline queue into POST /api/v1/ingest/locations (contract 5.4).
 *
 * Outcomes:
 * * nothing left to send -> Result.success
 * * 5xx or an IO failure -> Result.retry (WorkManager backs off exponentially from 30 s)
 * * 401 -> the pairing was revoked: token cleared, tracking stopped, queue emptied, user notified,
 *   and Result.success because retrying can never succeed.
 */
class LocationUploadWorker(
    appContext: Context,
    params: WorkerParameters,
) : CoroutineWorker(appContext, params) {

    private enum class UploadOutcome { Accepted, Retry, Unauthorized }

    override suspend fun doWork(): Result = withContext(Dispatchers.IO) {
        val context = applicationContext
        // ChildApp.onCreate has already done this; repeated here because a Worker must never
        // depend on the order in which the process was brought up. The call is idempotent.
        ServiceLocator.init(context)

        val securePrefs = ServiceLocator.securePrefs
        val trackingPrefs = ServiceLocator.trackingPrefs
        val locationRepository = ServiceLocator.locationRepository

        if (securePrefs.deviceToken.isNullOrBlank()) {
            // Not paired yet, or the token was cleared after a revocation: nothing is uploadable.
            return@withContext Result.success()
        }

        val batchSize = trackingPrefs.batchMaxSize
        var batchesLeft = MAX_BATCHES_PER_RUN

        while (batchesLeft > 0) {
            batchesLeft--
            val batch = locationRepository.nextBatch(batchSize)
            if (batch.isEmpty()) {
                return@withContext Result.success()
            }
            when (upload(locationRepository, batch)) {
                UploadOutcome.Accepted -> Unit // keep draining
                UploadOutcome.Retry -> return@withContext Result.retry()
                UploadOutcome.Unauthorized -> {
                    ServiceLocator.enrollmentRepository.revokeLocally()
                    // The service does not observe trackingEnabled, so it has to be stopped
                    // explicitly: otherwise it keeps collecting fixes and keeps showing the
                    // ongoing "sharing is on" notification after the parent revoked this device
                    // (contract §5.4). stop() tolerates the service not running.
                    TrackingController.stop(context)
                    // Queued fixes can never be delivered with a revoked token, and keeping
                    // location data on the device after sharing ended serves nobody.
                    locationRepository.clear()
                    notifyRevoked(context)
                    return@withContext Result.success()
                }
            }
        }

        // Hit the per-run batch cap: the rest is picked up by the next fix or the periodic run.
        Result.success()
    }

    private suspend fun upload(
        locationRepository: LocationRepository,
        batch: List<PendingLocationEntity>,
    ): UploadOutcome {
        val response: Response<*> = try {
            locationRepository.upload(batch)
        } catch (e: IOException) {
            Log.i(TAG, "Upload postponed: " + e.javaClass.simpleName)
            return UploadOutcome.Retry
        } catch (e: SerializationException) {
            // The rows stay queued and the server de-duplicates on clientId, so a resend is safe.
            Log.w(TAG, "Unreadable ingest response", e)
            return UploadOutcome.Retry
        }

        val code = response.code()
        if (response.isSuccessful) {
            locationRepository.deleteUploaded(batch.map { it.id })
            ServiceLocator.trackingPrefs.lastUploadAtMillis = System.currentTimeMillis()
            return UploadOutcome.Accepted
        }

        if (code == HttpURLConnection.HTTP_UNAUTHORIZED) {
            return UploadOutcome.Unauthorized
        }

        if (code >= HttpURLConnection.HTTP_INTERNAL_ERROR) {
            Log.w(TAG, "Server error $code, will retry")
            return UploadOutcome.Retry
        }

        // 4xx other than 401: the request itself is the problem, so resending it unchanged cannot
        // help. Rows the server named are dropped immediately; the rest carry an attempt count and
        // are dropped once they have been refused too often.
        val body = response.errorBodyText()
        val named = batch.filter { body != null && body.contains(it.clientId, ignoreCase = true) }
        if (named.isNotEmpty()) {
            locationRepository.deleteUploaded(named.map { it.id })
        }
        val namedIds = named.map { it.id }.toSet()
        val dropped = locationRepository.markFailedAttempt(
            batch.filterNot { it.id in namedIds }.map { it.id }
        )
        Log.w(
            TAG,
            "Ingest rejected with $code (${problemTitle(body) ?: "no detail"}): " +
                "${named.size} named row(s) deleted, $dropped exhausted row(s) dropped",
        )
        return UploadOutcome.Retry
    }

    private fun Response<*>.errorBodyText(): String? = try {
        errorBody()?.string()
    } catch (e: IOException) {
        Log.w(TAG, "Could not read the error body", e)
        null
    }

    private fun problemTitle(body: String?): String? {
        if (body.isNullOrBlank()) return null
        return try {
            ApiClient.json.decodeFromString(ProblemDetails.serializer(), body).title
        } catch (e: SerializationException) {
            Log.w(TAG, "Error body was not problem+json", e)
            null
        }
    }

    /**
     * Tells the user that sharing stopped. Built here on purpose: the work package must not depend
     * on the foreground-service notification, and this alert belongs on its own channel
     * ("tracking_alerts") rather than the ongoing "location_tracking" one.
     */
    private fun notifyRevoked(context: Context) {
        val manager = NotificationManagerCompat.from(context)
        manager.createNotificationChannel(
            NotificationChannelCompat.Builder(
                ALERTS_CHANNEL_ID,
                NotificationManagerCompat.IMPORTANCE_HIGH,
            )
                .setName(context.getString(R.string.notification_alert_channel_name))
                .setDescription(context.getString(R.string.notification_alert_channel_description))
                .build()
        )

        val title = context.getString(R.string.notification_revoked_title)
        val message = context.getString(R.string.notification_revoked_text)

        val launchIntent = context.packageManager.getLaunchIntentForPackage(context.packageName)
        val contentIntent = launchIntent?.let {
            PendingIntent.getActivity(
                context,
                0,
                it,
                PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
            )
        }

        val builder = NotificationCompat.Builder(context, ALERTS_CHANNEL_ID)
            .setSmallIcon(R.drawable.ic_stat_location)
            .setContentTitle(title)
            .setContentText(message)
            .setStyle(NotificationCompat.BigTextStyle().bigText(message))
            .setPriority(NotificationCompat.PRIORITY_HIGH)
            .setCategory(NotificationCompat.CATEGORY_STATUS)
            .setAutoCancel(true)
        if (contentIntent != null) {
            builder.setContentIntent(contentIntent)
        }

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU &&
            ContextCompat.checkSelfPermission(context, Manifest.permission.POST_NOTIFICATIONS) !=
            PackageManager.PERMISSION_GRANTED
        ) {
            Log.i(TAG, "Revocation notification suppressed: POST_NOTIFICATIONS is not granted")
            return
        }
        try {
            manager.notify(REVOKED_NOTIFICATION_ID, builder.build())
        } catch (e: SecurityException) {
            Log.w(TAG, "Could not post the revocation notification", e)
        }
    }

    private companion object {
        const val TAG = "LocationUploadWorker"

        /** Deliberately separate from the foreground-service channel "location_tracking". */
        const val ALERTS_CHANNEL_ID = "tracking_alerts"
        const val REVOKED_NOTIFICATION_ID = 4201

        /** Bounds one run so a huge backlog cannot hold a worker for minutes on end. */
        const val MAX_BATCHES_PER_RUN = 20
    }
}

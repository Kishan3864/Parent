package com.parentaltrack.child.work

import android.content.Context
import androidx.work.BackoffPolicy
import androidx.work.Constraints
import androidx.work.ExistingPeriodicWorkPolicy
import androidx.work.ExistingWorkPolicy
import androidx.work.NetworkType
import androidx.work.OneTimeWorkRequest
import androidx.work.PeriodicWorkRequest
import androidx.work.WorkManager
import java.util.concurrent.TimeUnit

/** Schedules the two flavours of upload work described in contract 5.4. */
object UploadScheduler {

    const val PERIODIC_WORK_NAME = "location-upload-periodic"
    const val ONE_SHOT_WORK_NAME = "location-upload-now"
    const val WORK_TAG = "location-upload"

    private const val PERIODIC_INTERVAL_MINUTES = 15L
    private const val BACKOFF_DELAY_SECONDS = 30L

    /** Safety net that drains whatever the one-shot runs could not deliver. */
    fun schedulePeriodic(context: Context) {
        val request = PeriodicWorkRequest.Builder(
            LocationUploadWorker::class.java,
            PERIODIC_INTERVAL_MINUTES,
            TimeUnit.MINUTES,
        )
            .setConstraints(constraints())
            .setBackoffCriteria(BackoffPolicy.EXPONENTIAL, BACKOFF_DELAY_SECONDS, TimeUnit.SECONDS)
            .addTag(WORK_TAG)
            .build()

        WorkManager.getInstance(context.applicationContext)
            .enqueueUniquePeriodicWork(PERIODIC_WORK_NAME, ExistingPeriodicWorkPolicy.KEEP, request)
    }

    /** Called after every fix; KEEP means a run already queued or running is left alone. */
    fun requestUpload(context: Context) {
        val request = OneTimeWorkRequest.Builder(LocationUploadWorker::class.java)
            .setConstraints(constraints())
            .setBackoffCriteria(BackoffPolicy.EXPONENTIAL, BACKOFF_DELAY_SECONDS, TimeUnit.SECONDS)
            .addTag(WORK_TAG)
            .build()

        WorkManager.getInstance(context.applicationContext)
            .enqueueUniqueWork(ONE_SHOT_WORK_NAME, ExistingWorkPolicy.KEEP, request)
    }

    fun cancelAll(context: Context) {
        val workManager = WorkManager.getInstance(context.applicationContext)
        workManager.cancelUniqueWork(PERIODIC_WORK_NAME)
        workManager.cancelUniqueWork(ONE_SHOT_WORK_NAME)
    }

    private fun constraints(): Constraints = Constraints.Builder()
        .setRequiredNetworkType(NetworkType.CONNECTED)
        .build()
}

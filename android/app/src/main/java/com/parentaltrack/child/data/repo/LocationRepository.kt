package com.parentaltrack.child.data.repo

import com.parentaltrack.child.data.local.PendingLocationDao
import com.parentaltrack.child.data.local.PendingLocationEntity
import com.parentaltrack.child.data.prefs.TrackingPrefs
import com.parentaltrack.child.data.remote.IngestPointDto
import com.parentaltrack.child.data.remote.IngestRequest
import com.parentaltrack.child.data.remote.IngestResponse
import com.parentaltrack.child.data.remote.TrackingApi
import kotlinx.coroutines.flow.Flow
import retrofit2.Response
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import java.util.TimeZone
import java.util.UUID

/**
 * Owns the offline upload queue: fixes go in here and stay until the server accepts them.
 * The HTTP round trip lives here too; [com.parentaltrack.child.work.LocationUploadWorker] decides
 * what to do with the answer.
 */
class LocationRepository(
    private val dao: PendingLocationDao,
    private val api: TrackingApi,
    private val trackingPrefs: TrackingPrefs,
) {

    /** Live pending-row count for the status screen. */
    val pendingCount: Flow<Int> = dao.countFlow()

    /**
     * Queues one fix and enforces the queue cap. The row id and the idempotency key are always
     * assigned here, so no caller can accidentally queue two fixes under the same `clientId`.
     * Returns the new row id.
     */
    suspend fun enqueue(fix: PendingLocationEntity): Long {
        val row = fix.copy(
            id = 0L,
            clientId = UUID.randomUUID().toString(),
            attemptCount = 0,
        )
        val id = dao.insert(row)
        trackingPrefs.lastFixAtMillis = row.recordedAtEpochMillis
        // Cheap guard: only walk the delete path once the queue is actually over the cap.
        if (dao.count() > MAX_QUEUE_ROWS) {
            dao.deleteOldestBeyond(MAX_QUEUE_ROWS)
        }
        return id
    }

    /** Oldest rows first, capped at what the server accepts in one request. */
    suspend fun nextBatch(limit: Int): List<PendingLocationEntity> =
        dao.oldestBatch(limit.coerceIn(1, MAX_SERVER_BATCH))

    /**
     * POSTs one batch to `/api/v1/ingest/locations`.
     * Throws [java.io.IOException] when the request never reached the server.
     */
    suspend fun upload(batch: List<PendingLocationEntity>): Response<IngestResponse> {
        // SimpleDateFormat is not thread safe, so it is built per call rather than shared.
        val format = SimpleDateFormat(ISO_8601_UTC_MILLIS, Locale.US)
        format.timeZone = TimeZone.getTimeZone("UTC")
        val points = batch.map { row ->
            IngestPointDto(
                clientId = row.clientId,
                latitude = row.latitude,
                longitude = row.longitude,
                accuracyMeters = row.accuracyMeters,
                altitudeMeters = row.altitudeMeters,
                speedMetersPerSecond = row.speedMps,
                bearingDegrees = row.bearingDeg,
                batteryPercent = row.batteryPercent,
                isCharging = row.isCharging,
                provider = row.provider,
                recordedAt = format.format(Date(row.recordedAtEpochMillis)),
            )
        }
        return api.ingestLocations(IngestRequest(points))
    }

    suspend fun deleteUploaded(ids: List<Long>): Int =
        if (ids.isEmpty()) 0 else dao.deleteByIds(ids)

    /**
     * Records a failed attempt and drops rows the server has refused [MAX_UPLOAD_ATTEMPTS] times,
     * so one poisonous point can never wedge the queue forever. Returns the number of rows dropped.
     */
    suspend fun markFailedAttempt(ids: List<Long>): Int {
        if (ids.isEmpty()) return 0
        dao.incrementAttempts(ids)
        return dao.deleteExhausted(MAX_UPLOAD_ATTEMPTS)
    }

    suspend fun clear(): Int = dao.deleteAll()

    companion object {
        /** Contract 5.4: queue capped at 10 000 rows, oldest dropped beyond that. */
        const val MAX_QUEUE_ROWS = 10_000

        /** Contract 2.4: `Ingestion:MaxBatchSize`. */
        const val MAX_SERVER_BATCH = 200

        /** How often a row may be refused with a 4xx before it is dropped. */
        const val MAX_UPLOAD_ATTEMPTS = 10

        /** ISO-8601 UTC with milliseconds and a trailing Z, as the API requires. */
        private const val ISO_8601_UTC_MILLIS = "yyyy-MM-dd'T'HH:mm:ss.SSS'Z'"
    }
}

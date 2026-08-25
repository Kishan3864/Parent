package com.parentaltrack.child.data.repo

import android.content.Context
import android.os.Build
import android.util.Log
import com.parentaltrack.child.BuildConfig
import com.parentaltrack.child.data.prefs.SecurePrefs
import com.parentaltrack.child.data.prefs.TrackingPrefs
import com.parentaltrack.child.data.remote.ApiClient
import com.parentaltrack.child.data.remote.DeviceSelfDto
import com.parentaltrack.child.data.remote.EnrollRequest
import com.parentaltrack.child.data.remote.EnrollResponse
import com.parentaltrack.child.data.remote.ProblemDetails
import com.parentaltrack.child.data.remote.TrackingApi
import com.parentaltrack.child.data.remote.TrackingConfigDto
import com.parentaltrack.child.work.UploadScheduler
import kotlinx.serialization.SerializationException
import retrofit2.Response
import java.io.IOException
import java.net.HttpURLConnection

/** A non-2xx answer from the API, carrying the RFC7807 body when the server sent one. */
class ApiException(
    message: String,
    val statusCode: Int,
    val problem: ProblemDetails? = null,
) : Exception(message)

/** Pairing, credential storage and unpairing. */
class EnrollmentRepository(
    private val api: TrackingApi,
    private val securePrefs: SecurePrefs,
    private val trackingPrefs: TrackingPrefs,
) {

    /**
     * Exchanges a pairing code for a device token and persists the credential plus the tracking
     * configuration the server chose. Failures come back as [ApiException] (server said no) or the
     * underlying [IOException] (never reached the server).
     */
    suspend fun enroll(pairingCode: String): Result<EnrollResponse> {
        val normalized = normalizePairingCode(pairingCode)
        if (normalized.length != PAIRING_CODE_LENGTH) {
            return Result.failure(
                IllegalArgumentException("A pairing code has $PAIRING_CODE_LENGTH characters")
            )
        }
        val request = EnrollRequest(
            pairingCode = normalized,
            installId = securePrefs.installId,
            manufacturer = Build.MANUFACTURER,
            model = Build.MODEL,
            osVersion = Build.VERSION.RELEASE,
            appVersion = BuildConfig.VERSION_NAME,
        )
        val response = try {
            api.enroll(request)
        } catch (e: IOException) {
            Log.w(TAG, "Enrollment failed: network error", e)
            return Result.failure(e)
        } catch (e: SerializationException) {
            Log.w(TAG, "Enrollment failed: unreadable response", e)
            return Result.failure(e)
        }

        val body = response.body()
        if (!response.isSuccessful || body == null) {
            return Result.failure(response.toApiException("Pairing failed"))
        }

        securePrefs.deviceToken = body.deviceToken
        securePrefs.deviceId = body.deviceId
        securePrefs.childName = body.childName
        // Kept in both stores on purpose: the credential store holds it next to the token, and the
        // status screen reads the non-secret copy without touching the keystore.
        trackingPrefs.childName = body.childName
        trackingPrefs.applyConfig(body.tracking)
        trackingPrefs.revoked = false
        return Result.success(body)
    }

    /**
     * Re-reads the device's own record and refreshes the tracking configuration, so a change made
     * server-side eventually reaches the device.
     */
    suspend fun refreshSelf(): Result<DeviceSelfDto> {
        val response = try {
            api.me()
        } catch (e: IOException) {
            return Result.failure(e)
        } catch (e: SerializationException) {
            return Result.failure(e)
        }

        if (response.code() == HttpURLConnection.HTTP_UNAUTHORIZED) {
            revokeLocally()
            return Result.failure(response.toApiException("This device is no longer paired"))
        }
        val body = response.body()
        if (!response.isSuccessful || body == null) {
            return Result.failure(response.toApiException("Could not read the device status"))
        }
        securePrefs.childName = body.childName
        trackingPrefs.childName = body.childName
        trackingPrefs.applyConfig(body.tracking)
        return Result.success(body)
    }

    /**
     * Reaction to a 401: the parent revoked this device (or deleted it). The token is useless, so
     * it is deleted and tracking is switched off; the UI reads `TrackingPrefs.revoked` to explain
     * what happened.
     */
    fun revokeLocally() {
        securePrefs.deviceToken = null
        trackingPrefs.trackingEnabled = false
        trackingPrefs.revoked = true
    }

    /**
     * "Unpair & delete token": cancels the upload work, deletes the credential and every stored
     * pairing detail, and empties the upload queue when the queue is passed in.
     */
    suspend fun unpair(context: Context, locationRepository: LocationRepository? = null) {
        UploadScheduler.cancelAll(context)
        securePrefs.clear()
        trackingPrefs.resetDeviceState()
        locationRepository?.clear()
    }

    private fun TrackingPrefs.applyConfig(config: TrackingConfigDto) = applyTrackingConfig(
        intervalSeconds = config.intervalSeconds,
        fastestIntervalSeconds = config.fastestIntervalSeconds,
        minDistanceMeters = config.minDistanceMeters,
        batchMaxSize = config.batchMaxSize,
        uploadIntervalSeconds = config.uploadIntervalSeconds,
    )

    private fun Response<*>.toApiException(fallbackMessage: String): ApiException {
        val problem = readProblem()
        val message = problem?.title?.takeIf { it.isNotBlank() }
            ?: problem?.detail?.takeIf { it.isNotBlank() }
            ?: "$fallbackMessage (HTTP ${code()})"
        return ApiException(message, code(), problem)
    }

    private fun Response<*>.readProblem(): ProblemDetails? {
        val raw = try {
            errorBody()?.string()
        } catch (e: IOException) {
            Log.w(TAG, "Could not read the error body", e)
            null
        }
        if (raw.isNullOrBlank()) return null
        return try {
            ApiClient.json.decodeFromString(ProblemDetails.serializer(), raw)
        } catch (e: SerializationException) {
            Log.w(TAG, "Error body was not problem+json", e)
            null
        }
    }

    companion object {
        private const val TAG = "EnrollmentRepo"
        private const val PAIRING_CODE_LENGTH = 8

        /** The server normalises too, but sending a clean code keeps the failure modes obvious. */
        fun normalizePairingCode(raw: String): String =
            raw.uppercase().filter { it.isLetterOrDigit() }
    }
}

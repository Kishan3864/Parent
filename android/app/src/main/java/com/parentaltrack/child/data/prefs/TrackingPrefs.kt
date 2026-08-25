package com.parentaltrack.child.data.prefs

import android.content.Context
import android.content.SharedPreferences
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

/** Immutable snapshot of everything in [TrackingPrefs], so the UI can render from one value. */
data class TrackingState(
    val consentAcceptedAt: Long? = null,
    val childName: String? = null,
    val trackingEnabled: Boolean = false,
    val revoked: Boolean = false,
    val lastFixAtMillis: Long = 0L,
    val lastUploadAtMillis: Long = 0L,
    val lastServiceError: String? = null,
    val intervalSeconds: Int = TrackingPrefs.DEFAULT_INTERVAL_SECONDS,
    val fastestIntervalSeconds: Int = TrackingPrefs.DEFAULT_FASTEST_INTERVAL_SECONDS,
    val minDistanceMeters: Int = TrackingPrefs.DEFAULT_MIN_DISTANCE_METERS,
    val batchMaxSize: Int = TrackingPrefs.DEFAULT_BATCH_MAX_SIZE,
    val uploadIntervalSeconds: Int = TrackingPrefs.DEFAULT_UPLOAD_INTERVAL_SECONDS,
) {
    val consentAccepted: Boolean get() = (consentAcceptedAt ?: 0L) > 0L
}

/**
 * Non-secret tracking state plus the tracking configuration the server handed out at enrollment.
 *
 * Written from the UI, the foreground service and the upload worker, so every mutation refreshes
 * [state] synchronously: a read straight after a write is consistent even though the
 * SharedPreferences change callback is delivered on the main thread.
 */
class TrackingPrefs(context: Context) {

    private val prefs: SharedPreferences =
        context.applicationContext.getSharedPreferences(FILE_NAME, Context.MODE_PRIVATE)

    private val _state = MutableStateFlow(read())

    /** Observable snapshot for the UI. */
    val state: StateFlow<TrackingState> = _state.asStateFlow()

    // Held in a field: SharedPreferences keeps only a weak reference to its listeners.
    private val changeListener = SharedPreferences.OnSharedPreferenceChangeListener { _, _ ->
        _state.value = read()
    }

    init {
        prefs.registerOnSharedPreferenceChangeListener(changeListener)
    }

    /** Epoch millis of the consent tap, or null while consent has not been given. */
    var consentAcceptedAt: Long?
        get() = prefs.getLong(KEY_CONSENT_ACCEPTED_AT, 0L).takeIf { it > 0L }
        set(value) = edit {
            if (value == null) remove(KEY_CONSENT_ACCEPTED_AT) else putLong(KEY_CONSENT_ACCEPTED_AT, value)
        }

    val consentAccepted: Boolean
        get() = consentAcceptedAt != null

    /** Records the consent tap. Re-accepting keeps the first timestamp: consent is not renewed. */
    fun acceptConsent() {
        if (consentAcceptedAt == null) {
            consentAcceptedAt = System.currentTimeMillis()
        }
    }

    /** Child name as shown on the status screen; the same value is kept with the credential. */
    var childName: String?
        get() = prefs.getString(KEY_CHILD_NAME, null)
        set(value) = edit {
            if (value == null) remove(KEY_CHILD_NAME) else putString(KEY_CHILD_NAME, value)
        }

    var trackingEnabled: Boolean
        get() = prefs.getBoolean(KEY_TRACKING_ENABLED, false)
        set(value) = edit { putBoolean(KEY_TRACKING_ENABLED, value) }

    /** True once the server answered 401: the parent revoked this device. */
    var revoked: Boolean
        get() = prefs.getBoolean(KEY_REVOKED, false)
        set(value) = edit { putBoolean(KEY_REVOKED, value) }

    /** Last message from a failed service start, or null when the service is healthy. */
    var lastServiceError: String?
        get() = prefs.getString(KEY_LAST_SERVICE_ERROR, null)
        set(value) = edit {
            if (value == null) remove(KEY_LAST_SERVICE_ERROR) else putString(KEY_LAST_SERVICE_ERROR, value)
        }

    /** Epoch millis of the newest queued fix; 0 when none has been taken yet. */
    var lastFixAtMillis: Long
        get() = prefs.getLong(KEY_LAST_FIX_AT, 0L)
        set(value) = edit { putLong(KEY_LAST_FIX_AT, value) }

    /** Epoch millis of the last batch the server accepted; 0 when nothing has been uploaded. */
    var lastUploadAtMillis: Long
        get() = prefs.getLong(KEY_LAST_UPLOAD_AT, 0L)
        set(value) = edit { putLong(KEY_LAST_UPLOAD_AT, value) }

    /** Null-instead-of-zero view of [lastFixAtMillis]. */
    val lastFixAt: Long?
        get() = lastFixAtMillis.takeIf { it > 0L }

    /** Null-instead-of-zero view of [lastUploadAtMillis]. */
    val lastUploadAt: Long?
        get() = lastUploadAtMillis.takeIf { it > 0L }

    val intervalSeconds: Int
        get() = prefs.getInt(KEY_INTERVAL_SECONDS, DEFAULT_INTERVAL_SECONDS)

    val fastestIntervalSeconds: Int
        get() = prefs.getInt(KEY_FASTEST_INTERVAL_SECONDS, DEFAULT_FASTEST_INTERVAL_SECONDS)

    val minDistanceMeters: Int
        get() = prefs.getInt(KEY_MIN_DISTANCE_METERS, DEFAULT_MIN_DISTANCE_METERS)

    val batchMaxSize: Int
        get() = prefs.getInt(KEY_BATCH_MAX_SIZE, DEFAULT_BATCH_MAX_SIZE)

    val uploadIntervalSeconds: Int
        get() = prefs.getInt(KEY_UPLOAD_INTERVAL_SECONDS, DEFAULT_UPLOAD_INTERVAL_SECONDS)

    /** Stores the `tracking` block returned by enroll and `GET /api/v1/devices/me`. */
    fun applyTrackingConfig(
        intervalSeconds: Int,
        fastestIntervalSeconds: Int,
        minDistanceMeters: Int,
        batchMaxSize: Int,
        uploadIntervalSeconds: Int,
    ) = edit {
        putInt(KEY_INTERVAL_SECONDS, intervalSeconds)
        putInt(KEY_FASTEST_INTERVAL_SECONDS, fastestIntervalSeconds)
        putInt(KEY_MIN_DISTANCE_METERS, minDistanceMeters)
        putInt(KEY_BATCH_MAX_SIZE, batchMaxSize)
        putInt(KEY_UPLOAD_INTERVAL_SECONDS, uploadIntervalSeconds)
    }

    /**
     * Clears everything tied to a pairing. Consent is deliberately kept: it is a statement the user
     * made about this device, and re-pairing should not force them through the consent screen again.
     */
    fun resetDeviceState() = edit {
        remove(KEY_CHILD_NAME)
        remove(KEY_TRACKING_ENABLED)
        remove(KEY_REVOKED)
        remove(KEY_LAST_SERVICE_ERROR)
        remove(KEY_LAST_FIX_AT)
        remove(KEY_LAST_UPLOAD_AT)
        remove(KEY_INTERVAL_SECONDS)
        remove(KEY_FASTEST_INTERVAL_SECONDS)
        remove(KEY_MIN_DISTANCE_METERS)
        remove(KEY_BATCH_MAX_SIZE)
        remove(KEY_UPLOAD_INTERVAL_SECONDS)
    }

    fun snapshot(): TrackingState = _state.value

    private inline fun edit(block: SharedPreferences.Editor.() -> Unit) {
        val editor = prefs.edit()
        editor.block()
        editor.apply()
        _state.value = read()
    }

    private fun read(): TrackingState = TrackingState(
        consentAcceptedAt = prefs.getLong(KEY_CONSENT_ACCEPTED_AT, 0L).takeIf { it > 0L },
        childName = prefs.getString(KEY_CHILD_NAME, null),
        trackingEnabled = prefs.getBoolean(KEY_TRACKING_ENABLED, false),
        revoked = prefs.getBoolean(KEY_REVOKED, false),
        lastFixAtMillis = prefs.getLong(KEY_LAST_FIX_AT, 0L),
        lastUploadAtMillis = prefs.getLong(KEY_LAST_UPLOAD_AT, 0L),
        lastServiceError = prefs.getString(KEY_LAST_SERVICE_ERROR, null),
        intervalSeconds = prefs.getInt(KEY_INTERVAL_SECONDS, DEFAULT_INTERVAL_SECONDS),
        fastestIntervalSeconds = prefs.getInt(KEY_FASTEST_INTERVAL_SECONDS, DEFAULT_FASTEST_INTERVAL_SECONDS),
        minDistanceMeters = prefs.getInt(KEY_MIN_DISTANCE_METERS, DEFAULT_MIN_DISTANCE_METERS),
        batchMaxSize = prefs.getInt(KEY_BATCH_MAX_SIZE, DEFAULT_BATCH_MAX_SIZE),
        uploadIntervalSeconds = prefs.getInt(KEY_UPLOAD_INTERVAL_SECONDS, DEFAULT_UPLOAD_INTERVAL_SECONDS),
    )

    companion object {
        const val DEFAULT_INTERVAL_SECONDS = 60
        const val DEFAULT_FASTEST_INTERVAL_SECONDS = 30
        const val DEFAULT_MIN_DISTANCE_METERS = 25
        const val DEFAULT_BATCH_MAX_SIZE = 100
        const val DEFAULT_UPLOAD_INTERVAL_SECONDS = 120

        private const val FILE_NAME = "pt_tracking_prefs"

        private const val KEY_CONSENT_ACCEPTED_AT = "consentAcceptedAt"
        private const val KEY_CHILD_NAME = "childName"
        private const val KEY_TRACKING_ENABLED = "trackingEnabled"
        private const val KEY_REVOKED = "revoked"
        private const val KEY_LAST_SERVICE_ERROR = "lastServiceError"
        private const val KEY_LAST_FIX_AT = "lastFixAtMillis"
        private const val KEY_LAST_UPLOAD_AT = "lastUploadAtMillis"
        private const val KEY_INTERVAL_SECONDS = "intervalSeconds"
        private const val KEY_FASTEST_INTERVAL_SECONDS = "fastestIntervalSeconds"
        private const val KEY_MIN_DISTANCE_METERS = "minDistanceMeters"
        private const val KEY_BATCH_MAX_SIZE = "batchMaxSize"
        private const val KEY_UPLOAD_INTERVAL_SECONDS = "uploadIntervalSeconds"
    }
}

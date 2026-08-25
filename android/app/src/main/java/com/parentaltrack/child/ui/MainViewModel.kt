package com.parentaltrack.child.ui

import android.Manifest
import android.annotation.SuppressLint
import android.content.Context
import android.content.pm.PackageManager
import android.os.Build
import android.os.PowerManager
import android.util.Log
import androidx.core.content.ContextCompat
import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewModelScope
import androidx.lifecycle.viewmodel.initializer
import androidx.lifecycle.viewmodel.viewModelFactory
import com.parentaltrack.child.data.prefs.SecurePrefs
import com.parentaltrack.child.data.prefs.TrackingPrefs
import com.parentaltrack.child.data.repo.ApiException
import com.parentaltrack.child.data.repo.EnrollmentRepository
import com.parentaltrack.child.data.repo.LocationRepository
import com.parentaltrack.child.di.ServiceLocator
import com.parentaltrack.child.service.TrackingController
import com.parentaltrack.child.service.TrackingStartCheck
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.catch
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.IOException
import java.net.HttpURLConnection

private const val TAG = "MainViewModel"

/** Keeps relative timestamps ("4 minutes ago") and permission state honest while the app is open. */
private const val REFRESH_INTERVAL_MILLIS = 15_000L

/** A pairing code is 8 characters from an alphabet that excludes I, O, 0 and 1. */
const val PAIRING_CODE_LENGTH = 8

/** Snapshot of the runtime permissions the app asks for. */
data class PermissionState(
    val notificationsRequired: Boolean = false,
    val notificationsGranted: Boolean = true,
    val fineLocationGranted: Boolean = false,
    val coarseLocationGranted: Boolean = false,
    val backgroundLocationRequired: Boolean = false,
    val backgroundLocationGranted: Boolean = true,
    val batteryOptimisationIgnored: Boolean = false,
) {
    val foregroundLocationGranted: Boolean get() = fineLocationGranted || coarseLocationGranted

    /**
     * The minimum needed to start the foreground service at all. POST_NOTIFICATIONS is not part of
     * it: without it the ongoing notification is simply not shown, which the status screen warns
     * about — it never blocks sharing (contract §5.2 "degrade gracefully").
     */
    val canTrack: Boolean get() = foregroundLocationGranted
}

/** Why an enrollment attempt failed; the screen turns this into the matching string resource. */
enum class PairingError { INCOMPLETE, INVALID_CODE, NETWORK, SERVER, UNKNOWN }

/** Why sharing could not be switched on; the screen turns this into the matching string resource. */
enum class SharingBlocker {
    CONSENT_MISSING,
    NOT_PAIRED,
    LOCATION_PERMISSION,
    SERVICE_START_FAILED,
}

/** Transient state of the pairing form. */
data class PairingUiState(
    val isSubmitting: Boolean = false,
    val error: PairingError? = null,
    val succeeded: Boolean = false,
)

data class UiState(
    val isLoading: Boolean = true,
    val consentAccepted: Boolean = false,
    val isPaired: Boolean = false,
    val childName: String? = null,
    val trackingEnabled: Boolean = false,
    val revokedByParent: Boolean = false,
    val permissions: PermissionState = PermissionState(),
    val lastFixAtMillis: Long? = null,
    val lastUploadAtMillis: Long? = null,
    val pendingUploadCount: Int = 0,
    val nowMillis: Long = System.currentTimeMillis(),
    val pairing: PairingUiState = PairingUiState(),
    val startFailure: SharingBlocker? = null,
    /**
     * Last failure recorded by the service itself — a refused foreground start (API 31+), a denied
     * permission at request time, a provider that would not start. Unlike [startFailure] this also
     * covers starts the user never watched: START_STICKY re-delivery and BootReceiver
     * (contract §5.3).
     */
    val lastServiceError: String? = null,
)

/**
 * Single state holder for the whole app: the four screens are views over the same handful of
 * stored values, so one ViewModel keeps consent, pairing, permissions and tracking in sync.
 *
 * [TrackingPrefs.state] and the Room count are observed; the credential store and the runtime
 * permission grants cannot be observed, so they are re-read on a timer, on every ON_RESUME and
 * after every action. All reads happen off the main thread.
 */
class MainViewModel(
    private val appContext: Context,
    private val trackingPrefs: TrackingPrefs,
    private val securePrefs: SecurePrefs,
    private val enrollmentRepository: EnrollmentRepository,
    private val locationRepository: LocationRepository,
    pendingCount: Flow<Int>,
) : ViewModel() {

    private data class DeviceSnapshot(
        val loaded: Boolean = false,
        val isPaired: Boolean = false,
        val childName: String? = null,
        val permissions: PermissionState = PermissionState(),
        val nowMillis: Long = System.currentTimeMillis(),
    )

    private data class TransientState(
        val pairing: PairingUiState = PairingUiState(),
        val startFailure: SharingBlocker? = null,
    )

    private val deviceSnapshot = MutableStateFlow(DeviceSnapshot())
    private val transient = MutableStateFlow(TransientState())

    val state: StateFlow<UiState> = combine(
        trackingPrefs.state,
        pendingCount.catch { throwable ->
            Log.w(TAG, "Pending-location count unavailable", throwable)
            emit(0)
        },
        deviceSnapshot,
        transient,
    ) { tracking, pending, snapshot, extra ->
        UiState(
            isLoading = !snapshot.loaded,
            consentAccepted = tracking.consentAccepted,
            isPaired = snapshot.isPaired,
            childName = snapshot.childName,
            trackingEnabled = tracking.trackingEnabled,
            revokedByParent = tracking.revoked,
            permissions = snapshot.permissions,
            lastFixAtMillis = tracking.lastFixAtMillis.takeIf { it > 0L },
            lastUploadAtMillis = tracking.lastUploadAtMillis.takeIf { it > 0L },
            pendingUploadCount = pending,
            nowMillis = snapshot.nowMillis,
            pairing = extra.pairing,
            startFailure = extra.startFailure,
            lastServiceError = tracking.lastServiceError,
        )
    }.stateIn(viewModelScope, SharingStarted.Eagerly, UiState())

    init {
        viewModelScope.launch {
            while (isActive) {
                refreshNow()
                delay(REFRESH_INTERVAL_MILLIS)
            }
        }
    }

    /** Re-reads the credential store and the permission grants. Called on ON_RESUME by the screens. */
    fun refresh() {
        viewModelScope.launch { refreshNow() }
    }

    private suspend fun refreshNow() {
        deviceSnapshot.value = withContext(Dispatchers.IO) {
            DeviceSnapshot(
                loaded = true,
                isPaired = securePrefs.isPaired,
                childName = securePrefs.childName?.takeIf { it.isNotBlank() },
                permissions = readPermissionState(appContext),
                nowMillis = System.currentTimeMillis(),
            )
        }
    }

    fun acceptConsent() {
        viewModelScope.launch {
            withContext(Dispatchers.IO) { trackingPrefs.acceptConsent() }
            refreshNow()
        }
    }

    /**
     * Enrolls this device with the code shown on the parent dashboard. The code is accepted with or
     * without its dash and in any case; the server normalises it the same way.
     */
    fun pair(rawCode: String) {
        val code = normalisePairingCode(rawCode)
        if (code.length != PAIRING_CODE_LENGTH) {
            transient.update { it.copy(pairing = PairingUiState(error = PairingError.INCOMPLETE)) }
            return
        }
        if (transient.value.pairing.isSubmitting) return
        transient.update { it.copy(pairing = PairingUiState(isSubmitting = true)) }

        viewModelScope.launch {
            // enroll() already maps expected failures into Result; this guards the rest.
            val outcome = runCatching { enrollmentRepository.enroll(code) }
                .getOrElse { unexpected -> Result.failure(unexpected) }
            outcome.fold(
                onSuccess = {
                    refreshNow()
                    transient.update { it.copy(pairing = PairingUiState(succeeded = true)) }
                },
                onFailure = { throwable ->
                    Log.w(TAG, "Pairing failed", throwable)
                    transient.update {
                        it.copy(pairing = PairingUiState(error = classifyPairingError(throwable)))
                    }
                },
            )
        }
    }

    /** Clears the one-shot success flag once the UI has navigated away from the pairing screen. */
    fun onPairingHandled() {
        transient.update { it.copy(pairing = PairingUiState()) }
    }

    fun clearPairingError() {
        transient.update { it.copy(pairing = it.pairing.copy(error = null)) }
    }

    fun startSharing() {
        viewModelScope.launch {
            // TrackingController reads the credential store, whose first touch loads a Tink keyset
            // and can generate a keystore master key; none of that belongs on the main thread.
            val blocker = withContext(Dispatchers.IO) {
                trackingPrefs.lastServiceError = null
                TrackingController.start(appContext).toBlocker()
            }
            transient.update { it.copy(startFailure = blocker) }
            refreshNow()
        }
    }

    fun stopSharing() {
        viewModelScope.launch {
            withContext(Dispatchers.IO) { TrackingController.stop(appContext) }
            transient.update { it.copy(startFailure = null) }
            refreshNow()
        }
    }

    fun dismissStartFailure() {
        transient.update { it.copy(startFailure = null) }
    }

    /** Clears the banner raised by a failure the service recorded on its own. */
    fun dismissServiceError() {
        viewModelScope.launch {
            withContext(Dispatchers.IO) { trackingPrefs.lastServiceError = null }
        }
    }

    /**
     * "Unpair & delete token": stops sharing first so nothing is running without a credential,
     * then hands over to the repository, which deletes the token, the queued fixes and the
     * scheduled uploads.
     */
    fun unpair() {
        viewModelScope.launch {
            try {
                withContext(Dispatchers.IO) {
                    TrackingController.stop(appContext)
                    // The queue is passed in on purpose: R.string.unpair_confirm_message promises
                    // the user that locations still waiting to be sent are discarded.
                    enrollmentRepository.unpair(appContext, locationRepository)
                }
            } catch (throwable: Throwable) {
                // The token is gone either way; a failure here only leaves queued rows behind.
                Log.w(TAG, "Unpair did not complete cleanly", throwable)
            }
            transient.value = TransientState()
            refreshNow()
        }
    }

    private fun TrackingStartCheck.toBlocker(): SharingBlocker? = when (val check = this) {
        TrackingStartCheck.Ready -> null
        TrackingStartCheck.ConsentMissing -> SharingBlocker.CONSENT_MISSING
        TrackingStartCheck.NotPaired -> SharingBlocker.NOT_PAIRED
        TrackingStartCheck.LocationPermissionMissing -> SharingBlocker.LOCATION_PERMISSION
        is TrackingStartCheck.ServiceStartFailed -> {
            Log.w(TAG, "Location sharing did not start: ${check.message}")
            SharingBlocker.SERVICE_START_FAILED
        }
    }

    private fun classifyPairingError(throwable: Throwable): PairingError = when {
        throwable is IllegalArgumentException -> PairingError.INCOMPLETE
        throwable is IOException -> PairingError.NETWORK
        throwable is ApiException && throwable.statusCode == HTTP_TOO_MANY_REQUESTS ->
            PairingError.SERVER

        throwable is ApiException && throwable.statusCode >= HttpURLConnection.HTTP_INTERNAL_ERROR ->
            PairingError.SERVER

        throwable is ApiException && throwable.statusCode == HttpURLConnection.HTTP_BAD_REQUEST ->
            PairingError.INVALID_CODE

        else -> PairingError.UNKNOWN
    }

    companion object {
        private const val HTTP_TOO_MANY_REQUESTS = 429

        val Factory: ViewModelProvider.Factory = viewModelFactory {
            initializer {
                MainViewModel(
                    appContext = ServiceLocator.appContext,
                    trackingPrefs = ServiceLocator.trackingPrefs,
                    securePrefs = ServiceLocator.securePrefs,
                    enrollmentRepository = ServiceLocator.enrollmentRepository,
                    locationRepository = ServiceLocator.locationRepository,
                    pendingCount = ServiceLocator.locationRepository.pendingCount,
                )
            }
        }
    }
}

/** Uppercases and strips the display dash, so "ab3d-9kmp" and "AB3D9KMP" both enroll. */
fun normalisePairingCode(raw: String): String = EnrollmentRepository.normalizePairingCode(raw)

/** Reads the live grant state of every permission the app asks for. */
@SuppressLint("InlinedApi") // The permission constants are inlined; every use is guarded by SDK_INT.
fun readPermissionState(context: Context): PermissionState {
    fun granted(permission: String): Boolean =
        ContextCompat.checkSelfPermission(context, permission) == PackageManager.PERMISSION_GRANTED

    val notificationsRequired = Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU
    val backgroundRequired = Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q
    val powerManager = context.getSystemService(PowerManager::class.java)

    return PermissionState(
        notificationsRequired = notificationsRequired,
        notificationsGranted = !notificationsRequired ||
            granted(Manifest.permission.POST_NOTIFICATIONS),
        fineLocationGranted = granted(Manifest.permission.ACCESS_FINE_LOCATION),
        coarseLocationGranted = granted(Manifest.permission.ACCESS_COARSE_LOCATION),
        backgroundLocationRequired = backgroundRequired,
        backgroundLocationGranted = !backgroundRequired ||
            granted(Manifest.permission.ACCESS_BACKGROUND_LOCATION),
        batteryOptimisationIgnored =
            powerManager?.isIgnoringBatteryOptimizations(context.packageName) == true,
    )
}

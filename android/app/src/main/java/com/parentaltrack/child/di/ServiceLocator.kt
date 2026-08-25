package com.parentaltrack.child.di

import android.content.Context
import com.parentaltrack.child.data.local.AppDatabase
import com.parentaltrack.child.data.prefs.SecurePrefs
import com.parentaltrack.child.data.prefs.TrackingPrefs
import com.parentaltrack.child.data.remote.ApiClient
import com.parentaltrack.child.data.remote.TrackingApi
import com.parentaltrack.child.data.repo.EnrollmentRepository
import com.parentaltrack.child.data.repo.LocationRepository

/**
 * Hand-rolled dependency graph (no Hilt, so the build needs no extra annotation processors).
 *
 * [init] is called once from `ChildApp.onCreate`, which runs before any Activity, Service,
 * Receiver or Worker in this process, so every consumer can read these properties directly. This
 * object is where the graph is wired: it is the only place that constructs the prefs, the Retrofit
 * API and the two repositories, so there is exactly one instance of each per process. The delegates
 * are `by lazy` (synchronized) because the tracking service and the upload worker touch them off
 * the main thread — and [securePrefs] in particular is expensive on first touch (keystore master
 * key + Tink keyset), which is why [com.parentaltrack.child.ChildApp] warms it up in the background.
 */
object ServiceLocator {

    @Volatile
    private var applicationContext: Context? = null

    fun init(context: Context) {
        if (applicationContext == null) {
            synchronized(this) {
                if (applicationContext == null) {
                    applicationContext = context.applicationContext
                }
            }
        }
    }

    val appContext: Context
        get() = checkNotNull(applicationContext) {
            "ServiceLocator.init(context) must be called from ChildApp.onCreate()"
        }

    val database: AppDatabase by lazy { AppDatabase.getInstance(appContext) }

    val securePrefs: SecurePrefs by lazy { SecurePrefs(appContext) }

    val trackingPrefs: TrackingPrefs by lazy { TrackingPrefs(appContext) }

    /** The one Retrofit stack; its AuthInterceptor reads the token from [securePrefs] per call. */
    val api: TrackingApi by lazy { ApiClient.create(securePrefs) }

    val locationRepository: LocationRepository by lazy {
        LocationRepository(database.pendingLocationDao(), api, trackingPrefs)
    }

    val enrollmentRepository: EnrollmentRepository by lazy {
        EnrollmentRepository(api, securePrefs, trackingPrefs)
    }
}

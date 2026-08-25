package com.parentaltrack.child

import android.app.Application
import android.util.Log
import androidx.work.Configuration
import com.parentaltrack.child.di.ServiceLocator
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch

/**
 * Application entry point. Builds the hand-rolled dependency graph before anything else can run and
 * supplies the WorkManager configuration used by [com.parentaltrack.child.work.LocationUploadWorker].
 */
class ChildApp : Application(), Configuration.Provider {

    private val appScope = CoroutineScope(SupervisorJob() + Dispatchers.IO)

    override fun onCreate() {
        super.onCreate()
        ServiceLocator.init(this)

        // Building SecurePrefs generates/loads an Android Keystore master key and a Tink keyset,
        // both of which touch disk and can take a noticeable moment on a cold start. Warming it
        // here means the first UI, service or receiver read finds it already built instead of
        // paying for it on whatever thread happens to ask first.
        appScope.launch {
            runCatching { ServiceLocator.securePrefs }
                .onFailure { Log.w(TAG, "Pre-warming the credential store failed", it) }
        }
    }

    override val workManagerConfiguration: Configuration
        get() = Configuration.Builder()
            .setMinimumLoggingLevel(if (BuildConfig.DEBUG) Log.DEBUG else Log.INFO)
            .build()

    private companion object {
        const val TAG = "ChildApp"
    }
}

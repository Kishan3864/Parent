package com.parentaltrack.child.service

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.util.Log
import androidx.core.os.UserManagerCompat
import com.parentaltrack.child.di.ServiceLocator
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch

/**
 * Restores location sharing after a reboot — but only when the child left it switched on and
 * everything it depends on is still in place. Sharing is never resumed silently.
 */
class BootReceiver : BroadcastReceiver() {

    override fun onReceive(context: Context, intent: Intent?) {
        val action = intent?.action
        if (action != Intent.ACTION_BOOT_COMPLETED && action != Intent.ACTION_LOCKED_BOOT_COMPLETED) {
            return
        }

        // The device token lives in credential-encrypted storage, which cannot be read before
        // the first unlock. ACTION_BOOT_COMPLETED arrives again afterwards, so waiting is free.
        if (!UserManagerCompat.isUserUnlocked(context)) {
            Log.i(TAG, "Boot received before unlock; waiting for ACTION_BOOT_COMPLETED")
            return
        }

        // onReceive runs on the main thread and the checks below open the encrypted credential
        // store (keystore + Tink keyset, both disk-backed). goAsync keeps the receiver alive
        // while that happens on an IO thread.
        val pendingResult = goAsync()
        CoroutineScope(SupervisorJob() + Dispatchers.IO).launch {
            try {
                restore(context)
            } catch (throwable: Throwable) {
                Log.w(TAG, "Restoring location sharing after boot failed", throwable)
            } finally {
                pendingResult.finish()
            }
        }
    }

    private fun restore(context: Context) {
        if (!ServiceLocator.trackingPrefs.trackingEnabled) {
            Log.i(TAG, "Location sharing was switched off; not restarting after boot")
            return
        }

        when (val check = TrackingController.canStart(context)) {
            TrackingStartCheck.Ready -> {
                val result = TrackingController.start(context)
                if (result != TrackingStartCheck.Ready) {
                    Log.w(TAG, "Restarting location sharing after boot failed: $result")
                }
            }
            else -> Log.i(TAG, "Not restarting location sharing after boot: $check")
        }
    }

    private companion object {
        const val TAG = "BootReceiver"
    }
}

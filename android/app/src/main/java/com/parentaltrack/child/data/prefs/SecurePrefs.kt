package com.parentaltrack.child.data.prefs

import android.content.Context
import android.content.SharedPreferences
import android.util.Log
import androidx.security.crypto.EncryptedSharedPreferences
import androidx.security.crypto.MasterKey
import java.util.UUID

/**
 * Storage for the device credential.
 *
 * Backed by [EncryptedSharedPreferences]. Some devices ship with a broken or locked Android
 * keystore and both the master-key creation and the first read can throw; when that happens we fall
 * back to a plain [SharedPreferences] file so the app keeps working instead of crash-looping. The
 * fallback is logged and exposed through [isEncrypted].
 */
class SecurePrefs(context: Context) {

    private val prefs: SharedPreferences

    /** False when the keystore was unavailable and the plaintext fallback file is in use. */
    val isEncrypted: Boolean

    init {
        val appContext = context.applicationContext
        // A corrupted keyset is only recoverable by throwing the file away; the only thing stored
        // here is a device token that can be obtained again by pairing.
        val encrypted = openEncrypted(appContext, deleteFirst = false)
            ?: openEncrypted(appContext, deleteFirst = true)
        if (encrypted != null) {
            prefs = encrypted
            isEncrypted = true
        } else {
            Log.w(TAG, "Falling back to unencrypted preferences: the Android keystore is unavailable")
            prefs = appContext.getSharedPreferences(FALLBACK_FILE_NAME, Context.MODE_PRIVATE)
            isEncrypted = false
        }
    }

    var deviceToken: String?
        get() = prefs.getString(KEY_DEVICE_TOKEN, null)
        set(value) = put(KEY_DEVICE_TOKEN, value)

    var deviceId: String?
        get() = prefs.getString(KEY_DEVICE_ID, null)
        set(value) = put(KEY_DEVICE_ID, value)

    var childName: String?
        get() = prefs.getString(KEY_CHILD_NAME, null)
        set(value) = put(KEY_CHILD_NAME, value)

    /** Stable random id for this installation; generated and persisted on first read. */
    val installId: String
        get() = synchronized(this) {
            prefs.getString(KEY_INSTALL_ID, null)
                ?: UUID.randomUUID().toString().also {
                    prefs.edit().putString(KEY_INSTALL_ID, it).apply()
                }
        }

    val isPaired: Boolean
        get() = !deviceToken.isNullOrBlank()

    /** Wipes the credential and the install id (used by "Unpair & delete token"). */
    fun clear() {
        prefs.edit().clear().apply()
    }

    private fun put(key: String, value: String?) {
        val editor = prefs.edit()
        if (value == null) editor.remove(key) else editor.putString(key, value)
        editor.apply()
    }

    private companion object {
        const val TAG = "SecurePrefs"
        const val ENCRYPTED_FILE_NAME = "pt_secure_prefs"
        const val FALLBACK_FILE_NAME = "pt_secure_prefs_plain"

        const val KEY_DEVICE_TOKEN = "deviceToken"
        const val KEY_DEVICE_ID = "deviceId"
        const val KEY_CHILD_NAME = "childName"
        const val KEY_INSTALL_ID = "installId"

        fun openEncrypted(appContext: Context, deleteFirst: Boolean): SharedPreferences? = try {
            if (deleteFirst) {
                appContext.deleteSharedPreferences(ENCRYPTED_FILE_NAME)
            }
            val masterKey = MasterKey.Builder(appContext, MasterKey.DEFAULT_MASTER_KEY_ALIAS)
                .setKeyScheme(MasterKey.KeyScheme.AES256_GCM)
                .build()
            EncryptedSharedPreferences.create(
                appContext,
                ENCRYPTED_FILE_NAME,
                masterKey,
                EncryptedSharedPreferences.PrefKeyEncryptionScheme.AES256_SIV,
                EncryptedSharedPreferences.PrefValueEncryptionScheme.AES256_GCM,
            )
        } catch (e: Exception) {
            // GeneralSecurityException, IOException and vendor-specific keystore RuntimeExceptions
            // all end up here; none of them may take the app down.
            Log.w(TAG, "EncryptedSharedPreferences unavailable (deleteFirst=$deleteFirst)", e)
            null
        }
    }
}

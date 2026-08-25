package com.parentaltrack.child.location

import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.os.BatteryManager
import android.util.Log
import androidx.core.content.ContextCompat
import kotlin.math.roundToInt

/** Battery telemetry attached to a fix. Either member is null when the device will not report it. */
data class BatteryState(val percent: Int?, val isCharging: Boolean?)

/**
 * Reads the two battery fields the contract allows us to send: charge percentage and
 * charging state. Nothing else about the device is inspected.
 */
class BatteryReader(context: Context) {

    private val appContext = context.applicationContext

    fun read(): BatteryState {
        val manager = appContext.getSystemService(Context.BATTERY_SERVICE) as? BatteryManager
        var percent = manager?.let(::readCapacity)
        var charging = manager?.isCharging

        if (percent == null || charging == null) {
            val sticky = readStickyBatteryIntent()
            if (sticky != null) {
                if (percent == null) percent = percentFrom(sticky)
                if (charging == null) charging = chargingFrom(sticky)
            }
        }
        return BatteryState(percent, charging)
    }

    /** Some devices report Int.MIN_VALUE or -1 when the property is unsupported. */
    private fun readCapacity(manager: BatteryManager): Int? =
        manager.getIntProperty(BatteryManager.BATTERY_PROPERTY_CAPACITY).takeIf { it in 0..100 }

    private fun readStickyBatteryIntent(): Intent? =
        try {
            ContextCompat.registerReceiver(
                appContext,
                null,
                IntentFilter(Intent.ACTION_BATTERY_CHANGED),
                ContextCompat.RECEIVER_NOT_EXPORTED,
            )
        } catch (e: IllegalArgumentException) {
            Log.w(TAG, "Battery status broadcast unavailable", e)
            null
        } catch (e: SecurityException) {
            Log.w(TAG, "Not allowed to read the battery status broadcast", e)
            null
        }

    private fun percentFrom(intent: Intent): Int? {
        val level = intent.getIntExtra(BatteryManager.EXTRA_LEVEL, -1)
        val scale = intent.getIntExtra(BatteryManager.EXTRA_SCALE, -1)
        if (level < 0 || scale <= 0) return null
        return (level * 100f / scale).roundToInt().coerceIn(0, 100)
    }

    private fun chargingFrom(intent: Intent): Boolean? =
        when (intent.getIntExtra(BatteryManager.EXTRA_STATUS, -1)) {
            BatteryManager.BATTERY_STATUS_CHARGING, BatteryManager.BATTERY_STATUS_FULL -> true
            BatteryManager.BATTERY_STATUS_DISCHARGING, BatteryManager.BATTERY_STATUS_NOT_CHARGING -> false
            else -> null
        }

    private companion object {
        const val TAG = "BatteryReader"
    }
}

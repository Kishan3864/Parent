package com.parentaltrack.child.location

import android.location.Location
import android.location.LocationManager

/**
 * Maps a platform [Location] provider onto the wire values the backend accepts
 * (contract §1: "gps" | "network" | "fused" | "passive" | "unknown").
 */
object ProviderMapper {

    /** `LocationManager.FUSED_PROVIDER` is only public API from 31; minSdk here is 24. */
    private const val FUSED_PROVIDER = "fused"

    /**
     * Mock fixes are mapped like any other fix and are deliberately not dropped: silently
     * discarding them would leave the parent staring at a stale position with no explanation.
     */
    fun fromLocation(location: Location): String = fromProviderName(location.provider)

    fun fromProviderName(provider: String?): String =
        when (provider?.trim()?.lowercase()) {
            LocationManager.GPS_PROVIDER -> "gps"
            LocationManager.NETWORK_PROVIDER -> "network"
            LocationManager.PASSIVE_PROVIDER -> "passive"
            FUSED_PROVIDER -> "fused"
            else -> "unknown"
        }
}

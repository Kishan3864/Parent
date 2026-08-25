package com.parentaltrack.child.data.local

import androidx.room.Entity
import androidx.room.Index
import androidx.room.PrimaryKey

/**
 * One queued location fix waiting to be uploaded.
 *
 * Column names are the property names on purpose: this table is the device-local upload buffer
 * (contract 5.4) and is never read by anything outside this app.
 */
@Entity(
    tableName = "pending_locations",
    indices = [Index(value = ["recordedAtEpochMillis"])],
)
data class PendingLocationEntity(
    @PrimaryKey(autoGenerate = true)
    val id: Long = 0L,
    /** Idempotency key sent to the server; a fresh UUID is generated for every fix. */
    val clientId: String,
    val latitude: Double,
    val longitude: Double,
    val accuracyMeters: Double,
    val altitudeMeters: Double?,
    val speedMps: Double?,
    val bearingDeg: Double?,
    val batteryPercent: Int?,
    val isCharging: Boolean?,
    /** Wire value of the provider: "unknown" | "gps" | "network" | "fused" | "passive". */
    val provider: String,
    val recordedAtEpochMillis: Long,
    /** Number of failed upload attempts; used to drop points the server keeps rejecting. */
    val attemptCount: Int = 0,
)

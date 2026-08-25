package com.parentaltrack.child.data.remote

import kotlinx.serialization.Serializable

/** Body of `POST /api/v1/devices/enroll`. */
@Serializable
data class EnrollRequest(
    val pairingCode: String,
    val installId: String,
    val manufacturer: String? = null,
    val model: String? = null,
    val osVersion: String? = null,
    val appVersion: String? = null,
)

/** The `tracking` block returned by enroll and `GET /api/v1/devices/me`. */
@Serializable
data class TrackingConfigDto(
    val intervalSeconds: Int,
    val fastestIntervalSeconds: Int,
    val minDistanceMeters: Int,
    val batchMaxSize: Int,
    val uploadIntervalSeconds: Int,
)

@Serializable
data class EnrollResponse(
    val deviceId: String,
    val childName: String,
    val deviceToken: String,
    val tokenExpiresAtUtc: String,
    val tracking: TrackingConfigDto,
)

@Serializable
data class DeviceSelfDto(
    val deviceId: String,
    val childName: String,
    val isActive: Boolean,
    val tracking: TrackingConfigDto,
)

/** One queued fix. [recordedAt] is ISO-8601 UTC with milliseconds and a trailing `Z`. */
@Serializable
data class IngestPointDto(
    val clientId: String,
    val latitude: Double,
    val longitude: Double,
    val accuracyMeters: Double,
    val altitudeMeters: Double? = null,
    val speedMetersPerSecond: Double? = null,
    val bearingDegrees: Double? = null,
    val batteryPercent: Int? = null,
    val isCharging: Boolean? = null,
    val provider: String,
    val recordedAt: String,
)

@Serializable
data class IngestRequest(
    val points: List<IngestPointDto>,
)

@Serializable
data class IngestResponse(
    val accepted: Int,
    val duplicates: Int,
    val rejected: Int,
    val serverTimeUtc: String,
)

/** RFC7807 error body; every non-2xx response from the API uses this shape (contract 0). */
@Serializable
data class ProblemDetails(
    val type: String? = null,
    val title: String? = null,
    val status: Int? = null,
    val detail: String? = null,
    val instance: String? = null,
)

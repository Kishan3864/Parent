package com.parentaltrack.child.data.remote

import retrofit2.Response
import retrofit2.http.Body
import retrofit2.http.GET
import retrofit2.http.POST

/**
 * The three endpoints the child app talks to. Every call returns [Response] so callers can act on
 * the exact status code (401 = revoked, 400 = permanently rejected, 5xx = retry).
 */
interface TrackingApi {

    @POST("api/v1/devices/enroll")
    suspend fun enroll(@Body request: EnrollRequest): Response<EnrollResponse>

    @GET("api/v1/devices/me")
    suspend fun me(): Response<DeviceSelfDto>

    @POST("api/v1/ingest/locations")
    suspend fun ingestLocations(@Body request: IngestRequest): Response<IngestResponse>
}

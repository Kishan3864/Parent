package com.parentaltrack.child.data.remote

import com.parentaltrack.child.BuildConfig
import com.parentaltrack.child.data.prefs.SecurePrefs
import kotlinx.serialization.ExperimentalSerializationApi
import kotlinx.serialization.json.Json
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.logging.HttpLoggingInterceptor
import retrofit2.Retrofit
import retrofit2.converter.kotlinx.serialization.asConverterFactory
import java.util.concurrent.TimeUnit

/** Builds the single OkHttp + Retrofit stack used by the app. */
object ApiClient {

    /** Shared JSON configuration; also used to parse RFC7807 error bodies. */
    @OptIn(ExperimentalSerializationApi::class)
    val json: Json = Json {
        ignoreUnknownKeys = true
        explicitNulls = false
    }

    private const val CONNECT_TIMEOUT_SECONDS = 15L
    private const val READ_TIMEOUT_SECONDS = 30L
    private const val WRITE_TIMEOUT_SECONDS = 30L
    private const val CALL_TIMEOUT_SECONDS = 60L

    private val contentType = "application/json".toMediaType()

    fun create(securePrefs: SecurePrefs): TrackingApi {
        val builder = OkHttpClient.Builder()
            .addInterceptor(AuthInterceptor(securePrefs))
            .connectTimeout(CONNECT_TIMEOUT_SECONDS, TimeUnit.SECONDS)
            .readTimeout(READ_TIMEOUT_SECONDS, TimeUnit.SECONDS)
            .writeTimeout(WRITE_TIMEOUT_SECONDS, TimeUnit.SECONDS)
            .callTimeout(CALL_TIMEOUT_SECONDS, TimeUnit.SECONDS)
            .retryOnConnectionFailure(true)

        if (BuildConfig.DEBUG) {
            // Debug builds only: bodies contain locations, and the header would contain the token.
            val logging = HttpLoggingInterceptor()
            logging.level = HttpLoggingInterceptor.Level.BODY
            logging.redactHeader("Authorization")
            builder.addInterceptor(logging)
        }

        return Retrofit.Builder()
            .baseUrl(BuildConfig.API_BASE_URL)
            .client(builder.build())
            .addConverterFactory(json.asConverterFactory(contentType))
            .build()
            .create(TrackingApi::class.java)
    }
}

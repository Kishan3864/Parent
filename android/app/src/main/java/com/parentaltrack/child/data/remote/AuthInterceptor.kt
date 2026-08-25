package com.parentaltrack.child.data.remote

import com.parentaltrack.child.data.prefs.SecurePrefs
import okhttp3.Interceptor
import okhttp3.Response

/**
 * Adds `Authorization: Bearer <deviceToken>` to every call except enrollment, which is the call
 * that obtains the token in the first place (and is anonymous + rate limited server side).
 */
class AuthInterceptor(private val securePrefs: SecurePrefs) : Interceptor {

    override fun intercept(chain: Interceptor.Chain): Response {
        val request = chain.request()
        if (request.url.encodedPath.endsWith(ENROLL_PATH_SUFFIX)) {
            return chain.proceed(request)
        }
        val token = securePrefs.deviceToken
        if (token.isNullOrBlank()) {
            return chain.proceed(request)
        }
        val authorized = request.newBuilder()
            .header("Authorization", "Bearer $token")
            .build()
        return chain.proceed(authorized)
    }

    private companion object {
        const val ENROLL_PATH_SUFFIX = "/devices/enroll"
    }
}

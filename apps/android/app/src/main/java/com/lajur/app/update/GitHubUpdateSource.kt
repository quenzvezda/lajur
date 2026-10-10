package com.lajur.app.update

import java.io.File
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicReference
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.withContext
import kotlinx.serialization.json.Json
import okhttp3.Call
import okhttp3.OkHttpClient
import okhttp3.Request

internal class GitHubUpdateSource(private val json: Json) {
    // This client has no Lajur session headers or GitHub credentials.
    private val client = OkHttpClient.Builder().followSslRedirects(false)
        .connectTimeout(15, TimeUnit.SECONDS).readTimeout(20, TimeUnit.SECONDS).build()
    private val active = AtomicReference<Call?>()

    fun cancel() { active.get()?.cancel() }

    suspend fun latest(): AppUpdateManifest = withContext(Dispatchers.IO) {
        try { request(AppUpdatePolicy.MANIFEST_URL, 30).use { response ->
            check(response.isSuccessful) { "Pembaruan belum dapat diperiksa. Coba lagi nanti." }
            val body = checkNotNull(response.body)
            val bytes = body.byteStream().readBytesBounded(64 * 1024)
            ensureActive()
            json.decodeFromString<AppUpdateManifest>(bytes.toString(Charsets.UTF_8)).also(AppUpdatePolicy::validate)
        } } finally { active.set(null) }
    }

    suspend fun download(manifest: AppUpdateManifest, file: File, progress: (Int) -> Unit): File = withContext(Dispatchers.IO) {
        AppUpdatePolicy.validate(manifest)
        try { request(manifest.apkUrl, 300).use { response ->
            check(response.isSuccessful) { "APK belum dapat diunduh. Coba lagi nanti." }
            val body = checkNotNull(response.body)
            require(body.contentLength() < 0 || body.contentLength() == manifest.sizeBytes) { "Ukuran APK berbeda dari release." }
            body.byteStream().use { input ->
                copyVerifiedApk(input, file, manifest.sizeBytes, manifest.sha256, { ensureActive() }, progress)
            }
        } } finally { active.set(null) }
    }

    private fun request(url: String, timeoutSeconds: Long): okhttp3.Response {
        val call = client.newCall(Request.Builder().url(url).header("User-Agent", "Lajur-Android-Updater").build())
        call.timeout().timeout(timeoutSeconds, TimeUnit.SECONDS)
        active.set(call)
        try {
            return call.execute()
        } catch (error: Exception) {
            active.compareAndSet(call, null)
            throw error
        }
        // Keep the call cancellable while its response body is being streamed.
    }
}

private fun java.io.InputStream.readBytesBounded(max: Int): ByteArray {
    val output = java.io.ByteArrayOutputStream()
    val buffer = ByteArray(4096)
    while (true) {
        val count = read(buffer)
        if (count < 0) return output.toByteArray()
        require(output.size() + count <= max) { "Metadata pembaruan terlalu besar." }
        output.write(buffer, 0, count)
    }
}

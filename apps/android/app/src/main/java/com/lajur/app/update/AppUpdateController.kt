package com.lajur.app.update

import android.content.Context
import android.content.Intent
import android.content.pm.PackageInfo
import android.content.pm.PackageManager
import android.os.Build
import androidx.core.content.FileProvider
import com.lajur.app.BuildConfig
import java.io.File
import java.security.MessageDigest
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.CoroutineStart
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.NonCancellable
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json

data class AppUpdateState(
    val checking: Boolean = false,
    val checked: Boolean = false,
    val release: AppUpdateManifest? = null,
    val progress: Int? = null,
    val ready: Boolean = false,
    val error: String? = null,
    val incompatibility: String? = null,
    val autoCheck: Boolean = true,
)

class AppUpdateController(context: Context, private val json: Json) {
    private val context = context.applicationContext
    private val preferences = AppUpdatePreferences(this.context)
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)
    private val source = GitHubUpdateSource(json)
    private val directory = File(this.context.cacheDir, "updates")
    private val apk = File(directory, "release.apk")
    private val mutableState = MutableStateFlow(AppUpdateState(checking = true))
    val state = mutableState.asStateFlow()
    private var operation: Job? = null
    private var lastAttempt = 0L

    init {
        runOperation(onFinished = { check(manual = false) }) {
            withContext(Dispatchers.IO) {
                val savedPreferences = preferences.read()
                lastAttempt = savedPreferences.lastAttempt
                mutableState.update { it.copy(autoCheck = savedPreferences.automatic) }
                directory.mkdirs()
                File(directory, "release.apk.part").delete()
                val saved = savedPreferences.readyManifest
                if (saved != null) {
                    runCatching {
                        val manifest = json.decodeFromString<AppUpdateManifest>(saved)
                        check(manifest.versionCode > BuildConfig.VERSION_CODE)
                        verifyFile(manifest)
                        mutableState.value = mutableState.value.copy(release = manifest, ready = true)
                    }.onFailure { discardReady() }
                } else apk.delete()
            }
        }
    }

    fun setAutoCheck(enabled: Boolean) {
        scope.launch {
            try {
                preferences.automatic(enabled)
                mutableState.update { it.copy(autoCheck = enabled) }
            } catch (_: Exception) {
                mutableState.update { it.copy(error = "Preferensi pembaruan belum dapat disimpan.") }
            }
        }
    }

    fun check(manual: Boolean = true) {
        if (operation != null || (!manual && mutableState.value.ready)) return
        val now = System.currentTimeMillis()
        if (!manual && (BuildConfig.DEBUG || !mutableState.value.autoCheck ||
                !AppUpdatePolicy.shouldCheck(lastAttempt, now))) return
        runOperation {
            mutableState.value = mutableState.value.copy(checking = true, error = null)
            try {
                preferences.attempt(now)
                lastAttempt = now
                val release = source.latest().takeIf { it.versionCode > BuildConfig.VERSION_CODE }
                val keepReady = mutableState.value.ready && mutableState.value.release == release
                if (!keepReady) discardReady()
                val incompatibility = when {
                    release == null -> null
                    release.minSdk > Build.VERSION.SDK_INT -> "Versi ini memerlukan Android yang lebih baru."
                    installedCertificates() != setOf(AppUpdatePolicy.CERTIFICATE) ->
                        "Build pengembangan ini tidak dapat diperbarui langsung. Sinkronkan data, lalu gunakan APK release dari GitHub."
                    else -> null
                }
                mutableState.value = mutableState.value.copy(checking = false, checked = true, release = release, ready = keepReady, incompatibility = incompatibility)
            } catch (cancelled: CancellationException) {
                mutableState.value = mutableState.value.copy(checking = false)
                throw cancelled
            } catch (_: Exception) {
                ensureActive()
                mutableState.value = mutableState.value.copy(checking = false,
                    error = if (manual) "Belum bisa memeriksa pembaruan. Periksa koneksi, lalu coba lagi." else null)
            }
        }
    }

    fun download() {
        val release = mutableState.value.release ?: return
        if (operation != null || mutableState.value.incompatibility != null || mutableState.value.ready) return
        runOperation {
            mutableState.value = mutableState.value.copy(progress = 0, error = null)
            try {
                directory.mkdirs()
                source.download(release, apk) { value -> mutableState.update { it.copy(progress = value) } }
                withContext(Dispatchers.IO) { verifyFile(release) }
                preferences.ready(json.encodeToString(release))
                mutableState.value = mutableState.value.copy(progress = null, ready = true)
            } catch (cancelled: CancellationException) {
                discardReady()
                mutableState.value = mutableState.value.copy(progress = null, ready = false)
                throw cancelled
            } catch (_: Exception) {
                discardReady()
                ensureActive()
                mutableState.value = mutableState.value.copy(progress = null, ready = false,
                    error = "Unduhan gagal atau APK tidak sesuai release. Silakan unduh ulang.")
            }
        }
    }

    fun cancelDownload() {
        operation?.cancel()
        source.cancel()
    }

    /** Revalidate cache at the last moment; installation itself is owned by Android. */
    fun prepareInstallation(onReady: (Intent) -> Unit) {
        val release = mutableState.value.release ?: return
        if (operation != null || !mutableState.value.ready) return
        runOperation {
            mutableState.value = mutableState.value.copy(checking = true, error = null)
            try {
                try {
                    withContext(Dispatchers.IO) { verifyFile(release) }
                } catch (invalid: Exception) {
                    discardReady()
                    mutableState.value = mutableState.value.copy(ready = false)
                    throw invalid
                }
                check(context.packageManager.canRequestPackageInstalls())
                val uri = FileProvider.getUriForFile(context, "${context.packageName}.fileprovider", apk)
                onReady(Intent(Intent.ACTION_VIEW).setDataAndType(uri, "application/vnd.android.package-archive")
                    .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION))
                mutableState.value = mutableState.value.copy(checking = false)
            } catch (cancelled: CancellationException) {
                mutableState.value = mutableState.value.copy(checking = false)
                throw cancelled
            } catch (_: Exception) {
                mutableState.value = mutableState.value.copy(checking = false, error = "APK belum bisa dipasang. Periksa izin pemasangan atau unduh ulang.")
            }
        }
    }

    fun installationError() {
        mutableState.value = mutableState.value.copy(error = "Installer Android belum dapat dibuka. Coba lagi.")
    }

    private fun runOperation(onFinished: (() -> Unit)? = null, block: suspend CoroutineScope.() -> Unit) {
        if (operation != null) return
        operation = scope.launch(start = CoroutineStart.LAZY) {
            try { block() }
            catch (cancelled: CancellationException) { throw cancelled }
            catch (_: Exception) { mutableState.update { it.copy(error = "Pembaruan belum dapat diproses. Coba lagi.") } }
            finally {
                mutableState.value = mutableState.value.copy(checking = false, progress = null)
                operation = null
                onFinished?.invoke()
            }
        }
        operation!!.start()
    }

    private suspend fun discardReady() {
        withContext(NonCancellable + Dispatchers.IO) {
            apk.delete()
            runCatching { preferences.ready(null) }
        }
    }

    @Suppress("DEPRECATION")
    private fun installedCertificates(): Set<String> = certificates(context.packageManager.getPackageInfo(context.packageName, signatureFlags()))

    @Suppress("DEPRECATION")
    private fun signatureFlags(): Int = if (Build.VERSION.SDK_INT >= 28) PackageManager.GET_SIGNING_CERTIFICATES else PackageManager.GET_SIGNATURES

    @Suppress("DEPRECATION")
    private fun certificates(info: PackageInfo): Set<String> =
        (if (Build.VERSION.SDK_INT >= 28) info.signingInfo?.apkContentsSigners else info.signatures)
            ?.map { AppUpdatePolicy.sha256(it.toByteArray()) }?.toSet().orEmpty()

    @Suppress("DEPRECATION")
    private fun verifyFile(manifest: AppUpdateManifest) {
        AppUpdatePolicy.validate(manifest)
        check(manifest.versionCode > BuildConfig.VERSION_CODE && apk.isFile && apk.length() == manifest.sizeBytes)
        val digest = MessageDigest.getInstance("SHA-256")
        apk.inputStream().use { input ->
            val buffer = ByteArray(64 * 1024)
            while (true) {
                val count = input.read(buffer)
                if (count < 0) break
                digest.update(buffer, 0, count)
            }
        }
        check(digest.digest().joinToString("") { "%02x".format(it) } == manifest.sha256)
        val info = checkNotNull(context.packageManager.getPackageArchiveInfo(apk.absolutePath, signatureFlags()))
        val identity = UpdatePackageIdentity(info.packageName,
            if (Build.VERSION.SDK_INT >= 28) info.longVersionCode else info.versionCode.toLong(),
            info.versionName, checkNotNull(info.applicationInfo).minSdkVersion, certificates(info))
        AppUpdatePolicy.validatePackage(manifest, identity, installedCertificates(), Build.VERSION.SDK_INT)
    }
}

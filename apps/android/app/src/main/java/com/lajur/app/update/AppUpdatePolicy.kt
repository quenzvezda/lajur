package com.lajur.app.update

import kotlinx.serialization.Serializable
import java.io.File
import java.io.InputStream
import java.security.MessageDigest

@Serializable
data class AppUpdateManifest(
    val schemaVersion: Int,
    val versionCode: Long,
    val versionName: String,
    val applicationId: String,
    val minSdk: Int,
    val apkUrl: String,
    val sha256: String,
    val sizeBytes: Long,
    val certificateSha256: String,
    val commit: String,
    val releaseUrl: String,
)

data class UpdatePackageIdentity(val applicationId: String, val versionCode: Long,
    val versionName: String?, val minSdk: Int, val certificates: Set<String>)

object AppUpdatePolicy {
    const val MANIFEST_URL = "https://github.com/quenzvezda/lajur/releases/latest/download/update.json"
    const val CERTIFICATE = "5f2bb88e0435324ddb7a89dca1f0924e2bd53ca49e39ac0778dab64d9f6efe1b"
    const val MAX_APK_BYTES = 256L * 1024 * 1024
    const val CHECK_INTERVAL_MILLIS = 24L * 60 * 60 * 1000

    fun validate(manifest: AppUpdateManifest) {
        require(manifest.schemaVersion == 1 && manifest.applicationId == "com.lajur.app") { "Metadata pembaruan tidak didukung." }
        require(manifest.versionCode in 1..2_100_000_000L && manifest.versionName.matches(Regex("[0-9]{1,6}\\.[0-9]{1,6}\\.[0-9]{1,10}"))) { "Versi pembaruan tidak valid." }
        require(manifest.sizeBytes in 1..MAX_APK_BYTES && manifest.minSdk >= 26) { "Ukuran atau kebutuhan Android tidak valid." }
        require(manifest.sha256.matches(Regex("[0-9a-f]{64}")) && manifest.commit.matches(Regex("[0-9a-f]{40}"))) { "Metadata integritas tidak valid." }
        require(manifest.certificateSha256 == CERTIFICATE) { "Sertifikat release tidak dikenali." }
        val base = "https://github.com/quenzvezda/lajur/releases"
        require(manifest.apkUrl == "$base/download/v${manifest.versionName}/lajur-${manifest.versionName}.apk" &&
            manifest.releaseUrl == "$base/tag/v${manifest.versionName}") { "Sumber pembaruan tidak dikenali." }
    }

    fun shouldCheck(lastAttempt: Long, now: Long): Boolean =
        lastAttempt == 0L || now < lastAttempt || now - lastAttempt >= CHECK_INTERVAL_MILLIS

    fun validatePackage(manifest: AppUpdateManifest, apk: UpdatePackageIdentity, installedCertificates: Set<String>, sdk: Int) {
        validate(manifest)
        require(apk.applicationId == manifest.applicationId && apk.versionCode == manifest.versionCode &&
            apk.versionName == manifest.versionName && apk.minSdk == manifest.minSdk && sdk >= apk.minSdk) { "Identitas APK tidak sesuai release." }
        require(apk.certificates == setOf(CERTIFICATE) && installedCertificates == apk.certificates) { "APK tidak dapat memperbarui instalasi ini." }
    }

    fun sha256(bytes: ByteArray): String = MessageDigest.getInstance("SHA-256").digest(bytes).joinToString("") { "%02x".format(it) }
}

/** Bound both actual bytes and the digest; a failed/cancelled transfer never leaves an installable file. */
internal fun copyVerifiedApk(input: InputStream, destination: File, size: Long, sha256: String,
    ensureActive: () -> Unit = {}, onProgress: (Int) -> Unit = {}): File {
    require(size in 1..AppUpdatePolicy.MAX_APK_BYTES)
    val partial = File(destination.parentFile, "${destination.name}.part")
    val digest = MessageDigest.getInstance("SHA-256")
    var total = 0L
    var previous = -1
    try {
        partial.outputStream().use { output ->
            val buffer = ByteArray(64 * 1024)
            while (true) {
                ensureActive()
                val count = input.read(buffer)
                if (count < 0) break
                total += count
                require(total <= size) { "Ukuran unduhan melampaui release." }
                output.write(buffer, 0, count)
                digest.update(buffer, 0, count)
                val progress = (total * 100 / size).toInt()
                if (progress != previous) { onProgress(progress); previous = progress }
            }
        }
        ensureActive()
        require(total == size && digest.digest().joinToString("") { "%02x".format(it) } == sha256) { "Unduhan tidak lengkap atau checksum berbeda." }
        check(partial.renameTo(destination)) { "APK tidak dapat disimpan." }
        return destination
    } finally {
        partial.delete()
    }
}

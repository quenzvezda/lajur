package com.lajur.app.update

import java.io.ByteArrayInputStream
import java.io.File
import java.util.concurrent.CancellationException
import org.junit.Assert.*
import org.junit.Test

class AppUpdatePolicyTest {
    private val bytes = "verified APK fixture".toByteArray()
    private val manifest = AppUpdateManifest(1, 100006, "1.0.6", "com.lajur.app", 26,
        "https://github.com/quenzvezda/lajur/releases/download/v1.0.6/lajur-1.0.6.apk",
        AppUpdatePolicy.sha256(bytes), bytes.size.toLong(), AppUpdatePolicy.CERTIFICATE,
        "a".repeat(40), "https://github.com/quenzvezda/lajur/releases/tag/v1.0.6")

    @Test fun acceptsOfficialManifest() = AppUpdatePolicy.validate(manifest)

    @Test fun rejectsUnsupportedSchemaPackageCertificateAndUnboundedSize() {
        listOf(manifest.copy(schemaVersion = 2), manifest.copy(applicationId = "other.app"),
            manifest.copy(certificateSha256 = "b".repeat(64)), manifest.copy(sizeBytes = AppUpdatePolicy.MAX_APK_BYTES + 1),
            manifest.copy(sizeBytes = 0), manifest.copy(versionCode = 0)).forEach {
            assertThrows(IllegalArgumentException::class.java) { AppUpdatePolicy.validate(it) }
        }
    }

    @Test fun rejectsForeignHttpCredentialAndWrongVersionDownloadUrls() {
        listOf("http://github.com/quenzvezda/lajur/releases/download/v1.0.6/lajur-1.0.6.apk",
            "https://github.com/other/lajur/releases/download/v1.0.6/lajur-1.0.6.apk",
            "https://token@github.com/quenzvezda/lajur/releases/download/v1.0.6/lajur-1.0.6.apk",
            "https://github.com/quenzvezda/lajur/releases/download/v1.0.5/lajur-1.0.5.apk").forEach {
            assertThrows(IllegalArgumentException::class.java) { AppUpdatePolicy.validate(manifest.copy(apkUrl = it)) }
        }
    }

    @Test fun checkThrottleSupportsDailyAndClockRollback() {
        assertTrue(AppUpdatePolicy.shouldCheck(0, 100))
        assertFalse(AppUpdatePolicy.shouldCheck(100, 101))
        assertTrue(AppUpdatePolicy.shouldCheck(100, 100 + AppUpdatePolicy.CHECK_INTERVAL_MILLIS))
        assertTrue(AppUpdatePolicy.shouldCheck(200, 100))
    }

    @Test fun acceptsMatchingPackageAndInstalledKey() {
        AppUpdatePolicy.validatePackage(manifest, identity(), setOf(AppUpdatePolicy.CERTIFICATE), 26)
    }

    @Test fun rejectsWrongApkIdentityOrSigningKeyOrUnsupportedDevice() {
        listOf(identity().copy(applicationId = "other.app"), identity().copy(versionCode = 100005),
            identity().copy(versionName = "1.0.5"), identity().copy(minSdk = 27),
            identity().copy(certificates = setOf("wrong"))).forEach {
            assertThrows(IllegalArgumentException::class.java) {
                AppUpdatePolicy.validatePackage(manifest, it, setOf(AppUpdatePolicy.CERTIFICATE), 26)
            }
        }
        assertThrows(IllegalArgumentException::class.java) { AppUpdatePolicy.validatePackage(manifest, identity(), setOf("debug"), 26) }
        assertThrows(IllegalArgumentException::class.java) { AppUpdatePolicy.validatePackage(manifest, identity(), setOf(AppUpdatePolicy.CERTIFICATE), 25) }
    }

    @Test fun verifiedTransferPromotesOnlyCompleteMatchingBytes() = withDestination { file ->
        copyVerifiedApk(ByteArrayInputStream(bytes), file, bytes.size.toLong(), manifest.sha256)
        assertArrayEquals(bytes, file.readBytes())
        assertFalse(File(file.parent, "${file.name}.part").exists())
    }

    @Test fun corruptTruncatedAndOversizedTransfersLeaveNoInstallableFile() = withDestination { file ->
        listOf(bytes.copyOf(bytes.size - 1), bytes + 1, "corrupted APK fixture".toByteArray()).forEach {
            assertThrows(IllegalArgumentException::class.java) { copyVerifiedApk(ByteArrayInputStream(it), file, bytes.size.toLong(), manifest.sha256) }
            assertFalse(file.exists())
            assertFalse(File(file.parent, "${file.name}.part").exists())
        }
    }

    @Test fun cancelledTransferRemovesPartialAndKeepsExistingVerifiedFile() = withDestination { file ->
        file.writeBytes(bytes)
        assertThrows(CancellationException::class.java) {
            copyVerifiedApk(ByteArrayInputStream(bytes), file, bytes.size.toLong(), manifest.sha256, { throw CancellationException() })
        }
        assertArrayEquals(bytes, file.readBytes())
        assertFalse(File(file.parent, "${file.name}.part").exists())
    }

    private fun identity() = UpdatePackageIdentity("com.lajur.app", 100006, "1.0.6", 26, setOf(AppUpdatePolicy.CERTIFICATE))
    private fun withDestination(block: (File) -> Unit) {
        val directory = kotlin.io.path.createTempDirectory("lajur-update-test").toFile()
        try { block(File(directory, "release.apk")) } finally { directory.deleteRecursively() }
    }
}

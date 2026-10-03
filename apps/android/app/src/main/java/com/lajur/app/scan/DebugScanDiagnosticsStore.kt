package com.lajur.app.scan

import android.content.Context
import android.os.Build
import android.util.Log
import com.lajur.app.BuildConfig
import com.lajur.app.scan.extraction.ExtractionRunResult
import com.lajur.app.scan.extraction.ExtractionTrace
import com.lajur.app.scan.extraction.ValidatedTransactionExtraction
import java.io.File
import java.nio.file.Files
import java.nio.file.StandardCopyOption
import java.security.MessageDigest
import java.time.Instant
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.serialization.Serializable
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json

@Serializable
data class ScanDiagnosticReport(
    val schemaVersion: Int = 1,
    val createdAtUtc: String,
    val appVersion: String,
    val device: String,
    val androidApi: Int,
    val modelSha256: String?,
    val imageSource: String,
    val image: ScanImageMetadata,
    val modelOutcome: String,
    val modelLoadMillis: Long?,
    val inferenceMillis: Long?,
    val coldStart: Boolean?,
    val trace: ExtractionTrace,
    val finalReview: ValidatedTransactionExtraction,
)

/** Called only by the debug-only, explicit save action; never logs receipt contents. */
class DebugScanDiagnosticsStore(private val context: Context, private val json: Json) {
    suspend fun save(
        output: SmartScanOutput,
        result: ExtractionRunResult,
        review: ValidatedTransactionExtraction,
    ): File = withContext(Dispatchers.IO) {
        check(BuildConfig.DEBUG) { "Scan diagnostics are debug-only." }
        val model = File(context.filesDir, "models/lajur-extractor.litertlm")
        val report = ScanDiagnosticReport(
            createdAtUtc = Instant.now().toString(),
            appVersion = BuildConfig.VERSION_NAME,
            device = "${Build.MANUFACTURER} ${Build.MODEL}",
            androidApi = Build.VERSION.SDK_INT,
            modelSha256 = if (model.isFile) sha256(model) else null,
            imageSource = output.request.source.name,
            image = output.image,
            modelOutcome = when (result) {
                is ExtractionRunResult.RulesOnly -> "rules_only"
                is ExtractionRunResult.Completed -> "completed"
                is ExtractionRunResult.Unavailable -> "unavailable"
                is ExtractionRunResult.Failed -> "failed"
            },
            modelLoadMillis = result.trace.modelLoadMillis,
            inferenceMillis = result.trace.inferenceMillis,
            coldStart = result.trace.coldStart,
            trace = result.trace,
            finalReview = review,
        )
        val directory = File(context.noBackupFilesDir, "scan-diagnostics").apply { mkdirs() }
        val target = File(directory, "latest.json")
        val temporary = File.createTempFile("report-", ".tmp", directory)
        try {
            temporary.writeText(json.encodeToString(report))
            Files.move(temporary.toPath(), target.toPath(), StandardCopyOption.ATOMIC_MOVE,
                StandardCopyOption.REPLACE_EXISTING)
        } finally {
            temporary.delete()
        }
        Log.i("LajurScan", "report_saved schema=1 file=no_backup/scan-diagnostics/latest.json outcome=${report.modelOutcome}")
        target
    }

    private fun sha256(file: File): String {
        val digest = MessageDigest.getInstance("SHA-256")
        file.inputStream().buffered().use { input ->
            val buffer = ByteArray(DEFAULT_BUFFER_SIZE)
            while (true) {
                val count = input.read(buffer)
                if (count < 0) break
                digest.update(buffer, 0, count)
            }
        }
        return digest.digest().joinToString("") { "%02x".format(it) }
    }
}

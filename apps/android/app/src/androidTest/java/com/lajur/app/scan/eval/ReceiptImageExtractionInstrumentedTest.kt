package com.lajur.app.scan.eval

import androidx.core.content.FileProvider
import androidx.test.core.app.ApplicationProvider
import androidx.test.platform.app.InstrumentationRegistry
import com.lajur.app.scan.AndroidImagePreprocessor
import com.lajur.app.scan.DebugScanDiagnosticsStore
import com.lajur.app.scan.ScanDiagnosticReport
import com.lajur.app.scan.MlKitOcrEngine
import com.lajur.app.scan.ScanImageRequest
import com.lajur.app.scan.ScanImageSource
import com.lajur.app.scan.SmartScanPipeline
import com.lajur.app.scan.extraction.LocalTransactionExtractionEngine
import com.lajur.app.scan.extraction.ExtractionRunResult
import com.lajur.app.scan.extraction.TransactionExtractionValidator
import com.lajur.app.scan.llm.LiteRtLmRuntime
import java.io.File
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.runBlocking
import kotlinx.serialization.json.Json
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Assume.assumeTrue
import org.junit.Test

/** Opt-in replay of the owner's synthetic receipt; does not create/save a transaction. */
class ReceiptImageExtractionInstrumentedTest {
    @Test
    fun dummyReceiptImageProducesEvidenceBackedRequiredFields() = runBlocking {
        assumeTrue(InstrumentationRegistry.getArguments().getString("lajur.runReceiptImageTest") == "true")
        val context = ApplicationProvider.getApplicationContext<android.content.Context>()
        val wakeLock = DeviceEvaluationWakeLock(context)
        val source = File(context.noBackupFilesDir, "scan-eval/receipt.png")
        assertTrue("Side-load the dummy receipt into ${source.absolutePath}", source.isFile)
        val image = File(context.cacheDir, "smart_scan/eval-receipt.png").apply {
            parentFile?.mkdirs()
            source.copyTo(this, overwrite = true)
        }
        val uri = FileProvider.getUriForFile(context, "com.lajur.app.fileprovider", image)
        val json = Json { ignoreUnknownKeys = true; encodeDefaults = true; explicitNulls = false }
        val dataset = InstrumentationRegistry.getInstrumentation().context.assets
            .open("cases.synthetic.json").bufferedReader().use {
                json.decodeFromString<ExtractionEvaluationDataset>(it.readText())
            }
        val arguments = InstrumentationRegistry.getArguments()
        val master = if (arguments.getString("lajur.useCapturedMaster") == "true") {
            requireNotNull(json.decodeFromString<ScanDiagnosticReport>(
                File(context.noBackupFilesDir, "scan-eval/captured-report.json").readText(),
            ).trace.master)
        } else dataset.master
        val repetitions = (arguments.getString("lajur.receiptRepetitions")?.toIntOrNull() ?: 1).coerceIn(1, 5)
        val runtime = LiteRtLmRuntime(File(context.filesDir, "models/lajur-extractor.litertlm"),
            File(context.cacheDir, "litert-receipt-eval"))
        val job = SupervisorJob()
        wakeLock.acquire()
        try {
            MlKitOcrEngine().use { ocr ->
                val engine = LocalTransactionExtractionEngine(runtime, TransactionExtractionValidator(),
                    json = json, lifecycleScope = CoroutineScope(job + Dispatchers.Default))
                repeat(repetitions) { iteration -> engine.begin().use { request ->
                    val output = SmartScanPipeline(AndroidImagePreprocessor(context), ocr)
                        .process(ScanImageRequest(uri.toString(), ScanImageSource.GALLERY))
                    val result = request.extract(output.ocr, master)
                    val report = DebugScanDiagnosticsStore(context, json).save(output, result, result.review)
                    report.copyTo(File(context.noBackupFilesDir, "scan-eval/replay-${iteration + 1}.json"), overwrite = true)
                    assertTrue("Local generation must produce a parseable suggestion, not only OCR fallback", result is ExtractionRunResult.Completed)
                    assertEquals(true, result.trace.generation?.constrainedJson)
                    assertTrue(requireNotNull(result.trace.generation?.totalTokens) < requireNotNull(result.trace.generation).maxContextTokens)
                    assertEquals("2026-09-20", result.review.extraction.date.value)
                    assertEquals(68_820L, result.review.extraction.amountIdr.value)
                    assertEquals("Bank", result.review.extraction.paymentMethod.value)
                    assertTrue(result.review.extraction.description.value != "KEMBALI")
                } }
            }
        } finally {
            job.cancel()
            runtime.unload()
            image.delete()
            wakeLock.close()
        }
    }
}

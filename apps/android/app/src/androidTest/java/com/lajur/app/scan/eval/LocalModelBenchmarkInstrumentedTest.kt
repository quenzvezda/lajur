package com.lajur.app.scan.eval

import android.content.Context
import android.os.Build
import android.os.Debug
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import com.lajur.app.scan.extraction.ExtractionRunResult
import com.lajur.app.scan.extraction.LocalTransactionExtractionEngine
import com.lajur.app.scan.extraction.TransactionExtractionValidator
import com.lajur.app.scan.llm.LiteRtLmRuntime
import java.io.File
import java.security.MessageDigest
import java.time.Instant
import java.time.format.DateTimeFormatter
import java.util.concurrent.atomic.AtomicLong
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancelAndJoin
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.runBlocking
import kotlinx.serialization.decodeFromString
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import org.junit.Assert.assertTrue
import org.junit.Assume.assumeTrue
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class LocalModelBenchmarkInstrumentedTest {
    private val json = Json {
        ignoreUnknownKeys = true
        encodeDefaults = true
        explicitNulls = false
        prettyPrint = true
    }

    @Test
    fun runOptInLocalModelBenchmark() = runBlocking {
        val arguments = InstrumentationRegistry.getArguments()
        assumeTrue(
            "Set -e lajur.runModelBenchmark true to run the optional model benchmark.",
            arguments.getString("lajur.runModelBenchmark").toBoolean(),
        )

        val context = ApplicationProvider.getApplicationContext<Context>()
        val wakeLock = DeviceEvaluationWakeLock(context)
        val dataset = loadDataset(context, arguments.getString("lajur.evalPath"))
        val modelFile = arguments.getString("lajur.modelPath")
            ?.let(::File)
            ?: File(context.filesDir, "models/lajur-extractor.litertlm")
        assertTrue("Model not found: ${modelFile.absolutePath}", modelFile.isFile)
        assertTrue("Evaluation dataset is empty.", dataset.cases.isNotEmpty())

        val runtime = LiteRtLmRuntime(modelFile, File(context.cacheDir, "litert-lm-benchmark"))
        val lifecycleJob: Job = SupervisorJob()
        val lifecycleScope = CoroutineScope(lifecycleJob + Dispatchers.Default)
        val engine = LocalTransactionExtractionEngine(
            runtime = runtime,
            validator = TransactionExtractionValidator(),
            json = json,
            lifecycleScope = lifecycleScope,
        )

        wakeLock.acquire()
        val observations = try {
            dataset.cases.map { evaluationCase ->
                android.util.Log.i("LajurEval", "case_start id=${evaluationCase.id}")
                evaluateCase(engine, evaluationCase, dataset).also { observation ->
                    android.util.Log.i("LajurEval", "case_end id=${evaluationCase.id} structured=${observation.structuredOutput} inference_ms=${observation.inferenceMillis}")
                }
            }
        } finally {
            lifecycleJob.cancel()
            runtime.unload()
            wakeLock.close()
        }

        val report = ModelEvaluationReport(
            createdAtUtc = Instant.now().toString(),
            datasetName = dataset.name,
            datasetSchemaVersion = dataset.schemaVersion,
            modelFileName = modelFile.name,
            modelSha256 = sha256(modelFile),
            runtime = "LiteRT-LM 0.16.0",
            backend = "CPU",
            device = "${Build.MANUFACTURER} ${Build.MODEL}",
            androidApi = Build.VERSION.SDK_INT,
            observations = observations,
            summary = ModelEvaluation.summarize(observations),
        )
        val outputDirectory = File(
            context.getExternalFilesDir(null) ?: context.filesDir,
            "benchmarks",
        ).apply { mkdirs() }
        val timestamp = DateTimeFormatter.ISO_INSTANT.format(Instant.now())
            .replace(Regex("[^0-9A-Za-z.-]"), "-")
        val reportFile = File(outputDirectory, "lajur-model-benchmark-$timestamp.json")
        reportFile.writeText(json.encodeToString(report))
        println("LAJUR_MODEL_BENCHMARK_REPORT=${reportFile.absolutePath}")
        println("LAJUR_MODEL_BENCHMARK_SUMMARY=${json.encodeToString(report.summary)}")
    }

    private suspend fun evaluateCase(
        engine: LocalTransactionExtractionEngine,
        evaluationCase: ExtractionEvaluationCase,
        dataset: ExtractionEvaluationDataset,
    ): EvaluationObservation = coroutineScope {
        val peakPssKb = AtomicLong(Debug.getPss())
        val sampler = launch(Dispatchers.Default) {
            while (isActive) {
                peakPssKb.accumulateAndGet(Debug.getPss(), ::maxOf)
                delay(50)
            }
        }
        val result = try {
            engine.begin().use { request ->
                request.extract(evaluationCase.ocrText, dataset.master)
            }
        } finally {
            sampler.cancelAndJoin()
            peakPssKb.accumulateAndGet(Debug.getPss(), ::maxOf)
        }

        when (result) {
            is ExtractionRunResult.RulesOnly -> error("Model benchmark must explicitly invoke a model.")
            is ExtractionRunResult.Completed -> {
                val extraction = result.review.extraction
                EvaluationObservation(
                    caseId = evaluationCase.id,
                    expected = evaluationCase.expected,
                    actual = ObservedExtraction(
                        documentKind = extraction.documentKind,
                        date = extraction.date.value,
                        amountIdr = extraction.amountIdr.value,
                        transactionType = extraction.transactionType.value,
                        paymentMethod = extraction.paymentMethod.value,
                        category = extraction.category.value,
                        merchant = extraction.merchant.value,
                    ),
                    structuredOutput = true,
                    validatorReady = result.review.isReadyToSave,
                    coldStart = result.coldStart,
                    modelLoadMillis = result.modelLoadMillis,
                    inferenceMillis = result.inferenceMillis,
                    peakPssKb = peakPssKb.get().coerceAtMost(Int.MAX_VALUE.toLong()).toInt(),
                )
            }
            is ExtractionRunResult.Unavailable -> failedObservation(
                evaluationCase,
                result.message,
                peakPssKb,
                result.review,
            )
            is ExtractionRunResult.Failed -> failedObservation(
                evaluationCase,
                result.message,
                peakPssKb,
                result.review,
            )
        }
    }

    private fun failedObservation(
        evaluationCase: ExtractionEvaluationCase,
        message: String,
        peakPssKb: AtomicLong,
        review: com.lajur.app.scan.extraction.ValidatedTransactionExtraction,
    ) = EvaluationObservation(
        caseId = evaluationCase.id,
        expected = evaluationCase.expected,
        actual = ObservedExtraction(
            documentKind = review.extraction.documentKind,
            date = review.extraction.date.value,
            amountIdr = review.extraction.amountIdr.value,
            transactionType = review.extraction.transactionType.value,
            paymentMethod = review.extraction.paymentMethod.value,
            category = review.extraction.category.value,
            merchant = review.extraction.merchant.value,
        ),
        structuredOutput = false,
        validatorReady = review.isReadyToSave,
        peakPssKb = peakPssKb.get().coerceAtMost(Int.MAX_VALUE.toLong()).toInt(),
        error = message,
    )

    private fun loadDataset(context: Context, customPath: String?): ExtractionEvaluationDataset {
        val contents = if (customPath.isNullOrBlank()) {
            InstrumentationRegistry.getInstrumentation().context.assets
                .open("cases.synthetic.json").bufferedReader().use { it.readText() }
        } else {
            File(customPath).readText()
        }
        return json.decodeFromString(contents)
    }

    private fun sha256(file: File): String {
        val digest = MessageDigest.getInstance("SHA-256")
        file.inputStream().buffered().use { input ->
            val buffer = ByteArray(DEFAULT_BUFFER_SIZE)
            while (true) {
                val read = input.read(buffer)
                if (read < 0) break
                digest.update(buffer, 0, read)
            }
        }
        return digest.digest().joinToString("") { byte -> "%02x".format(byte) }
    }
}

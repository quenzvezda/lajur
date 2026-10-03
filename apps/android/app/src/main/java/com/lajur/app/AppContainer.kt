package com.lajur.app

import android.content.Context
import com.lajur.app.data.FinanceRepository
import com.lajur.app.data.SessionRepository
import com.lajur.app.data.FinanceOperationGate
import com.lajur.app.data.WorkbookDocumentStore
import com.lajur.app.data.WorkbookRepository
import com.lajur.app.data.local.LajurDatabase
import com.lajur.app.security.SecureSessionStore
import com.lajur.app.security.AppLockSettingsStore
import com.lajur.app.scan.AndroidImagePreprocessor
import com.lajur.app.scan.MlKitOcrEngine
import com.lajur.app.scan.SmartScanPipeline
import com.lajur.app.scan.TemporaryCaptureStore
import com.lajur.app.scan.DebugScanDiagnosticsStore
import com.lajur.app.scan.extraction.TransactionExtractionValidator
import com.lajur.app.scan.extraction.LocalTransactionExtractionEngine
import com.lajur.app.scan.llm.LiteRtLmRuntime
import com.lajur.app.sync.SyncProcessor
import com.lajur.app.sync.SyncScheduler
import java.io.File
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.serialization.json.Json

class AppContainer(context: Context) {
    private val applicationContext = context.applicationContext
    private val applicationScope = CoroutineScope(SupervisorJob() + Dispatchers.Default)
    val json = Json {
        ignoreUnknownKeys = true
        encodeDefaults = true
        explicitNulls = false
    }
    val database = LajurDatabase.create(context)
    val appLockSettingsStore = AppLockSettingsStore(context)
    val temporaryCaptureStore = TemporaryCaptureStore(context)
    val extractionValidator = TransactionExtractionValidator()
    val debugScanDiagnosticsStore = DebugScanDiagnosticsStore(applicationContext, json)
    val transactionExtractionEngine by lazy {
        val modelDirectory = File(applicationContext.filesDir, "models")
        LocalTransactionExtractionEngine(
            runtime = LiteRtLmRuntime(
                modelFile = File(modelDirectory, "lajur-extractor.litertlm"),
                cacheDirectory = File(applicationContext.cacheDir, "litert-lm"),
            ),
            validator = extractionValidator,
            json = json,
            lifecycleScope = applicationScope,
        )
    }
    val smartScanPipeline by lazy {
        SmartScanPipeline(AndroidImagePreprocessor(context), MlKitOcrEngine())
    }
    val scheduler = SyncScheduler(context)
    private val financeOperationGate = FinanceOperationGate()
    val sessionRepository = SessionRepository(
        SecureSessionStore(context, json),
        database,
        json,
        financeOperationGate,
    )
    val financeRepository = FinanceRepository(database, sessionRepository, scheduler, json, operationGate = financeOperationGate)
    val workbookRepository = WorkbookRepository(database, sessionRepository, financeOperationGate, WorkbookDocumentStore(context))
    val syncProcessor = SyncProcessor(financeRepository, sessionRepository)
}

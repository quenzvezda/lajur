package com.lajur.app

import android.content.Context
import com.lajur.app.data.FinanceRepository
import com.lajur.app.data.SessionRepository
import com.lajur.app.data.local.LajurDatabase
import com.lajur.app.security.SecureSessionStore
import com.lajur.app.security.AppLockSettingsStore
import com.lajur.app.scan.AndroidImagePreprocessor
import com.lajur.app.scan.MlKitOcrEngine
import com.lajur.app.scan.SmartScanPipeline
import com.lajur.app.scan.TemporaryCaptureStore
import com.lajur.app.sync.SyncProcessor
import com.lajur.app.sync.SyncScheduler
import kotlinx.serialization.json.Json

class AppContainer(context: Context) {
    val json = Json {
        ignoreUnknownKeys = true
        encodeDefaults = true
        explicitNulls = false
    }
    val database = LajurDatabase.create(context)
    val appLockSettingsStore = AppLockSettingsStore(context)
    val temporaryCaptureStore = TemporaryCaptureStore(context)
    val smartScanPipeline by lazy {
        SmartScanPipeline(AndroidImagePreprocessor(context), MlKitOcrEngine())
    }
    val scheduler = SyncScheduler(context)
    val sessionRepository = SessionRepository(
        SecureSessionStore(context, json),
        database,
        json,
    )
    val financeRepository = FinanceRepository(database, sessionRepository, scheduler, json)
    val syncProcessor = SyncProcessor(financeRepository, sessionRepository)
}

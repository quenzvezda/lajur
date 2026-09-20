package com.lajur.app.sync

import android.content.Context
import androidx.work.CoroutineWorker
import androidx.work.WorkerParameters
import com.lajur.app.LajurApplication

class SyncWorker(
    appContext: Context,
    parameters: WorkerParameters,
) : CoroutineWorker(appContext, parameters) {
    override suspend fun doWork(): Result {
        val container = (applicationContext as LajurApplication).container
        return when (container.syncProcessor.run()) {
            SyncRunResult.RETRY -> Result.retry()
            else -> Result.success()
        }
    }
}

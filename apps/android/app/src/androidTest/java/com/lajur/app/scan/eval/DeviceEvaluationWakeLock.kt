package com.lajur.app.scan.eval

import android.content.Context
import android.os.PowerManager

/** Test-only and time-bounded: screen-off suspend must not masquerade as model latency. */
class DeviceEvaluationWakeLock(context: Context) : AutoCloseable {
    private val lock = context.getSystemService(PowerManager::class.java)
        .newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "Lajur:LocalModelEvaluation")

    fun acquire() { lock.acquire(600_000L) }

    override fun close() { if (lock.isHeld) lock.release() }
}

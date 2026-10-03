package com.lajur.app.data

import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock

/** Local entry never waits for ordinary network sync. Import excludes both. */
class FinanceOperationGate {
    private val remote = Mutex()
    private val local = Mutex()

    suspend fun <T> localChange(action: suspend () -> T): T = local.withLock { action() }

    suspend fun <T> remoteChange(action: suspend () -> T): T = remote.withLock { action() }

    suspend fun <T> importWorkbook(hasPending: suspend () -> Boolean, action: suspend () -> T): T =
        remote.withLock {
            local.withLock {
                check(!hasPending()) { "Impor diblokir: sinkronkan atau selesaikan seluruh transaksi pending/gagal terlebih dahulu." }
                action()
            }
        }
}

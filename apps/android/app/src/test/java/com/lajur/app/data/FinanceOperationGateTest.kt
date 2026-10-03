package com.lajur.app.data

import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.launch
import kotlinx.coroutines.test.runTest
import kotlinx.coroutines.test.runCurrent
import org.junit.Assert.*
import org.junit.Test

@OptIn(kotlinx.coroutines.ExperimentalCoroutinesApi::class)
class FinanceOperationGateTest {
    @Test fun `pending outbox blocks import without running replacement`() = runTest {
        val gate = FinanceOperationGate()
        var imported = false
        val error = runCatching { gate.importWorkbook({ true }) { imported = true } }.exceptionOrNull()
        assertTrue(error is IllegalStateException)
        assertFalse(imported)
    }

    @Test fun `ordinary sync does not delay offline entry but import waits for sync`() = runTest {
        val gate = FinanceOperationGate()
        val release = CompletableDeferred<Unit>()
        var imported = false
        launch { gate.remoteChange { release.await() } }
        runCurrent()
        gate.localChange { /* No dependency on network. */ }
        launch { gate.importWorkbook({ false }) { imported = true } }
        runCurrent()
        assertFalse(imported)
        release.complete(Unit)
        runCurrent()
        assertTrue(imported)
    }

    @Test fun `import checks actual outbox after concurrent local entry commits`() = runTest {
        val gate = FinanceOperationGate()
        val release = CompletableDeferred<Unit>()
        var pending = false
        var imported = false
        launch { gate.localChange { release.await(); pending = true } }
        runCurrent()
        val attempt = launch { runCatching { gate.importWorkbook({ pending }) { imported = true } } }
        runCurrent()
        release.complete(Unit)
        attempt.join()
        assertFalse(imported)
    }

    @Test fun `import excludes local entry and releases gates on failure`() = runTest {
        val gate = FinanceOperationGate()
        val release = CompletableDeferred<Unit>()
        var saved = false
        launch { runCatching { gate.importWorkbook({ false }) { release.await(); error("invalid workbook") } } }
        runCurrent()
        launch { gate.localChange { saved = true } }
        runCurrent()
        assertFalse(saved)
        release.complete(Unit)
        runCurrent()
        assertTrue(saved)
        gate.remoteChange { /* failure did not leak the lock */ }
    }
}

package com.lajur.app.scan

import com.lajur.app.domain.MasterCategory
import com.lajur.app.domain.MasterData
import com.lajur.app.scan.extraction.ExtractionRunResult
import com.lajur.app.scan.extraction.LocalTransactionExtractionEngine
import com.lajur.app.scan.extraction.TransactionExtractionValidator
import com.lajur.app.scan.llm.LocalModelRequest
import com.lajur.app.scan.llm.LocalModelResponse
import com.lajur.app.scan.llm.LocalTextModelRuntime
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.NonCancellable
import kotlinx.coroutines.delay
import kotlinx.coroutines.test.TestScope
import kotlinx.coroutines.test.advanceTimeBy
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import kotlinx.coroutines.withContext
import kotlinx.serialization.json.Json
import org.junit.Assert.*
import org.junit.Test

@OptIn(ExperimentalCoroutinesApi::class)
class ScanReviewSessionTest {
    private val master = MasterData(2026, "1", listOf("Pengeluaran", "Pemasukan"),
        listOf("Bank", "Cash"), listOf(MasterCategory("Kebutuhan", "Pengeluaran")))

    @Test fun showingOcrProducesAReviewWithoutLoadingTheModel() = runTest {
        val runtime = Runtime()
        val session = session(runtime)
        session.show(output(), master)
        runCurrent()
        val snapshot = requireNotNull(session.state.value)
        assertTrue(snapshot.result is ExtractionRunResult.RulesOnly)
        assertEquals(ScanSuggestionState.NOT_REQUESTED, snapshot.suggestions)
        assertEquals(68820L, snapshot.review.extraction.amountIdr.value)
        assertEquals("2026-09-20", snapshot.review.extraction.date.value)
        assertEquals("Bank", snapshot.review.extraction.paymentMethod.value)
        assertEquals(0, runtime.loads)
        assertEquals(0, runtime.generations)
        assertNull(snapshot.review.extraction.category.value)
    }

    @Test fun modelIsExplicitAndDraftRemainsAvailableWhileWaiting() = runTest {
        val runtime = Runtime()
        val session = session(runtime)
        session.show(output(), master)
        session.requestSuggestions()
        session.requestSuggestions()
        runCurrent()
        assertEquals(ScanSuggestionState.RUNNING, session.state.value?.suggestions)
        assertEquals(68820L, session.state.value?.review?.extraction?.amountIdr?.value)
        assertEquals(1, runtime.loads)
        assertEquals(1, runtime.generations)
        advanceTimeBy(100)
        runCurrent()
        assertEquals(ScanSuggestionState.COMPLETE, session.state.value?.suggestions)
        assertEquals("Kebutuhan", session.state.value?.review?.extraction?.category?.value)
        assertEquals(68820L, session.state.value?.review?.extraction?.amountIdr?.value)
    }

    @Test fun cancellationCannotPromoteLateNativeSuggestions() = runTest {
        val runtime = Runtime(ignoreCancellation = true)
        val session = session(runtime)
        session.show(output(), master)
        session.requestSuggestions()
        runCurrent()
        session.cancelSuggestions()
        assertEquals(ScanSuggestionState.CANCELLED, session.state.value?.suggestions)
        advanceTimeBy(100)
        runCurrent()
        assertEquals(ScanSuggestionState.CANCELLED, session.state.value?.suggestions)
        assertNull(session.state.value?.review?.extraction?.category?.value)
        assertEquals(68820L, session.state.value?.review?.extraction?.amountIdr?.value)
    }

    @Test fun replacedOrDiscardedScanCannotBeOverwrittenByAnOldRun() = runTest {
        val runtime = Runtime(ignoreCancellation = true)
        val session = session(runtime)
        session.show(output(), master)
        session.requestSuggestions()
        runCurrent()
        session.show(output("TOTAL Rp 12.000"), master)
        advanceTimeBy(100)
        runCurrent()
        assertEquals(12000L, session.state.value?.review?.extraction?.amountIdr?.value)
        assertEquals(ScanSuggestionState.NOT_REQUESTED, session.state.value?.suggestions)
        session.requestSuggestions()
        runCurrent()
        session.clear()
        advanceTimeBy(100)
        runCurrent()
        assertNull(session.state.value)
    }

    @Test fun failedModelPreservesOcrAndAllowsRetry() = runTest {
        val runtime = Runtime(response = "broken JSON")
        val session = session(runtime)
        session.show(output(), master)
        session.requestSuggestions()
        runCurrent(); advanceTimeBy(100); runCurrent()
        assertEquals(ScanSuggestionState.FAILED, session.state.value?.suggestions)
        assertEquals(68820L, session.state.value?.review?.extraction?.amountIdr?.value)
        session.requestSuggestions()
        runCurrent()
        assertEquals(ScanSuggestionState.RUNNING, session.state.value?.suggestions)
    }

    @Test fun latestMasterRevalidatesSuggestionsBeforeTheyReachTheEditor() = runTest {
        val session = session(Runtime())
        session.show(output(), master)
        session.requestSuggestions()
        runCurrent()
        session.revalidate(master.copy(categories = emptyList()))
        advanceTimeBy(100)
        runCurrent()
        assertNull(session.state.value?.review?.extraction?.category?.value)
        assertFalse(requireNotNull(session.state.value).review.isReadyToSave)
    }

    private fun TestScope.session(runtime: Runtime): ScanReviewSession {
        val validator = TransactionExtractionValidator()
        return ScanReviewSession(LocalTransactionExtractionEngine(runtime, validator,
            json = Json { ignoreUnknownKeys = true }, lifecycleScope = backgroundScope), validator, backgroundScope)
    }

    private fun output(text: String = "20/09/2026\nSUBTOTAL 65.000\nTOTAL RP 68.820\nKEMBALI 0\nQRIS") = SmartScanOutput(
        ScanImageRequest("content://dummy/" + text.hashCode(), ScanImageSource.GALLERY),
        ScanImageMetadata(493, 785, 493, 785, 1), OcrResult(text, emptyList(), 10), emptyList(),
    )

    private class Runtime(
        val ignoreCancellation: Boolean = false,
        val response: String = """{"schemaVersion":1,"transactionType":null,"paymentMethod":null,"category":"Kebutuhan","merchant":null,"description":"Belanja"}""",
    ) : LocalTextModelRuntime {
        var loads = 0
        var generations = 0
        override suspend fun load() { loads++ }
        override suspend fun generate(request: LocalModelRequest): LocalModelResponse {
            generations++
            if (ignoreCancellation) withContext(NonCancellable) { delay(100) } else delay(100)
            return LocalModelResponse(response)
        }
        override suspend fun unload() {}
    }
}

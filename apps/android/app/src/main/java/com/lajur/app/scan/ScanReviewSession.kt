package com.lajur.app.scan

import com.lajur.app.domain.MasterData
import com.lajur.app.scan.extraction.ExtractionRunResult
import com.lajur.app.scan.extraction.LocalTransactionExtractionEngine
import com.lajur.app.scan.extraction.TransactionExtractionValidator
import com.lajur.app.scan.extraction.ValidatedTransactionExtraction
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch

enum class ScanSuggestionState { NOT_REQUESTED, RUNNING, COMPLETE, UNAVAILABLE, FAILED, CANCELLED }

data class ScanReviewSnapshot(
    val output: SmartScanOutput,
    val result: ExtractionRunResult,
    val review: ValidatedTransactionExtraction,
    val suggestions: ScanSuggestionState = ScanSuggestionState.NOT_REQUESTED,
)

/** Owns one in-memory review. Results from cancelled/replaced scans cannot cross this seam. */
class ScanReviewSession(
    private val engine: LocalTransactionExtractionEngine,
    private val validator: TransactionExtractionValidator,
    private val scope: CoroutineScope,
) {
    private val mutableState = MutableStateFlow<ScanReviewSnapshot?>(null)
    val state = mutableState.asStateFlow()
    private var suggestionJob: Job? = null
    private var generation = 0L
    private var currentMaster: MasterData? = null

    fun show(output: SmartScanOutput, master: MasterData?) {
        clear()
        currentMaster = master
        val result = engine.reviewOcr(output.ocr, master)
        mutableState.value = ScanReviewSnapshot(output, result, result.review)
    }

    fun revalidate(master: MasterData?) {
        currentMaster = master
        mutableState.value?.let { snapshot ->
            mutableState.value = snapshot.copy(review = validator.validate(snapshot.result.review.extraction, master))
        }
    }

    fun requestSuggestions() {
        val snapshot = mutableState.value ?: return
        if (snapshot.suggestions == ScanSuggestionState.RUNNING ||
            snapshot.suggestions == ScanSuggestionState.COMPLETE) return
        val token = ++generation
        val modelMaster = currentMaster
        mutableState.value = snapshot.copy(suggestions = ScanSuggestionState.RUNNING)
        suggestionJob = scope.launch {
            try {
                val result = engine.begin().use { it.extract(snapshot.output.ocr, modelMaster) }
                if (!isActive || token != generation) return@launch
                mutableState.value = snapshot.copy(
                    result = result,
                    review = validator.validate(result.review.extraction, currentMaster),
                    suggestions = when (result) {
                        is ExtractionRunResult.Completed -> ScanSuggestionState.COMPLETE
                        is ExtractionRunResult.Unavailable -> ScanSuggestionState.UNAVAILABLE
                        is ExtractionRunResult.Failed -> ScanSuggestionState.FAILED
                        is ExtractionRunResult.RulesOnly -> error("A requested model run cannot be rules-only.")
                    },
                )
            } catch (error: CancellationException) {
                throw error
            } catch (error: Exception) {
                if (token == generation && isActive) {
                    val review = validator.validate(snapshot.result.review.extraction, currentMaster)
                    val failed = ExtractionRunResult.Failed(
                        "Saran AI belum dapat diproses. Anda tetap bisa melanjutkan draft.",
                        review, snapshot.result.trace.copy(failure = error.message),
                    )
                    mutableState.value = snapshot.copy(result = failed, review = review,
                        suggestions = ScanSuggestionState.FAILED)
                }
            } finally {
                if (token == generation) suggestionJob = null
            }
        }
    }

    fun cancelSuggestions() {
        if (mutableState.value?.suggestions != ScanSuggestionState.RUNNING) return
        ++generation
        suggestionJob?.cancel()
        suggestionJob = null
        mutableState.value = mutableState.value?.copy(suggestions = ScanSuggestionState.CANCELLED)
    }

    fun clear() {
        ++generation
        suggestionJob?.cancel()
        suggestionJob = null
        mutableState.value = null
    }
}

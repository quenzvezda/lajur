package com.lajur.app.scan.eval

import com.lajur.app.domain.MasterData
import com.lajur.app.scan.extraction.DocumentKind
import kotlinx.serialization.Serializable

@Serializable
data class ExtractionEvaluationDataset(
    val schemaVersion: Int,
    val name: String,
    val description: String,
    val master: MasterData,
    val cases: List<ExtractionEvaluationCase>,
)

@Serializable
data class ExtractionEvaluationCase(
    val id: String,
    val sourceKind: String,
    val ocrText: String,
    val expected: ExpectedExtraction,
)

@Serializable
data class ExpectedExtraction(
    val documentKind: DocumentKind,
    val date: String?,
    val amountIdr: Long?,
    val transactionType: String?,
    val paymentMethod: String?,
    val category: String?,
    val merchant: String? = null,
)

@Serializable
data class ObservedExtraction(
    val documentKind: DocumentKind? = null,
    val date: String? = null,
    val amountIdr: Long? = null,
    val transactionType: String? = null,
    val paymentMethod: String? = null,
    val category: String? = null,
    val merchant: String? = null,
)

@Serializable
data class EvaluationObservation(
    val caseId: String,
    val expected: ExpectedExtraction,
    val actual: ObservedExtraction? = null,
    val structuredOutput: Boolean,
    val validatorReady: Boolean,
    val coldStart: Boolean = false,
    val modelLoadMillis: Long? = null,
    val inferenceMillis: Long? = null,
    val peakPssKb: Int? = null,
    val error: String? = null,
)

@Serializable
data class RateMetric(
    val numerator: Int,
    val denominator: Int,
    val rate: Double?,
)

@Serializable
data class LatencySummary(
    val samples: Int,
    val meanMillis: Double?,
    val p50Millis: Long?,
    val p95Millis: Long?,
)

@Serializable
data class EvaluationSummary(
    val totalCases: Int,
    val structuredOutput: RateMetric,
    val validatorReady: RateMetric,
    val documentKindExact: RateMetric,
    val dateExact: RateMetric,
    val amountExact: RateMetric,
    val transactionTypeExact: RateMetric,
    val paymentMethodExact: RateMetric,
    val categoryAccuracyWhenSuggested: RateMetric,
    val categoryCoverage: RateMetric,
    val categoryFalsePositive: RateMetric,
    val merchantExact: RateMetric,
    val inferenceLatency: LatencySummary,
    val coldLoadLatency: LatencySummary,
    val maxObservedPssKb: Int?,
)

object ModelEvaluation {
    fun summarize(observations: List<EvaluationObservation>): EvaluationSummary {
        val completed = observations.filter { it.actual != null }
        val expectedCategories = observations.filter { it.expected.category != null }
        val suggestedForExpected = expectedCategories.filter { it.actual?.category != null }
        val expectedMissingCategory = observations.filter { it.expected.category == null }

        return EvaluationSummary(
            totalCases = observations.size,
            structuredOutput = rate(observations.count(EvaluationObservation::structuredOutput), observations.size),
            validatorReady = rate(observations.count(EvaluationObservation::validatorReady), observations.size),
            documentKindExact = exact(completed) { it.expected.documentKind to it.actual?.documentKind },
            dateExact = exact(completed) { it.expected.date to it.actual?.date },
            amountExact = exact(completed) { it.expected.amountIdr to it.actual?.amountIdr },
            transactionTypeExact = exact(completed) {
                it.expected.transactionType to it.actual?.transactionType
            },
            paymentMethodExact = exact(completed) {
                it.expected.paymentMethod to it.actual?.paymentMethod
            },
            categoryAccuracyWhenSuggested = rate(
                suggestedForExpected.count { it.expected.category == it.actual?.category },
                suggestedForExpected.size,
            ),
            categoryCoverage = rate(
                expectedCategories.count { it.actual?.category != null },
                expectedCategories.size,
            ),
            categoryFalsePositive = rate(
                expectedMissingCategory.count { it.actual?.category != null },
                expectedMissingCategory.size,
            ),
            merchantExact = exact(completed) { it.expected.merchant to it.actual?.merchant },
            inferenceLatency = latency(completed.mapNotNull(EvaluationObservation::inferenceMillis)),
            coldLoadLatency = latency(
                completed.filter(EvaluationObservation::coldStart)
                    .mapNotNull(EvaluationObservation::modelLoadMillis),
            ),
            maxObservedPssKb = observations.mapNotNull(EvaluationObservation::peakPssKb).maxOrNull(),
        )
    }

    private fun exact(
        observations: List<EvaluationObservation>,
        values: (EvaluationObservation) -> Pair<Any?, Any?>,
    ): RateMetric = rate(
        observations.count { values(it).let { pair -> pair.first == pair.second } },
        observations.size,
    )

    private fun rate(numerator: Int, denominator: Int) = RateMetric(
        numerator = numerator,
        denominator = denominator,
        rate = if (denominator == 0) null else numerator.toDouble() / denominator,
    )

    private fun latency(values: List<Long>): LatencySummary {
        val sorted = values.sorted()
        return LatencySummary(
            samples = sorted.size,
            meanMillis = sorted.takeIf(List<Long>::isNotEmpty)?.average(),
            p50Millis = percentile(sorted, 0.50),
            p95Millis = percentile(sorted, 0.95),
        )
    }

    private fun percentile(sorted: List<Long>, percentile: Double): Long? {
        if (sorted.isEmpty()) return null
        val index = kotlin.math.ceil(percentile * sorted.size).toInt().coerceIn(1, sorted.size) - 1
        return sorted[index]
    }
}

@Serializable
data class ModelEvaluationReport(
    val reportVersion: Int = 1,
    val createdAtUtc: String,
    val datasetName: String,
    val datasetSchemaVersion: Int,
    val modelFileName: String,
    val modelSha256: String,
    val runtime: String,
    val backend: String,
    val device: String,
    val androidApi: Int,
    val observations: List<EvaluationObservation>,
    val summary: EvaluationSummary,
)

package com.lajur.app.scan.eval

import com.lajur.app.scan.extraction.DocumentKind
import kotlinx.serialization.json.Json
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class ModelEvaluationTest {
    @Test
    fun `summary separates category accuracy coverage and false positives`() {
        val observations = listOf(
            observation("one", category = "Jajan", actualCategory = "Jajan", inference = 100),
            observation("two", category = "Makanan-Jadi", actualCategory = null, inference = 200),
            observation("three", category = null, actualCategory = "Jajan", inference = 300),
        )

        val summary = ModelEvaluation.summarize(observations)

        assertEquals(1.0, summary.categoryAccuracyWhenSuggested.rate!!, 0.0)
        assertEquals(0.5, summary.categoryCoverage.rate!!, 0.0)
        assertEquals(1.0, summary.categoryFalsePositive.rate!!, 0.0)
        assertEquals(200.0, summary.inferenceLatency.meanMillis!!, 0.0)
        assertEquals(200L, summary.inferenceLatency.p50Millis)
        assertEquals(300L, summary.inferenceLatency.p95Millis)
    }

    @Test
    fun `empty observations produce null rates instead of misleading zero`() {
        val summary = ModelEvaluation.summarize(emptyList())

        assertEquals(0, summary.totalCases)
        assertNull(summary.structuredOutput.rate)
        assertNull(summary.inferenceLatency.meanMillis)
        assertNull(summary.maxObservedPssKb)
    }

    @Test
    fun `committed synthetic dataset is valid and uniquely identified`() {
        val contents = checkNotNull(javaClass.getResourceAsStream("/cases.synthetic.json"))
            .bufferedReader()
            .use { it.readText() }
        val dataset = Json.decodeFromString<ExtractionEvaluationDataset>(contents)

        assertEquals(1, dataset.schemaVersion)
        assertTrue(dataset.cases.isNotEmpty())
        assertEquals(dataset.cases.size, dataset.cases.map { it.id }.distinct().size)
    }

    private fun observation(
        id: String,
        category: String?,
        actualCategory: String?,
        inference: Long,
    ) = EvaluationObservation(
        caseId = id,
        expected = ExpectedExtraction(
            documentKind = DocumentKind.RECEIPT,
            date = "2026-09-20",
            amountIdr = 15_000,
            transactionType = "Pengeluaran",
            paymentMethod = "Cash",
            category = category,
        ),
        actual = ObservedExtraction(
            documentKind = DocumentKind.RECEIPT,
            date = "2026-09-20",
            amountIdr = 15_000,
            transactionType = "Pengeluaran",
            paymentMethod = "Cash",
            category = actualCategory,
        ),
        structuredOutput = true,
        validatorReady = actualCategory != null,
        inferenceMillis = inference,
    )
}

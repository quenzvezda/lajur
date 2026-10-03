package com.lajur.app.scan.extraction

import com.lajur.app.scan.OcrResult
import com.lajur.app.scan.OcrTextBlock
import com.lajur.app.scan.eval.ExtractionEvaluationDataset
import kotlinx.serialization.json.Json
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class EvidenceTransactionExtractorTest {
    private val extractor = EvidenceTransactionExtractor()

    @Test
    fun `synthetic dataset dates and labelled amounts survive without model suggestions`() {
        val dataset = javaClass.classLoader!!.getResourceAsStream("cases.synthetic.json")!!.bufferedReader().use {
            Json { ignoreUnknownKeys = true }.decodeFromString<ExtractionEvaluationDataset>(it.readText())
        }
        dataset.cases.forEach { case ->
            val result = extractor.extract(case.ocrText, dataset.master)
            assertEquals(case.id, case.expected.date, result.date.value)
            assertEquals(case.id, case.expected.amountIdr, result.amountIdr.value)
        }
    }

    @Test
    fun `reconstructs receipt columns and extracts labelled total not largest decoy`() {
        val ocr = OcrResult("TOTAL\nSUBTOTAL\nRP 68.820\n65.000", emptyList(), 0, lines = listOf(
            box("65.000", 300, 40), box("RP 68.820", 300, 80), box("999.000", 300, 100),
            box("TOTAL", 10, 81), box("SUBTOTAL", 10, 40), box("TUNAI", 10, 100),
            box("20/09/2026 14:23", 10, 10), box("PPN 6.820", 10, 60),
        ))
        val text = OcrReadingOrder.text(ocr)
        val result = extractor.extract(text, null)
        assertEquals("2026-09-20", result.date.value)
        assertEquals(68_820L, result.amountIdr.value)
        assertEquals("TOTAL RP 68.820", result.amountIdr.evidence)
        assertEquals(ExtractionSource.OCR_RULES, result.amountIdr.source)
    }

    @Test
    fun `invalid dates decimals subtotal balance and unpaired values do not become financial fields`() {
        val result = extractor.extract("31/02/2026\nBerlaku 20/09/2026\nSUBTOTAL 65.000\nSALDO 99.000\nTOTAL 68.820,50\nTOTAL\n123.456", null)
        assertNull(result.date.value)
        assertNull(result.amountIdr.value)
    }

    @Test
    fun `conflicting explicit dates and totals remain missing despite model guesses`() {
        val rules = extractor.extract("20/09/2026\n21/09/2026\nTOTAL 68.820\nTOTAL 70.000", null)
        val model = TransactionExtraction(date = ExtractedField("2026-09-20"), amountIdr = ExtractedField(68_820L))
        val result = extractor.merge(model, rules)
        assertNull(result.date.value)
        assertNull(result.amountIdr.value)
        assertEquals(listOf("2026-09-20", "2026-09-21"), result.date.alternatives)
        assertEquals(listOf(68_820L, 70_000L), result.amountIdr.alternatives)
    }

    @Test
    fun `repeated dates deduplicate and disagreeing model remains an alternative`() {
        val rules = extractor.extract("20/09/2026 14:23\n2026-09-20\nTOTAL: RP 68.820", null)
        val result = extractor.merge(TransactionExtraction(amountIdr = ExtractedField(65_000L)), rules)
        assertEquals("2026-09-20", result.date.value)
        assertEquals(68_820L, result.amountIdr.value)
        assertEquals(listOf(65_000L), result.amountIdr.alternatives)
        assertEquals(ExtractionConfidence.MEDIUM, result.amountIdr.confidence)
    }

    @Test
    fun `no geometry preserves input and adjacent rows never join`() {
        assertEquals("TOTAL\n68.820", OcrReadingOrder.text(OcrResult("TOTAL\n68.820", emptyList(), 0)))
        val ocr = OcrResult("", emptyList(), 0, lines = listOf(box("TOTAL", 10, 20), box("68.820", 300, 40)))
        assertNull(extractor.extract(OcrReadingOrder.text(ocr), null).amountIdr.value)
    }

    private fun box(text: String, left: Int, top: Int) = OcrTextBlock(text, left, top, left + 120, top + 10)
}

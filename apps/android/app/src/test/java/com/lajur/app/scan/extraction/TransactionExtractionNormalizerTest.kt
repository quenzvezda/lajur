package com.lajur.app.scan.extraction

import com.lajur.app.domain.MasterData
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class TransactionExtractionNormalizerTest {
    private val normalizer = TransactionExtractionNormalizer()

    @Test
    fun `normalizes common Indonesian numeric dates to ISO`() {
        val normalized = normalizer.normalize(
            extraction(date = "20/09/2026"),
            ocrText = "20/09/2026 14:23",
            master = master(),
        )

        assertEquals("2026-09-20", normalized.date.value)
        assertEquals("20/09/2026", normalized.date.evidence)
    }

    @Test
    fun `QRIS evidence maps payment to Bank only when Bank exists in Master`() {
        val normalized = normalizer.normalize(
            extraction(paymentMethod = "Cash"),
            ocrText = "TOTAL RP 68.820\nQRIS 68.820\nKEMBALI 0",
            master = master(),
        )

        assertEquals("Bank", normalized.paymentMethod.value)
        assertEquals("QRIS", normalized.paymentMethod.evidence)
        assertEquals(ExtractionConfidence.HIGH, normalized.paymentMethod.confidence)

        val withoutBank = normalizer.normalize(
            extraction(paymentMethod = "Cash"),
            ocrText = "QRIS 68.820",
            master = master().copy(paymentMethods = listOf("Cash", "Debit")),
        )
        assertNull(withoutBank.paymentMethod.value)
    }

    @Test
    fun `receipt structure labels are not descriptions`() {
        listOf("KEMBALI", "TOTAL", "SUBTOTAL", "PPN").forEach { label ->
            val normalized = normalizer.normalize(
                extraction(description = label),
                ocrText = "TOTAL 68.820\nKEMBALI 0",
                master = master(),
            )

            assertNull(normalized.description.value)
            assertEquals(ExtractionConfidence.MISSING, normalized.description.confidence)
        }
    }

    private fun extraction(
        date: String = "2026-09-20",
        paymentMethod: String? = "Cash",
        description: String? = "Belanja minimarket",
    ) = TransactionExtraction(
        documentKind = DocumentKind.RECEIPT,
        date = field(date),
        amountIdr = field(68_820L),
        transactionType = field("Pengeluaran"),
        paymentMethod = field(paymentMethod),
        category = field("Makanan-Jadi"),
        merchant = field("MINIMARKET NUSANTARA"),
        description = field(description),
    )

    private fun master() = MasterData(
        workbookYear = 2026,
        schemaVersion = "1",
        transactionTypes = listOf("Pengeluaran", "Pemasukan"),
        paymentMethods = listOf("Cash", "Debit", "Bank"),
        categories = emptyList(),
    )

    private fun <T> field(value: T?) = ExtractedField(
        value = value,
        confidence = if (value == null) ExtractionConfidence.MISSING else ExtractionConfidence.LOW,
        evidence = value?.toString(),
    )
}

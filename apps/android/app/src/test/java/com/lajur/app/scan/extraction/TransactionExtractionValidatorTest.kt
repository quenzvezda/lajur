package com.lajur.app.scan.extraction

import com.lajur.app.domain.MasterCategory
import com.lajur.app.domain.MasterData
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class TransactionExtractionValidatorTest {
    private val validator = TransactionExtractionValidator()

    @Test
    fun `valid extraction canonicalizes Master values and becomes a transaction draft`() {
        val result = validator.validate(
            validExtraction().copy(
                transactionType = field("pengeluaran"),
                paymentMethod = field("cash"),
                category = field("jajan"),
            ),
            master(),
        )

        assertTrue(result.isReadyToSave)
        assertEquals("Pengeluaran", result.extraction.transactionType.value)
        assertEquals("Cash", result.extraction.paymentMethod.value)
        assertEquals("Jajan", result.extraction.category.value)
        assertEquals(15_000L, result.toTransactionDraftOrNull()?.amountIdr)
    }

    @Test
    fun `invalid date and non-positive amount are blocking and become missing`() {
        val result = validator.validate(
            validExtraction().copy(
                date = field("20/09/2026"),
                amountIdr = field(0L),
            ),
            master(),
        )

        assertFalse(result.isReadyToSave)
        assertNull(result.extraction.date.value)
        assertNull(result.extraction.amountIdr.value)
        assertTrue(result.issues.any { it.code == "invalid_date" })
        assertTrue(result.issues.any { it.code == "invalid_amount" })
    }

    @Test
    fun `out of Master values are rejected instead of invented`() {
        val result = validator.validate(
            validExtraction().copy(
                paymentMethod = field("Cloud Wallet"),
                category = field("Liburan ke Mars"),
            ),
            master(),
        )

        assertFalse(result.isReadyToSave)
        assertNull(result.extraction.paymentMethod.value)
        assertNull(result.extraction.category.value)
        assertTrue(result.issues.count { it.code == "out_of_master" } >= 2)
    }

    @Test
    fun `category must belong to the canonical transaction type`() {
        val result = validator.validate(
            validExtraction().copy(category = field("Gaji")),
            master(),
        )

        assertNull(result.extraction.category.value)
        assertFalse(result.isReadyToSave)
    }

    @Test
    fun `missing category remains null and blocks save`() {
        val result = validator.validate(
            validExtraction().copy(category = ExtractedField()),
            master(),
        )

        assertNull(result.extraction.category.value)
        assertEquals(ExtractionConfidence.MISSING, result.extraction.category.confidence)
        assertFalse(result.isReadyToSave)
        assertTrue(result.issues.any { it.field == ExtractionFieldName.CATEGORY && it.code == "missing_required" })
    }

    @Test
    fun `alternatives are canonicalized filtered and deduplicated`() {
        val result = validator.validate(
            validExtraction().copy(
                paymentMethod = ExtractedField(
                    value = "Cash",
                    alternatives = listOf("cash", "Debit", "Unknown", "Debit"),
                    confidence = ExtractionConfidence.LOW,
                ),
            ),
            master(),
        )

        assertEquals(listOf("Debit"), result.extraction.paymentMethod.alternatives)
    }

    @Test
    fun `value marked missing is downgraded to low with a warning`() {
        val result = validator.validate(
            validExtraction().copy(
                amountIdr = ExtractedField(value = 15_000, confidence = ExtractionConfidence.MISSING),
                merchant = ExtractedField(value = "Es Jeruk", confidence = ExtractionConfidence.MISSING),
            ),
            master(),
        )

        assertEquals(ExtractionConfidence.LOW, result.extraction.amountIdr.confidence)
        assertEquals(ExtractionConfidence.LOW, result.extraction.merchant.confidence)
        assertTrue(
            result.issues.count { it.code == "confidence_mismatch" } == 2,
        )
    }

    @Test
    fun `missing Master data blocks constrained fields`() {
        val result = validator.validate(validExtraction(), null)

        assertFalse(result.isReadyToSave)
        assertTrue(result.issues.any { it.code == "master_unavailable" })
    }

    @Test
    fun `schema serializes confidence and document kind as stable lowercase values`() {
        val encoded = Json.encodeToString(validExtraction())

        assertTrue(encoded.contains("\"documentKind\":\"payment_success\""))
        assertTrue(encoded.contains("\"confidence\":\"high\""))
    }

    private fun validExtraction() = TransactionExtraction(
        documentKind = DocumentKind.PAYMENT_SUCCESS,
        date = field("2026-09-20"),
        amountIdr = field(15_000L),
        transactionType = field("Pengeluaran"),
        paymentMethod = field("Cash"),
        category = field("Jajan"),
        merchant = field("Es Jeruk"),
        description = field("Beli es jeruk"),
    )

    private fun master() = MasterData(
        workbookYear = 2026,
        schemaVersion = "1",
        transactionTypes = listOf("Pengeluaran", "Pemasukan"),
        paymentMethods = listOf("Cash", "Debit"),
        categories = listOf(
            MasterCategory("Jajan", "Pengeluaran"),
            MasterCategory("Makanan-Jadi", "Pengeluaran"),
            MasterCategory("Gaji", "Pemasukan"),
        ),
    )

    private fun <T> field(value: T) = ExtractedField(
        value = value,
        confidence = ExtractionConfidence.HIGH,
        evidence = value.toString(),
    )
}

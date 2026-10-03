package com.lajur.app.ui

import androidx.compose.ui.text.AnnotatedString
import com.lajur.app.domain.MasterCategory
import com.lajur.app.domain.MasterData
import org.junit.Assert.*
import org.junit.Test

class TransactionEditorInputTest {
    private val master = MasterData(2026, "1", listOf("Pengeluaran", "Pemasukan"),
        listOf("Bank", "Cash"), listOf(MasterCategory("Makanan-Jadi", "Pengeluaran"), MasterCategory("Gaji", "Pemasukan")))
    private val valid = TransactionEditorInput("2026-09-20", "Pengeluaran", "Bank", "Makanan-Jadi", "68820", "")

    @Test fun validInputProducesAnIntegerDraft() {
        assertTrue(valid.errors(master, null).isEmpty())
        assertEquals(68820L, valid.toDraft(master, null)?.amountIdr)
        assertNull(valid.toDraft(master, null)?.description)
    }

    @Test fun requiredAndMasterInvalidChoicesCannotSave() {
        for (input in listOf(valid.copy(date = ""), valid.copy(date = "2026-09-31"),
            valid.copy(type = "Tidak ada"), valid.copy(payment = "QRIS"), valid.copy(category = "Gaji"),
            valid.copy(amount = "0"), valid.copy(amount = "-1"), valid.copy(amount = "1.25"),
            valid.copy(amount = "9223372036854775808"))) {
            assertFalse(input.errors(master, null).isEmpty())
            assertNull(input.toDraft(master, null))
        }
        assertNull(valid.toDraft(null, null))
        assertNull(valid.copy(date = "2027-01-01").toDraft(master, 2026))
        assertNotNull(valid.copy(date = "2026-10-01").toDraft(master, 2026))
    }

    @Test fun onlyPaymentCanBeEmptyAndWhitespaceIsCanonicalized() {
        for (payment in listOf("", "   ")) {
            val input = valid.copy(payment = payment)
            assertTrue(input.errors(master, null).isEmpty())
            assertEquals("", input.toDraft(master, null)?.paymentMethod)
        }
        assertNotNull(valid.copy(payment = "", type = "Pemasukan", category = "Gaji").toDraft(master, null))
        for (input in listOf(valid.copy(date = ""), valid.copy(type = ""), valid.copy(category = ""), valid.copy(amount = "")))
            assertNull(input.copy(payment = "").toDraft(master, null))
        assertNull(valid.copy(payment = "Unknown method").toDraft(master, null))
        assertNull(valid.copy(payment = "").toDraft(null, null))
    }

    @Test fun groupingDoesNotChangeTheRawAmountAndCursorMappingRoundTrips() {
        for (raw in listOf("", "0", "10", "100", "1000", "68820", "1000000", "9223372036854775807")) {
            val transformed = IdrGroupingTransformation.filter(AnnotatedString(raw))
            assertEquals(raw, transformed.text.text.replace(".", ""))
            for (offset in 0..raw.length) {
                val visual = transformed.offsetMapping.originalToTransformed(offset)
                assertTrue(visual in 0..transformed.text.length)
                assertEquals(offset, transformed.offsetMapping.transformedToOriginal(visual))
            }
            for (offset in 0..transformed.text.length)
                assertTrue(transformed.offsetMapping.transformedToOriginal(offset) in 0..raw.length)
        }
        assertEquals("68.820", IdrGroupingTransformation.filter(AnnotatedString("68820")).text.text)
    }

    @Test fun pastedGroupingWorksWithoutSilentlyConvertingNegativesOrDecimals() {
        assertEquals("68820", editIdrInput("", "68.820"))
        assertEquals("", editIdrInput("", ""))
        assertEquals("100", editIdrInput("100", "-100"))
        assertEquals("100", editIdrInput("100", "68,82"))
        assertEquals("100", editIdrInput("100", "1.25"))
    }

    @Test fun categorySearchIsCaseInsensitiveAndPreservesMasterOrdering() {
        val choices = listOf("Makanan-Jadi", "Makanan-Mentah", "Transport", "Kebutuhan")
        assertEquals(listOf("Makanan-Mentah"), filterSelectionChoices(choices, "  MAKANAN mentah  "))
        assertEquals(choices, filterSelectionChoices(choices, "  "))
        assertTrue(filterSelectionChoices(choices, "tidak ada").isEmpty())
    }

    @Test fun calendarUsesUtcDateSemanticsWithoutTimezoneShift() {
        assertEquals(1790985600000L, dateToPickerMillis("2026-10-03"))
        assertEquals("2026-10-03", pickerMillisToDate(requireNotNull(dateToPickerMillis("2026-10-03"))))
        assertNull(dateToPickerMillis(""))
        assertNull(dateToPickerMillis("2026-09-31"))
    }
}

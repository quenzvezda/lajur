package com.lajur.app.ui

import com.lajur.app.domain.LocalTransaction
import com.lajur.app.domain.SyncState
import com.lajur.app.domain.TransactionDraft
import org.junit.Assert.*
import org.junit.Test

class TransactionListReviewTest {
    private val draft = TransactionDraft("2026-10-03", "Pengeluaran", "Bank", "Transport", 25_000, "Bus dummy")
    private val complete = LocalTransaction("complete", 1, draft, SyncState.SYNCED, null, false)
    private val incomplete = complete.copy(transactionId = "incomplete", draft = draft.copy(paymentMethod = ""))

    @Test fun paymentReminderDoesNotIntroduceSettlementOrSyncStatus() {
        assertEquals(listOf("Pembayaran"), missingTransactionFields(incomplete.draft))
        assertEquals(SyncState.SYNCED, incomplete.syncState)
        assertTrue(missingTransactionFields(draft).isEmpty())
        assertEquals(listOf("Tanggal", "Tipe", "Pembayaran", "Kategori", "Jumlah"),
            missingTransactionFields(draft.copy(date = "", type = "", paymentMethod = " ", category = "", amountIdr = 0)))
    }

    @Test fun reviewFilterCombinesWithSearchWithoutCountingDeletedRows() {
        val rows = listOf(complete, incomplete, incomplete.copy(transactionId = "deleted", isDeleted = true))
        assertEquals(listOf(complete, incomplete), filterTransactionReview(rows, "", false))
        assertEquals(listOf(incomplete), filterTransactionReview(rows, " bus ", true))
        assertEquals(listOf(complete), filterTransactionReview(rows, "bAnK", false))
        assertTrue(filterTransactionReview(rows, "Bank", true).isEmpty())
        assertEquals(1, rows.count { !it.isDeleted && missingTransactionFields(it.draft).isNotEmpty() })
    }

    @Test fun fillingPaymentRemovesReminderWithoutChangingIdentityAmountOrRowCount() {
        val filled = incomplete.copy(draft = incomplete.draft.copy(paymentMethod = "Bank"))
        assertEquals(incomplete.transactionId, filled.transactionId)
        assertEquals(incomplete.draft.amountIdr, filled.draft.amountIdr)
        assertTrue(filterTransactionReview(listOf(filled), "", true).isEmpty())
        assertEquals(1, filterTransactionReview(listOf(filled), "", false).size)
    }
}

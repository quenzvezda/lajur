package com.lajur.app.ui

import com.lajur.app.domain.*
import org.junit.Assert.*
import org.junit.Test

class HistoryFiltersTest {
    private fun row(id: String, date: String, amount: Long, payment: String, state: SyncState = SyncState.SYNCED) =
        LocalTransaction(id, 1, TransactionDraft(date, "Pengeluaran", payment, "Makan", amount, "Belanja"), state, null, false)
    private val rows = listOf(row("a", "2026-10-01", 100_000, ""), row("b", "2026-10-07", 250_000, "Bank", SyncState.FAILED),
        row("c", "2026-10-10", 150_000, "Bank"), row("deleted", "2026-10-12", 10_000, "").copy(isDeleted = true))
    @Test fun filtersComposeWithInclusiveBoundsAndSearch() {
        val selected = filterHistory(rows, "belanja", HistoryFilters(type = "Pengeluaran", category = "Makan", payment = "Bank",
            dateFrom = "2026-10-07", dateTo = "2026-10-10", minimum = 150_000, maximum = 250_000))
        assertEquals(listOf("c", "b"), selected.map { it.transactionId })
        assertTrue(filterHistory(rows, "unknown", HistoryFilters()).isEmpty())
    }
    @Test fun blankPaymentAndReviewAreDistinctFromAllPayments() {
        assertEquals(listOf("a"), filterHistory(rows, "", HistoryFilters(payment = "")).map { it.transactionId })
        assertEquals(listOf("a"), filterHistory(rows, "", HistoryFilters(incompleteOnly = true)).map { it.transactionId })
        assertEquals(3, filterHistory(rows, "", HistoryFilters()).size)
    }
    @Test fun sortsByMoneyAndDateAndSupportsFailedSync() {
        assertEquals(listOf("b", "c", "a"), filterHistory(rows, "", HistoryFilters(order = HistoryOrder.HIGHEST)).map { it.transactionId })
        assertEquals(listOf("a", "c", "b"), filterHistory(rows, "", HistoryFilters(order = HistoryOrder.LOWEST)).map { it.transactionId })
        assertEquals(listOf("a", "b", "c"), filterHistory(rows, "", HistoryFilters(order = HistoryOrder.OLDEST)).map { it.transactionId })
        assertEquals(listOf("b"), filterHistory(rows, "", HistoryFilters(sync = SyncState.FAILED)).map { it.transactionId })
    }
}

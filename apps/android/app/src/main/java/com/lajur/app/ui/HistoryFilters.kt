package com.lajur.app.ui

import com.lajur.app.domain.LocalTransaction
import com.lajur.app.domain.SyncState

enum class HistoryOrder(val label: String) {
    NEWEST("Tanggal terbaru"), OLDEST("Tanggal terlama"), HIGHEST("Nominal terbesar"), LOWEST("Nominal terkecil")
}
data class HistoryFilters(
    val type: String? = null, val category: String? = null, val payment: String? = null,
    val dateFrom: String? = null, val dateTo: String? = null,
    val minimum: Long? = null, val maximum: Long? = null,
    val sync: SyncState? = null, val incompleteOnly: Boolean = false,
    val order: HistoryOrder = HistoryOrder.NEWEST,
) {
    val activeCount: Int get() = listOf(type, category, payment, dateFrom, dateTo, minimum, maximum, sync,
        true.takeIf { incompleteOnly }).count { it != null }
}

internal fun filterHistory(rows: List<LocalTransaction>, query: String, filter: HistoryFilters): List<LocalTransaction> {
    val selected = filterTransactionReview(rows, query, filter.incompleteOnly).filter { row ->
        val d = row.draft
        (filter.type == null || d.type == filter.type) && (filter.category == null || d.category == filter.category) &&
            (filter.payment == null || d.paymentMethod == filter.payment) &&
            (filter.dateFrom == null || d.date >= filter.dateFrom) && (filter.dateTo == null || d.date <= filter.dateTo) &&
            (filter.minimum == null || d.amountIdr >= filter.minimum) && (filter.maximum == null || d.amountIdr <= filter.maximum) &&
            (filter.sync == null || row.syncState == filter.sync)
    }
    val order = when (filter.order) {
        HistoryOrder.NEWEST -> compareByDescending<LocalTransaction> { it.draft.date }
        HistoryOrder.OLDEST -> compareBy { it.draft.date }
        HistoryOrder.HIGHEST -> compareByDescending { it.draft.amountIdr }
        HistoryOrder.LOWEST -> compareBy { it.draft.amountIdr }
    }
    return selected.sortedWith(order.thenBy { it.transactionId })
}

package com.lajur.app.ui

import com.lajur.app.domain.LocalTransaction
import com.lajur.app.domain.TransactionDraft
import java.time.LocalDate

/** A review hint only: empty payment does not invalidate a transaction or change its totals. */
internal fun missingTransactionFields(draft: TransactionDraft): List<String> = buildList {
    if (runCatching { LocalDate.parse(draft.date) }.isFailure) add("Tanggal")
    if (draft.type.isBlank()) add("Tipe")
    if (draft.paymentMethod.isBlank()) add("Pembayaran")
    if (draft.category.isBlank()) add("Kategori")
    if (draft.amountIdr <= 0) add("Jumlah")
}

internal fun filterTransactionReview(
    transactions: List<LocalTransaction>, query: String, incompleteOnly: Boolean,
): List<LocalTransaction> {
    val search = query.trim()
    return transactions.filter { transaction ->
        val draft = transaction.draft
        !transaction.isDeleted &&
            (!incompleteOnly || missingTransactionFields(draft).isNotEmpty()) &&
            (search.isEmpty() || listOf(draft.category, draft.paymentMethod, draft.type, draft.description.orEmpty())
                .any { it.contains(search, ignoreCase = true) })
    }
}

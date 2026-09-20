package com.lajur.app.sync

import com.lajur.app.domain.OutboxOperationType
import com.lajur.app.domain.TransactionDraft

data class PendingOperation(
    val type: OutboxOperationType,
    val expectedRevision: Int?,
    val draft: TransactionDraft?,
)

sealed interface CompactionResult {
    data class Keep(val operation: PendingOperation) : CompactionResult
    data object RemoveLocalTransaction : CompactionResult
}

object OutboxPlanner {
    fun plan(
        existing: PendingOperation?,
        requested: OutboxOperationType,
        serverRevision: Int?,
        draft: TransactionDraft?,
    ): CompactionResult = when {
        existing?.type == OutboxOperationType.CREATE && requested == OutboxOperationType.UPDATE ->
            CompactionResult.Keep(PendingOperation(OutboxOperationType.CREATE, null, requireNotNull(draft)))

        existing?.type == OutboxOperationType.CREATE && requested == OutboxOperationType.DELETE ->
            CompactionResult.RemoveLocalTransaction

        requested == OutboxOperationType.CREATE ->
            CompactionResult.Keep(PendingOperation(OutboxOperationType.CREATE, null, requireNotNull(draft)))

        requested == OutboxOperationType.UPDATE -> CompactionResult.Keep(
            PendingOperation(
                OutboxOperationType.UPDATE,
                existing?.expectedRevision ?: requireNotNull(serverRevision),
                requireNotNull(draft),
            ),
        )

        else -> CompactionResult.Keep(
            PendingOperation(
                OutboxOperationType.DELETE,
                existing?.expectedRevision ?: requireNotNull(serverRevision),
                null,
            ),
        )
    }
}

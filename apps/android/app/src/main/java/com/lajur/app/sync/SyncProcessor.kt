package com.lajur.app.sync

import com.lajur.app.data.FinanceRepository
import com.lajur.app.data.SessionRepository
import com.lajur.app.data.remote.ApiException
import com.lajur.app.data.remote.CreateTransactionRequest
import com.lajur.app.data.remote.UpdateTransactionRequest
import com.lajur.app.domain.OutboxOperationType

enum class SyncRunResult { COMPLETE, RETRY, AUTHENTICATION_REQUIRED, USER_ACTION_REQUIRED }

class SyncProcessor(
    private val finance: FinanceRepository,
    private val session: SessionRepository,
) {
    suspend fun run(): SyncRunResult = finance.operationGate.remoteChange { runExclusive() }

    private suspend fun runExclusive(): SyncRunResult {
        while (true) {
            val operation = finance.nextOutbox() ?: return SyncRunResult.COMPLETE
            finance.markSyncing(operation.transactionId)
            try {
                when (OutboxOperationType.valueOf(operation.operation)) {
                    OutboxOperationType.CREATE -> {
                        val draft = finance.decodeDraft(operation)
                        val result = session.authorized { api, token ->
                            api.createTransaction(CreateTransactionRequest.from(operation.transactionId, draft), token)
                        }
                        finance.completeMutation(operation.transactionId, result)
                    }
                    OutboxOperationType.UPDATE -> {
                        val draft = finance.decodeDraft(operation)
                        val result = session.authorized { api, token ->
                            api.updateTransaction(
                                operation.transactionId,
                                draft.date.substring(0, 4).toInt(),
                                UpdateTransactionRequest(
                                    expectedRevision = requireNotNull(operation.expectedRevision),
                                    date = draft.date,
                                    type = draft.type,
                                    paymentMethod = draft.paymentMethod,
                                    category = draft.category,
                                    amountIdr = draft.amountIdr,
                                    description = draft.description,
                                ),
                                token,
                            )
                        }
                        finance.completeMutation(operation.transactionId, result)
                    }
                    OutboxOperationType.DELETE -> {
                        val local = requireNotNull(finance.getTransaction(operation.transactionId))
                        try {
                            session.authorized { api, token ->
                                api.deleteTransaction(
                                    operation.transactionId,
                                    local.draft.date.substring(0, 4).toInt(),
                                    requireNotNull(operation.expectedRevision),
                                    token,
                                )
                            }
                        } catch (exception: ApiException) {
                            if (!(exception.status == 404 && exception.code == "transaction_not_found")) throw exception
                        }
                        finance.completeDelete(operation.transactionId)
                    }
                }
            } catch (exception: ApiException) {
                val message = exception.message ?: "Sinkronisasi gagal."
                return when {
                    exception.requiresAuthentication -> {
                        finance.markFailed(operation.transactionId, "Sesi berakhir — login kembali.")
                        SyncRunResult.AUTHENTICATION_REQUIRED
                    }
                    exception.isTransient -> {
                        finance.markPending(operation.transactionId, message)
                        SyncRunResult.RETRY
                    }
                    else -> {
                        finance.markFailed(operation.transactionId, message)
                        SyncRunResult.USER_ACTION_REQUIRED
                    }
                }
            } catch (exception: Exception) {
                finance.markFailed(operation.transactionId, exception.message ?: "Data lokal tidak valid.")
                return SyncRunResult.USER_ACTION_REQUIRED
            }
        }
    }
}

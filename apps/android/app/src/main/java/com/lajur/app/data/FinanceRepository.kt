package com.lajur.app.data

import androidx.room.withTransaction
import com.lajur.app.data.local.LajurDao
import com.lajur.app.data.local.LajurDatabase
import com.lajur.app.data.local.LocalTransactionEntity
import com.lajur.app.data.local.MasterCacheEntity
import com.lajur.app.data.local.OutboxEntity
import com.lajur.app.data.remote.CreateTransactionRequest
import com.lajur.app.data.remote.FinanceTransactionDto
import com.lajur.app.domain.LocalTransaction
import com.lajur.app.domain.MasterData
import com.lajur.app.domain.MonthSummary
import com.lajur.app.domain.OutboxOperationType
import com.lajur.app.domain.SyncCounts
import com.lajur.app.domain.SyncState
import com.lajur.app.domain.TransactionDraft
import com.lajur.app.sync.CompactionResult
import com.lajur.app.sync.OutboxPlanner
import com.lajur.app.sync.PendingOperation
import com.lajur.app.sync.SyncScheduler
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.map
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import java.time.YearMonth
import java.util.UUID

class FinanceRepository(
    private val database: LajurDatabase,
    private val sessionRepository: SessionRepository,
    private val scheduler: SyncScheduler,
    private val json: Json,
    private val now: () -> Long = System::currentTimeMillis,
) {
    private val dao: LajurDao = database.dao()

    fun observeTransactions(month: YearMonth, query: String): Flow<List<LocalTransaction>> =
        dao.observeMonth(month.toString(), query.trim()).map { rows -> rows.map(LocalTransactionEntity::toDomain) }

    fun observeSummary(month: YearMonth): Flow<MonthSummary> =
        observeTransactions(month, "").map { transactions ->
            MonthSummary(
                incomeIdr = transactions.filter { it.draft.type == "Pemasukan" }.sumOf { it.draft.amountIdr },
                expenseIdr = transactions.filter { it.draft.type == "Pengeluaran" }.sumOf { it.draft.amountIdr },
                transactionCount = transactions.size,
            )
        }

    fun observeSyncCounts(): Flow<SyncCounts> = combine(
        dao.observePendingCount(),
        dao.observeFailedCount(),
    ) { pending, failed -> SyncCounts(pending, failed) }

    fun observeMaster(year: Int): Flow<MasterData?> = dao.observeMaster(year).map { cached ->
        cached?.let { json.decodeFromString<MasterData>(it.payloadJson) }
    }

    suspend fun getTransaction(transactionId: String): LocalTransaction? =
        dao.getTransaction(transactionId)?.toDomain()

    suspend fun create(draft: TransactionDraft): String {
        validate(draft)
        val id = UUID.randomUUID().toString()
        database.withTransaction {
            dao.upsertTransaction(entity(id, null, draft, SyncState.PENDING, null, false))
            persistOperation(id, OutboxPlanner.plan(null, OutboxOperationType.CREATE, null, draft))
        }
        scheduler.enqueue()
        return id
    }

    suspend fun update(transactionId: String, draft: TransactionDraft) {
        validate(draft)
        database.withTransaction {
            val current = requireNotNull(dao.getTransaction(transactionId))
            require(current.revision == null || current.date.substring(0, 4) == draft.date.substring(0, 4)) {
                "Transaksi tidak dapat dipindahkan ke workbook tahun lain."
            }
            val existing = dao.getOutboxForTransaction(transactionId)?.toPendingOperation()
            dao.upsertTransaction(entity(transactionId, current.revision, draft, SyncState.PENDING, null, false))
            persistOperation(
                transactionId,
                OutboxPlanner.plan(existing, OutboxOperationType.UPDATE, current.revision, draft),
            )
        }
        scheduler.enqueue()
    }

    suspend fun delete(transactionId: String) {
        database.withTransaction {
            val current = requireNotNull(dao.getTransaction(transactionId))
            val existing = dao.getOutboxForTransaction(transactionId)?.toPendingOperation()
            when (val plan = OutboxPlanner.plan(existing, OutboxOperationType.DELETE, current.revision, null)) {
                CompactionResult.RemoveLocalTransaction -> {
                    dao.deleteOutbox(transactionId)
                    dao.deleteTransaction(transactionId)
                }
                is CompactionResult.Keep -> {
                    dao.upsertTransaction(current.copy(
                        syncState = SyncState.PENDING.name,
                        lastSyncError = null,
                        isDeleted = true,
                        updatedAtEpochMillis = now(),
                    ))
                    persistOperation(transactionId, plan)
                }
            }
        }
        scheduler.enqueue()
    }

    suspend fun refreshMonth(month: YearMonth): Result<Unit> = runCatching {
        val serverRows = sessionRepository.authorized { api, token ->
            api.listTransactions(month.year, month.monthValue, token)
        }
        database.withTransaction {
            val protected = dao.getUnsyncedTransactionIds()
            if (protected.isEmpty()) dao.deleteSyncedMonth(month.toString())
            else dao.deleteSyncedMonthExcept(month.toString(), protected)
            dao.upsertTransactions(serverRows.filterNot { it.transactionId in protected }.map(::serverEntity))
        }
        refreshMaster(month.year)
    }

    suspend fun refreshMaster(year: Int) {
        val master = sessionRepository.authorized { api, token -> api.getMaster(year, token) }
        dao.upsertMaster(MasterCacheEntity(year, json.encodeToString(master), now()))
    }

    suspend fun nextOutbox(): OutboxEntity? = dao.getNextOutbox()

    suspend fun markSyncing(transactionId: String) = updateSyncState(transactionId, SyncState.SYNCING, null)

    suspend fun markPending(transactionId: String, message: String?) =
        updateSyncState(transactionId, SyncState.PENDING, message)

    suspend fun markFailed(transactionId: String, message: String) =
        updateSyncState(transactionId, SyncState.FAILED, message)

    suspend fun completeMutation(transactionId: String, server: FinanceTransactionDto) {
        database.withTransaction {
            dao.upsertTransaction(serverEntity(server))
            dao.deleteOutbox(transactionId)
        }
    }

    suspend fun completeDelete(transactionId: String) {
        database.withTransaction {
            dao.deleteOutbox(transactionId)
            dao.deleteTransaction(transactionId)
        }
    }

    fun decodeDraft(outbox: OutboxEntity): TransactionDraft =
        json.decodeFromString(requireNotNull(outbox.payloadJson))

    private suspend fun persistOperation(transactionId: String, result: CompactionResult) {
        when (result) {
            CompactionResult.RemoveLocalTransaction -> {
                dao.deleteOutbox(transactionId)
                dao.deleteTransaction(transactionId)
            }
            is CompactionResult.Keep -> {
                val current = dao.getOutboxForTransaction(transactionId)
                dao.upsertOutbox(OutboxEntity(
                    id = current?.id ?: 0,
                    transactionId = transactionId,
                    operation = result.operation.type.name,
                    expectedRevision = result.operation.expectedRevision,
                    payloadJson = result.operation.draft?.let { json.encodeToString(it) },
                    createdAtEpochMillis = current?.createdAtEpochMillis ?: now(),
                ))
            }
        }
    }

    private suspend fun updateSyncState(transactionId: String, state: SyncState, error: String?) {
        dao.getTransaction(transactionId)?.let { current ->
            dao.upsertTransaction(current.copy(
                syncState = state.name,
                lastSyncError = error,
                updatedAtEpochMillis = now(),
            ))
        }
    }

    private fun OutboxEntity.toPendingOperation() = PendingOperation(
        type = OutboxOperationType.valueOf(operation),
        expectedRevision = expectedRevision,
        draft = payloadJson?.let { json.decodeFromString<TransactionDraft>(it) },
    )

    private fun entity(
        id: String,
        revision: Int?,
        draft: TransactionDraft,
        state: SyncState,
        error: String?,
        deleted: Boolean,
    ) = LocalTransactionEntity(
        transactionId = id,
        revision = revision,
        date = draft.date,
        type = draft.type,
        paymentMethod = draft.paymentMethod,
        category = draft.category,
        amountIdr = draft.amountIdr,
        description = draft.description,
        syncState = state.name,
        lastSyncError = error,
        isDeleted = deleted,
        updatedAtEpochMillis = now(),
    )

    private fun serverEntity(server: FinanceTransactionDto) = entity(
        server.transactionId,
        server.revision,
        TransactionDraft(
            server.date,
            server.type,
            server.paymentMethod,
            server.category,
            server.amountIdr,
            server.description,
        ),
        SyncState.SYNCED,
        null,
        false,
    )

    private fun validate(draft: TransactionDraft) {
        require(runCatching { java.time.LocalDate.parse(draft.date) }.isSuccess) { "Tanggal tidak valid." }
        require(draft.type.isNotBlank()) { "Tipe wajib dipilih." }
        require(draft.paymentMethod.isNotBlank()) { "Pembayaran wajib dipilih." }
        require(draft.category.isNotBlank()) { "Kategori wajib dipilih." }
        require(draft.amountIdr > 0) { "Jumlah harus lebih dari nol." }
    }
}

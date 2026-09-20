package com.lajur.app.data.local

import androidx.room.Entity
import androidx.room.Index
import androidx.room.PrimaryKey
import com.lajur.app.domain.LocalTransaction
import com.lajur.app.domain.SyncState
import com.lajur.app.domain.TransactionDraft

@Entity(
    tableName = "transactions",
    indices = [Index("date"), Index("syncState")],
)
data class LocalTransactionEntity(
    @PrimaryKey val transactionId: String,
    val revision: Int?,
    val date: String,
    val type: String,
    val paymentMethod: String,
    val category: String,
    val amountIdr: Long,
    val description: String?,
    val syncState: String,
    val lastSyncError: String?,
    val isDeleted: Boolean,
    val updatedAtEpochMillis: Long,
) {
    fun toDomain() = LocalTransaction(
        transactionId = transactionId,
        revision = revision,
        draft = TransactionDraft(date, type, paymentMethod, category, amountIdr, description),
        syncState = SyncState.valueOf(syncState),
        lastSyncError = lastSyncError,
        isDeleted = isDeleted,
    )
}

@Entity(
    tableName = "outbox",
    indices = [Index(value = ["transactionId"], unique = true)],
)
data class OutboxEntity(
    @PrimaryKey(autoGenerate = true) val id: Long = 0,
    val transactionId: String,
    val operation: String,
    val expectedRevision: Int?,
    val payloadJson: String?,
    val createdAtEpochMillis: Long,
)

@Entity(tableName = "master_cache")
data class MasterCacheEntity(
    @PrimaryKey val year: Int,
    val payloadJson: String,
    val updatedAtEpochMillis: Long,
)

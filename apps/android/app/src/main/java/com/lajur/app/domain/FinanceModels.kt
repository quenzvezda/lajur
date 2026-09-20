package com.lajur.app.domain

import kotlinx.serialization.Serializable

enum class SyncState { SYNCED, PENDING, SYNCING, FAILED }

enum class OutboxOperationType { CREATE, UPDATE, DELETE }

@Serializable
data class TransactionDraft(
    val date: String,
    val type: String,
    val paymentMethod: String,
    val category: String,
    val amountIdr: Long,
    val description: String? = null,
)

data class LocalTransaction(
    val transactionId: String,
    val revision: Int?,
    val draft: TransactionDraft,
    val syncState: SyncState,
    val lastSyncError: String?,
    val isDeleted: Boolean,
)

@Serializable
data class MasterCategory(
    val name: String,
    val kind: String,
    val monthlyBudgetIdr: Long? = null,
)

@Serializable
data class MasterData(
    val workbookYear: Int,
    val schemaVersion: String,
    val transactionTypes: List<String>,
    val paymentMethods: List<String>,
    val categories: List<MasterCategory>,
)

data class MonthSummary(
    val incomeIdr: Long,
    val expenseIdr: Long,
    val transactionCount: Int,
)

data class SyncCounts(val pending: Int, val failed: Int)

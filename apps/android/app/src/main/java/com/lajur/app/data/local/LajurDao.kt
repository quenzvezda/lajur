package com.lajur.app.data.local

import androidx.room.Dao
import androidx.room.Delete
import androidx.room.Insert
import androidx.room.OnConflictStrategy
import androidx.room.Query
import kotlinx.coroutines.flow.Flow

@Dao
interface LajurDao {
    @Query(
        """SELECT * FROM transactions
           WHERE substr(date, 1, 7) = :monthKey AND isDeleted = 0
             AND (:query = '' OR category LIKE '%' || :query || '%' COLLATE NOCASE
                  OR paymentMethod LIKE '%' || :query || '%' COLLATE NOCASE
                  OR type LIKE '%' || :query || '%' COLLATE NOCASE
                  OR coalesce(description, '') LIKE '%' || :query || '%' COLLATE NOCASE)
           ORDER BY date DESC, updatedAtEpochMillis DESC""",
    )
    fun observeMonth(monthKey: String, query: String): Flow<List<LocalTransactionEntity>>

    @Query("SELECT * FROM transactions WHERE transactionId = :transactionId")
    suspend fun getTransaction(transactionId: String): LocalTransactionEntity?

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsertTransaction(transaction: LocalTransactionEntity)

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsertTransactions(transactions: List<LocalTransactionEntity>)

    @Query("DELETE FROM transactions WHERE transactionId = :transactionId")
    suspend fun deleteTransaction(transactionId: String)

    @Query(
        """DELETE FROM transactions
           WHERE substr(date, 1, 7) = :monthKey AND syncState = 'SYNCED'
             AND transactionId NOT IN (:protectedIds)""",
    )
    suspend fun deleteSyncedMonthExcept(monthKey: String, protectedIds: List<String>)

    @Query("DELETE FROM transactions WHERE substr(date, 1, 7) = :monthKey AND syncState = 'SYNCED'")
    suspend fun deleteSyncedMonth(monthKey: String)

    @Query("SELECT transactionId FROM transactions WHERE syncState != 'SYNCED'")
    suspend fun getUnsyncedTransactionIds(): List<String>

    @Query("SELECT COUNT(*) FROM transactions WHERE syncState IN ('PENDING', 'SYNCING')")
    fun observePendingCount(): Flow<Int>

    @Query("SELECT COUNT(*) FROM transactions WHERE syncState = 'FAILED'")
    fun observeFailedCount(): Flow<Int>

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsertOutbox(operation: OutboxEntity)

    @Query("SELECT * FROM outbox WHERE transactionId = :transactionId")
    suspend fun getOutboxForTransaction(transactionId: String): OutboxEntity?

    @Query("SELECT * FROM outbox ORDER BY createdAtEpochMillis, id LIMIT 1")
    suspend fun getNextOutbox(): OutboxEntity?

    @Query("SELECT COUNT(*) FROM outbox")
    suspend fun getOutboxCount(): Int

    @Query("SELECT COUNT(*) FROM outbox")
    fun observeOutboxCount(): Flow<Int>

    @Query("DELETE FROM transactions WHERE substr(date, 1, 4) = :year AND syncState = 'SYNCED'")
    suspend fun clearSyncedYear(year: String)

    @Query("DELETE FROM master_cache WHERE year = :year")
    suspend fun clearMasterYear(year: Int)

    @Query("DELETE FROM outbox WHERE transactionId = :transactionId")
    suspend fun deleteOutbox(transactionId: String)

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsertMaster(master: MasterCacheEntity)

    @Query("SELECT * FROM master_cache WHERE year = :year")
    fun observeMaster(year: Int): Flow<MasterCacheEntity?>

    @Query("DELETE FROM transactions")
    suspend fun clearTransactions()

    @Query("DELETE FROM outbox")
    suspend fun clearOutbox()

    @Query("DELETE FROM master_cache")
    suspend fun clearMaster()
}

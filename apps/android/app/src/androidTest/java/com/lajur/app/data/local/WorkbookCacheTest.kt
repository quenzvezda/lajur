package com.lajur.app.data.local

import androidx.room.Room
import androidx.test.platform.app.InstrumentationRegistry
import kotlinx.coroutines.runBlocking
import org.junit.Assert.*
import org.junit.Test

/** In-memory Room, isolated from Lajur's actual database/account. */
class WorkbookCacheTest {
    @Test fun invalidationIsYearScopedAndOutboxCountsFailedAndDeletedOperations() = runBlocking {
        val database = Room.inMemoryDatabaseBuilder(
            InstrumentationRegistry.getInstrumentation().targetContext, LajurDatabase::class.java,
        ).build()
        try {
            val dao = database.dao()
            fun row(id: String, year: Int, state: String, deleted: Boolean = false) = LocalTransactionEntity(
                id, 1, "$year-09-20", "Pengeluaran", "Cash", "Dummy", 1000, null, state, null, deleted, 1,
            )
            dao.upsertTransactions(listOf(row("old", 2026, "SYNCED"), row("other", 2027, "SYNCED"),
                row("pending", 2026, "PENDING"), row("failed-delete", 2026, "FAILED", true)))
            dao.upsertOutbox(OutboxEntity(transactionId = "pending", operation = "CREATE", expectedRevision = null, payloadJson = "{}", createdAtEpochMillis = 1))
            dao.upsertOutbox(OutboxEntity(transactionId = "failed-delete", operation = "DELETE", expectedRevision = 1, payloadJson = null, createdAtEpochMillis = 1))
            assertEquals(2, dao.getOutboxCount())
            dao.clearSyncedYear("2026")
            assertNull(dao.getTransaction("old"))
            assertNotNull(dao.getTransaction("other"))
            assertNotNull(dao.getTransaction("pending"))
            assertNotNull(dao.getTransaction("failed-delete"))
            assertEquals(2, dao.getOutboxCount())
        } finally { database.close() }
    }
}

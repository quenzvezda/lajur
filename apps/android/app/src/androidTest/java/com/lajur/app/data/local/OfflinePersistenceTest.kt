package com.lajur.app.data.local

import androidx.room.Room
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import com.lajur.app.domain.SyncState
import com.lajur.app.domain.TransactionDraft
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import kotlinx.coroutines.runBlocking
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class OfflinePersistenceTest {
    private val databaseName = "offline-persistence-test.db"
    private val context = InstrumentationRegistry.getInstrumentation().targetContext

    @Before
    fun resetDatabase() {
        context.deleteDatabase(databaseName)
    }

    @After
    fun cleanDatabase() {
        context.deleteDatabase(databaseName)
    }

    @Test
    fun pendingTransactionAndOutboxSurviveDatabaseRestart() = verifyRestart("Tunai")

    @Test
    fun blankPaymentAndItsOutboxSurviveDatabaseRestart() = verifyRestart("")

    private fun verifyRestart(payment: String) {
        runBlocking {
            val transaction = LocalTransactionEntity(
            transactionId = "offline-id",
            revision = null,
            date = "2026-09-20",
            type = "Pengeluaran",
            paymentMethod = payment,
            category = "Makan",
            amountIdr = 25_000,
            description = "Disimpan saat offline",
            syncState = SyncState.PENDING.name,
            lastSyncError = null,
            isDeleted = false,
            updatedAtEpochMillis = 1,
        )
            val outbox = OutboxEntity(
            transactionId = transaction.transactionId,
            operation = "CREATE",
            expectedRevision = null,
            payloadJson = Json.encodeToString(TransactionDraft(transaction.date, transaction.type,
                transaction.paymentMethod, transaction.category, transaction.amountIdr, transaction.description)),
            createdAtEpochMillis = 1,
        )

            val database = openDatabase()
            try {
                database.dao().upsertTransaction(transaction)
                database.dao().upsertOutbox(outbox)
            } finally {
                database.close()
            }

            val reopened = openDatabase()
            try {
                assertEquals(SyncState.PENDING.name, reopened.dao().getTransaction("offline-id")?.syncState)
                val operation = reopened.dao().getNextOutbox()
                assertNotNull(operation)
                assertEquals(payment, reopened.dao().getTransaction("offline-id")?.paymentMethod)
                assertEquals(payment, Json.decodeFromString<TransactionDraft>(requireNotNull(operation?.payloadJson)).paymentMethod)
            } finally {
                reopened.close()
            }
        }
    }

    private fun openDatabase() = Room.databaseBuilder(context, LajurDatabase::class.java, databaseName).build()
}

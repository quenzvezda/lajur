package com.lajur.app.sync

import com.lajur.app.domain.OutboxOperationType
import com.lajur.app.domain.TransactionDraft
import org.junit.Assert.assertEquals
import org.junit.Assert.assertSame
import org.junit.Test

class OutboxPlannerTest {
    private val initial = TransactionDraft(
        date = "2026-09-20",
        type = "Pengeluaran",
        paymentMethod = "Tunai",
        category = "Makan",
        amountIdr = 25_000,
        description = "Nasi goreng",
    )
    private val edited = initial.copy(amountIdr = 30_000, description = "Makan siang")

    @Test
    fun `new transaction edited offline remains one create with latest payload`() {
        val existing = PendingOperation(OutboxOperationType.CREATE, null, initial)

        val result = OutboxPlanner.plan(existing, OutboxOperationType.UPDATE, null, edited)

        assertEquals(
            CompactionResult.Keep(PendingOperation(OutboxOperationType.CREATE, null, edited)),
            result,
        )
    }

    @Test
    fun `new transaction deleted before sync disappears completely`() {
        val existing = PendingOperation(OutboxOperationType.CREATE, null, initial)

        val result = OutboxPlanner.plan(existing, OutboxOperationType.DELETE, null, null)

        assertSame(CompactionResult.RemoveLocalTransaction, result)
    }

    @Test
    fun `multiple offline edits retain original expected revision`() {
        val existing = PendingOperation(OutboxOperationType.UPDATE, 4, initial)

        val result = OutboxPlanner.plan(existing, OutboxOperationType.UPDATE, 5, edited)

        assertEquals(
            CompactionResult.Keep(PendingOperation(OutboxOperationType.UPDATE, 4, edited)),
            result,
        )
    }

    @Test
    fun `deleting edited server transaction retains original expected revision`() {
        val existing = PendingOperation(OutboxOperationType.UPDATE, 7, edited)

        val result = OutboxPlanner.plan(existing, OutboxOperationType.DELETE, 8, null)

        assertEquals(
            CompactionResult.Keep(PendingOperation(OutboxOperationType.DELETE, 7, null)),
            result,
        )
    }

    @Test
    fun `first edit uses current server revision`() {
        val result = OutboxPlanner.plan(null, OutboxOperationType.UPDATE, 3, edited)

        assertEquals(
            CompactionResult.Keep(PendingOperation(OutboxOperationType.UPDATE, 3, edited)),
            result,
        )
    }
}

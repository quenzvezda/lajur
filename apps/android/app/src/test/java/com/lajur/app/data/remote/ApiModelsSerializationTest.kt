package com.lajur.app.data.remote

import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class ApiModelsSerializationTest {
    private val json = Json { ignoreUnknownKeys = true; explicitNulls = false }

    @Test
    fun `ASP NET camel case transaction response decodes`() {
        val response = """
            {
              "transactionId":"2bd00ca2-bad1-4fe2-bccb-d14f22d2073f",
              "revision":2,
              "date":"2026-09-20",
              "type":"Pengeluaran",
              "paymentMethod":"Tunai",
              "category":"Makan",
              "amountIdr":25000,
              "description":"Nasi goreng",
              "futureField":true
            }
        """.trimIndent()

        val transaction = json.decodeFromString<FinanceTransactionDto>(response)

        assertEquals(2, transaction.revision)
        assertEquals(25_000L, transaction.amountIdr)
        assertEquals("Makan", transaction.category)
    }

    @Test
    fun `create request omits null description and uses API field names`() {
        val request = CreateTransactionRequest(
            transactionId = "2bd00ca2-bad1-4fe2-bccb-d14f22d2073f",
            date = "2026-09-20",
            type = "Pengeluaran",
            paymentMethod = "Tunai",
            category = "Makan",
            amountIdr = 25_000,
        )

        val encoded = json.encodeToString(request)

        assertTrue(encoded.contains("\"amountIdr\":25000"))
        assertFalse(encoded.contains("description"))
    }

    @Test
    fun `API failure classification separates retry and user action`() {
        assertTrue(ApiException(null, "network_unavailable", "offline").isTransient)
        assertTrue(ApiException(503, null, "busy").isTransient)
        assertTrue(ApiException(401, "authentication_failed", "expired").requiresAuthentication)
        assertFalse(ApiException(409, "transaction_revision_conflict", "stale").isTransient)
    }
}

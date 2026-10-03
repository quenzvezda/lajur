package com.lajur.app.data.remote

import com.lajur.app.data.WorkbookTransfers
import java.io.File
import kotlinx.coroutines.test.runTest
import kotlinx.serialization.json.Json
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Protocol
import okhttp3.Response
import okhttp3.ResponseBody.Companion.toResponseBody
import okio.Buffer
import org.junit.Assert.*
import org.junit.Test

class WorkbookApiClientTest {
    private val json = Json { ignoreUnknownKeys = true }

    @Test fun `annual and all exports use auth routes and preserve opaque package`() = runTest {
        val bytes = byteArrayOf(80, 75, 3, 4, 0, -1, 7)
        listOf(2026, null).forEach { year ->
            val client = OkHttpClient.Builder().addInterceptor { chain ->
                assertEquals("Bearer test-token", chain.request().header("Authorization"))
                assertEquals(if (year == null) "/api/v1/workbooks/export-all" else "/api/v1/workbooks/2026/export", chain.request().url.encodedPath)
                Response.Builder().request(chain.request()).protocol(Protocol.HTTP_1_1).code(200).message("OK")
                    .body(bytes.toResponseBody((if (year == null) WorkbookTransfers.ZIP_TYPE else WorkbookTransfers.XLSM_TYPE).toMediaType())).build()
            }.build()
            val file = File.createTempFile("workbook-test", ".tmp")
            try {
                LajurApiClient("https://example.test/api/v1", json, client).exportWorkbook(year, file, "test-token")
                assertArrayEquals(bytes, file.readBytes())
            } finally { file.delete() }
        }
    }

    @Test fun `auth and wrong content type never write problem JSON into workbook`() = runTest {
        listOf(401, 200).forEach { status ->
            val client = OkHttpClient.Builder().addInterceptor { chain ->
                Response.Builder().request(chain.request()).protocol(Protocol.HTTP_1_1).code(status).message("Test")
                    .body("{\"code\":\"invalid_session\"}".toResponseBody("application/problem+json".toMediaType())).build()
            }.build()
            val file = File.createTempFile("workbook-test", ".tmp")
            try {
                file.writeText("untouched")
                val error = runCatching { LajurApiClient("https://example.test/api/v1", json, client).exportWorkbook(2026, file, "test-token") }.exceptionOrNull()
                assertNotNull(error)
                if (status == 401) assertTrue((error as ApiException).requiresAuthentication)
                assertEquals("untouched", file.readText())
            } finally { file.delete() }
        }
    }

    @Test fun `import sends explicit safety fields and safe upload filename`() = runTest {
        val client = OkHttpClient.Builder().addInterceptor { chain ->
            val request = chain.request()
            assertEquals("/api/v1/workbooks/import", request.url.encodedPath)
            assertEquals("Bearer test-token", request.header("Authorization"))
            val buffer = Buffer()
            requireNotNull(request.body).writeTo(buffer)
            val multipart = buffer.readUtf8()
            assertTrue(multipart.contains("name=\"year\"\r\n\r\n2026"))
            assertTrue(multipart.contains("name=\"replaceExisting\"\r\n\r\ntrue"))
            assertTrue(multipart.contains("name=\"confirmNoPendingChanges\"\r\n\r\ntrue"))
            assertTrue(multipart.contains("filename=\"workbook.xlsm\""))
            Response.Builder().request(request).protocol(Protocol.HTTP_1_1).code(200).message("OK")
                .body("""{"year":2026,"fileName":"Keuangan_Pribadi_2026.xlsm","indexedTransactionCount":8,"updatedTransactionCount":1,"tombstonedTransactionCount":0,"replacedExisting":true,"backupFileName":"safe-backup.xlsm"}""".toResponseBody("application/json".toMediaType())).build()
        }.build()
        val file = File.createTempFile("workbook-test", ".tmp")
        try {
            file.writeBytes(byteArrayOf(80, 75, 3, 4))
            val result = LajurApiClient("https://example.test/api/v1", json, client).importWorkbook(file, 2026, true, "test-token")
            assertEquals(8, result.indexedTransactionCount)
            assertTrue(result.replacedExisting)
        } finally { file.delete() }
    }
}

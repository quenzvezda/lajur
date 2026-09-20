package com.lajur.app.data.remote

import com.lajur.app.domain.MasterData
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import java.io.IOException
import java.util.concurrent.TimeUnit

class ApiException(
    val status: Int?,
    val code: String?,
    message: String,
    cause: Throwable? = null,
) : Exception(message, cause) {
    val isTransient: Boolean get() = status == null || status >= 500 || status == 408 || status == 429
    val requiresAuthentication: Boolean get() = status == 401
}

class LajurApiClient(
    baseUrl: String,
    private val json: Json,
    private val client: OkHttpClient = OkHttpClient.Builder()
        .connectTimeout(15, TimeUnit.SECONDS)
        .readTimeout(30, TimeUnit.SECONDS)
        .writeTimeout(30, TimeUnit.SECONDS)
        .build(),
) {
    private val root = baseUrl.trim().trimEnd('/')
    private val mediaType = "application/json; charset=utf-8".toMediaType()

    suspend fun login(request: LoginRequest): AuthTokensDto =
        post("auth/login", json.encodeToString(request), null)

    suspend fun refresh(refreshToken: String): AuthTokensDto =
        post("auth/refresh", json.encodeToString(RefreshRequest(refreshToken)), null)

    suspend fun me(accessToken: String): MeDto = get("me", accessToken)

    suspend fun logout(accessToken: String) {
        val request = Request.Builder()
            .url(url("auth/logout"))
            .post(ByteArray(0).toRequestBody())
            .bearer(accessToken)
            .build()
        execute(request)
    }

    suspend fun createWorkbook(year: Int, accessToken: String) {
        execute(Request.Builder().url(url("workbooks/$year/create")).post(ByteArray(0).toRequestBody()).bearer(accessToken).build())
    }

    suspend fun getMaster(year: Int, accessToken: String): MasterData =
        get("master?year=$year", accessToken)

    suspend fun listTransactions(year: Int, month: Int, accessToken: String): List<FinanceTransactionDto> =
        get("transactions?year=$year&month=$month", accessToken)

    suspend fun createTransaction(
        request: CreateTransactionRequest,
        accessToken: String,
    ): FinanceTransactionDto = post("transactions", json.encodeToString(request), accessToken)

    suspend fun updateTransaction(
        transactionId: String,
        workbookYear: Int,
        request: UpdateTransactionRequest,
        accessToken: String,
    ): FinanceTransactionDto = put(
        "transactions/$transactionId?year=$workbookYear",
        json.encodeToString(request),
        accessToken,
    )

    suspend fun deleteTransaction(
        transactionId: String,
        workbookYear: Int,
        expectedRevision: Int,
        accessToken: String,
    ) {
        val request = Request.Builder()
            .url(url("transactions/$transactionId?year=$workbookYear&expectedRevision=$expectedRevision"))
            .delete()
            .bearer(accessToken)
            .build()
        execute(request)
    }

    private suspend inline fun <reified T> get(path: String, accessToken: String): T {
        val request = Request.Builder().url(url(path)).get().bearer(accessToken).build()
        return decode(execute(request))
    }

    private suspend inline fun <reified T> post(path: String, body: String, accessToken: String?): T {
        val builder = Request.Builder().url(url(path)).post(body.toRequestBody(mediaType))
        if (accessToken != null) {
            builder.bearer(accessToken)
        }
        return decode(execute(builder.build()))
    }

    private suspend inline fun <reified T> put(path: String, body: String, accessToken: String): T {
        val request = Request.Builder().url(url(path)).put(body.toRequestBody(mediaType)).bearer(accessToken).build()
        return decode(execute(request))
    }

    private suspend fun execute(request: Request): String = withContext(Dispatchers.IO) {
        try {
            client.newCall(request).execute().use { response ->
                val body = response.body.string()
                if (!response.isSuccessful) {
                    val problem = runCatching { json.decodeFromString<ProblemDto>(body) }.getOrNull()
                    throw ApiException(response.code, problem?.code, problem?.detail ?: "Request failed (${response.code}).")
                }
                body
            }
        } catch (exception: ApiException) {
            throw exception
        } catch (exception: IOException) {
            throw ApiException(null, "network_unavailable", "Tidak dapat terhubung ke server.", exception)
        }
    }

    private inline fun <reified T> decode(body: String): T = try {
        json.decodeFromString(body)
    } catch (exception: Exception) {
        throw ApiException(null, "invalid_server_response", "Respons server tidak dapat dibaca.", exception)
    }

    private fun url(path: String) = "$root/${path.trimStart('/')}"

    private fun Request.Builder.bearer(token: String) = header("Authorization", "Bearer $token")
}

package com.lajur.app.data.remote

import com.lajur.app.domain.MasterData
import com.lajur.app.domain.TransactionDraft
import kotlinx.serialization.Serializable

@Serializable
data class LoginRequest(
    val username: String,
    val password: String,
    val deviceId: String? = null,
    val deviceLabel: String,
)

@Serializable
data class RefreshRequest(val refreshToken: String)

@Serializable
data class AuthDeviceDto(val id: String, val label: String)

@Serializable
data class AuthTokensDto(
    val accessToken: String,
    val accessTokenExpiresAtUtc: String,
    val refreshToken: String,
    val refreshTokenIdleExpiresAtUtc: String,
    val refreshTokenAbsoluteExpiresAtUtc: String,
    val device: AuthDeviceDto,
)

@Serializable
data class MeDto(
    val userId: String,
    val username: String,
    val deviceId: String,
    val deviceLabel: String,
    val sessionFamilyId: String,
    val needsWorkbookOnboarding: Boolean,
    val workbookYears: List<Int>,
)

@Serializable
data class FinanceTransactionDto(
    val transactionId: String,
    val revision: Int,
    val date: String,
    val type: String,
    val paymentMethod: String,
    val category: String,
    val amountIdr: Long,
    val description: String? = null,
)

@Serializable
data class CreateTransactionRequest(
    val transactionId: String,
    val date: String,
    val type: String,
    val paymentMethod: String,
    val category: String,
    val amountIdr: Long,
    val description: String? = null,
) {
    companion object {
        fun from(id: String, draft: TransactionDraft) = CreateTransactionRequest(
            id,
            draft.date,
            draft.type,
            draft.paymentMethod,
            draft.category,
            draft.amountIdr,
            draft.description,
        )
    }
}

@Serializable
data class UpdateTransactionRequest(
    val expectedRevision: Int,
    val date: String,
    val type: String,
    val paymentMethod: String,
    val category: String,
    val amountIdr: Long,
    val description: String? = null,
)

@Serializable
data class ProblemDto(val code: String? = null, val detail: String? = null)

typealias MasterDataDto = MasterData

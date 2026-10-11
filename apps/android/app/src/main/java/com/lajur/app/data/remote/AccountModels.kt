package com.lajur.app.data.remote

import kotlinx.serialization.Serializable

@Serializable data class RegistrationRequest(val username: String, val password: String, val invitationCode: String)
@Serializable data class RegistrationReceipt(val username: String, val codes: List<String>)
@Serializable data class RecoveryRequest(val username: String, val code: String, val newPassword: String)
@Serializable data class ChangePasswordRequest(val password: String, val newPassword: String)
@Serializable data class PasswordConfirmation(val password: String)
@Serializable data class RecoveryCodesDto(val codes: List<String>)
@Serializable data class InvitationReceipt(val code: String, val expiresAtUtc: String)

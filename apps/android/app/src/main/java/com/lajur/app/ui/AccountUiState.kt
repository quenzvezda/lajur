package com.lajur.app.ui

data class AccountUiState(
    val busy: Boolean = false,
    val error: String? = null,
    val message: String? = null,
    val codes: List<String> = emptyList(),
    val invitation: String? = null,
)

fun validNewAccountPassword(password: String, confirmation: String): Boolean =
    password.length in 12..256 && password == confirmation

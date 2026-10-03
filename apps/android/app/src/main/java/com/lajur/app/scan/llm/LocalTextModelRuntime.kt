package com.lajur.app.scan.llm

import kotlinx.serialization.Serializable

data class LocalModelRequest(val prompt: String, val responseSchema: String? = null)

@Serializable
data class LocalModelGenerationMetrics(
    val totalTokens: Int? = null,
    val maxContextTokens: Int,
    val maxOutputTokens: Int,
    val constrainedJson: Boolean,
)

data class LocalModelResponse(val text: String, val metrics: LocalModelGenerationMetrics? = null)

interface LocalTextModelRuntime {
    suspend fun load()
    suspend fun generate(request: LocalModelRequest): LocalModelResponse
    suspend fun unload()
}

class LocalModelUnavailableException(message: String) : IllegalStateException(message)

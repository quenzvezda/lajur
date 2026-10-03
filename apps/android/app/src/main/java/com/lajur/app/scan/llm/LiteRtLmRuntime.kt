package com.lajur.app.scan.llm

import com.google.ai.edge.litertlm.Backend
import com.google.ai.edge.litertlm.Engine
import com.google.ai.edge.litertlm.EngineConfig
import com.google.ai.edge.litertlm.ConversationConfig
import com.google.ai.edge.litertlm.ResponseFormat
import com.google.ai.edge.litertlm.SamplerConfig
import java.io.File
import kotlinx.coroutines.CoroutineDispatcher
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

// Conservative policy for the current Qwen artifact's 1280-token KV cache.
private const val MAX_CONTEXT_TOKENS = 1_280
private const val MAX_OUTPUT_TOKENS = 256

class LiteRtLmRuntime(
    private val modelFile: File,
    private val cacheDirectory: File,
    private val dispatcher: CoroutineDispatcher = Dispatchers.Default,
) : LocalTextModelRuntime {
    private var engine: Engine? = null

    override suspend fun load() = withContext(dispatcher) {
        if (engine != null) return@withContext
        if (!modelFile.isFile) {
            throw LocalModelUnavailableException(
                "Model lokal belum terpasang (${modelFile.name}).",
            )
        }
        cacheDirectory.mkdirs()
        val candidate = Engine(
            EngineConfig(
                modelPath = modelFile.absolutePath,
                backend = Backend.CPU(),
                maxNumTokens = MAX_CONTEXT_TOKENS,
                cacheDir = cacheDirectory.absolutePath,
            ),
        )
        try {
            candidate.initialize()
            engine = candidate
        } catch (error: Throwable) {
            runCatching { candidate.close() }
            throw error
        }
    }

    override suspend fun generate(request: LocalModelRequest): LocalModelResponse = withContext(dispatcher) {
        val current = checkNotNull(engine) { "Model lokal belum dimuat." }
        current.createConversation(ConversationConfig(
            samplerConfig = SamplerConfig(topK = 1, topP = 1.0, temperature = 0.0, seed = 0),
            maxOutputToken = MAX_OUTPUT_TOKENS,
            enableResponseFormat = request.responseSchema != null,
        )).use { conversation ->
            val response = conversation.sendMessage(
                request.prompt,
                responseFormat = request.responseSchema?.let(ResponseFormat::json),
            ).toString()
            LocalModelResponse(response, LocalModelGenerationMetrics(
                totalTokens = runCatching { conversation.getTokenCount() }.getOrNull(),
                maxContextTokens = MAX_CONTEXT_TOKENS,
                maxOutputTokens = MAX_OUTPUT_TOKENS,
                constrainedJson = request.responseSchema != null,
            ))
        }
    }

    override suspend fun unload() = withContext(dispatcher) {
        val current = engine
        engine = null
        current?.close()
        Unit
    }
}

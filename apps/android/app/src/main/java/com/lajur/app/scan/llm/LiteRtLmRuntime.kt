package com.lajur.app.scan.llm

import com.google.ai.edge.litertlm.Backend
import com.google.ai.edge.litertlm.Engine
import com.google.ai.edge.litertlm.EngineConfig
import java.io.File
import kotlinx.coroutines.CoroutineDispatcher
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

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

    override suspend fun generate(prompt: String): String = withContext(dispatcher) {
        val current = checkNotNull(engine) { "Model lokal belum dimuat." }
        current.createConversation().use { conversation ->
            conversation.sendMessage(prompt).toString()
        }
    }

    override suspend fun unload() = withContext(dispatcher) {
        val current = engine
        engine = null
        current?.close()
        Unit
    }
}

package com.lajur.app.scan.llm

interface LocalTextModelRuntime {
    suspend fun load()
    suspend fun generate(prompt: String): String
    suspend fun unload()
}

class LocalModelUnavailableException(message: String) : IllegalStateException(message)

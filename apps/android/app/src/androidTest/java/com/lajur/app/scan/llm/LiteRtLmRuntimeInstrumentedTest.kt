package com.lajur.app.scan.llm

import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import java.io.File
import java.util.UUID
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class LiteRtLmRuntimeInstrumentedTest {
    @Test
    fun missingModelFailsBeforeNativeEngineInitialization() = runBlocking {
        val context = ApplicationProvider.getApplicationContext<android.content.Context>()
        val missingModel = File(context.cacheDir, "missing-${UUID.randomUUID()}.litertlm")
        val runtime = LiteRtLmRuntime(missingModel, File(context.cacheDir, "litert-test-cache"))

        val error = runCatching { runtime.load() }.exceptionOrNull()

        assertTrue(error is LocalModelUnavailableException)
        runtime.unload()
    }
}

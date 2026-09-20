package com.lajur.app.data.remote

import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import kotlinx.coroutines.runBlocking
import kotlinx.serialization.json.Json
import org.junit.Assert.assertEquals
import org.junit.Assume.assumeTrue
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class LocalBackendConnectivityTest {
    @Test
    fun emulatorCanLoginAndReadCurrentSessionWhenExplicitlyEnabled() = runBlocking {
        val arguments = InstrumentationRegistry.getArguments()
        assumeTrue(arguments.getString("lajurE2e") == "true")
        val baseUrl = requireNotNull(arguments.getString("lajurBaseUrl"))
        val username = requireNotNull(arguments.getString("lajurUsername"))
        val password = requireNotNull(arguments.getString("lajurPassword"))
        val json = Json { ignoreUnknownKeys = true; explicitNulls = false }
        val api = LajurApiClient(baseUrl, json)

        val tokens = api.login(LoginRequest(username, password, deviceLabel = "Android emulator test"))
        val me = api.me(tokens.accessToken)

        assertEquals(username, me.username)
    }
}

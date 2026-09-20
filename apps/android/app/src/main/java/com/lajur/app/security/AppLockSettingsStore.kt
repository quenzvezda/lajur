package com.lajur.app.security

import android.content.Context
import androidx.datastore.preferences.core.booleanPreferencesKey
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.emptyPreferences
import androidx.datastore.preferences.core.longPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.catch
import kotlinx.coroutines.flow.map
import java.io.IOException

private val Context.appLockDataStore by preferencesDataStore(name = "app_lock_settings")

class AppLockSettingsStore(context: Context) {
    private val dataStore = context.applicationContext.appLockDataStore

    val settings: Flow<AppLockSettings> = dataStore.data
        .catch { exception ->
            if (exception is IOException) emit(emptyPreferences()) else throw exception
        }
        .map { preferences ->
            AppLockSettings(
                enabled = preferences[ENABLED] ?: false,
                deviceCredentialFallback = preferences[DEVICE_CREDENTIAL_FALLBACK] ?: true,
                timeoutMillis = (preferences[TIMEOUT_MILLIS] ?: DEFAULT_APP_LOCK_TIMEOUT_MILLIS)
                    .takeIf { it in ALLOWED_TIMEOUTS }
                    ?: DEFAULT_APP_LOCK_TIMEOUT_MILLIS,
                screenProtectionEnabled = preferences[SCREEN_PROTECTION_ENABLED] ?: true,
            )
        }

    suspend fun update(settings: AppLockSettings) {
        dataStore.edit { preferences ->
            preferences[ENABLED] = settings.enabled
            preferences[DEVICE_CREDENTIAL_FALLBACK] = settings.deviceCredentialFallback
            preferences[TIMEOUT_MILLIS] = settings.timeoutMillis
            preferences[SCREEN_PROTECTION_ENABLED] = settings.screenProtectionEnabled
        }
    }

    companion object {
        val ALLOWED_TIMEOUTS = setOf(0L, 60_000L, 300_000L, 900_000L, 1_800_000L)

        private val ENABLED = booleanPreferencesKey("enabled")
        private val DEVICE_CREDENTIAL_FALLBACK = booleanPreferencesKey("device_credential_fallback")
        private val TIMEOUT_MILLIS = longPreferencesKey("timeout_millis")
        private val SCREEN_PROTECTION_ENABLED = booleanPreferencesKey("screen_protection_enabled")
    }
}

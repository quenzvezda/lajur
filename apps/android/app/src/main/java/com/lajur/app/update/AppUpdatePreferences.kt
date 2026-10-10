package com.lajur.app.update

import android.content.Context
import androidx.datastore.preferences.core.booleanPreferencesKey
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.emptyPreferences
import androidx.datastore.preferences.core.longPreferencesKey
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import java.io.IOException
import kotlinx.coroutines.flow.catch
import kotlinx.coroutines.flow.first

private val Context.updateDataStore by preferencesDataStore(name = "app_update_settings")

internal class AppUpdatePreferences(context: Context) {
    private val store = context.applicationContext.updateDataStore
    data class Snapshot(val automatic: Boolean, val lastAttempt: Long, val readyManifest: String?)
    suspend fun read(): Snapshot {
        val preferences = store.data.catch { if (it is IOException) emit(emptyPreferences()) else throw it }.first()
        return Snapshot(preferences[AUTO] ?: true, preferences[ATTEMPT] ?: 0, preferences[READY])
    }
    suspend fun automatic(enabled: Boolean) { store.edit { it[AUTO] = enabled } }
    suspend fun attempt(now: Long) { store.edit { it[ATTEMPT] = now } }
    suspend fun ready(manifest: String?) { store.edit { if (manifest == null) it.remove(READY) else it[READY] = manifest } }
    private companion object {
        val AUTO = booleanPreferencesKey("automatic")
        val ATTEMPT = longPreferencesKey("attempt")
        val READY = stringPreferencesKey("ready_manifest")
    }
}

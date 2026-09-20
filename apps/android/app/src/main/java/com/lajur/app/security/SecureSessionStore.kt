package com.lajur.app.security

import android.annotation.SuppressLint
import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import androidx.core.content.edit
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json
import java.security.KeyStore
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

@Serializable
data class PersistedSession(
    val serverUrl: String,
    val refreshToken: String,
    val refreshIdleExpiresAtUtc: String,
    val refreshAbsoluteExpiresAtUtc: String,
    val userId: String,
    val username: String,
    val deviceId: String,
    val deviceLabel: String,
    val needsWorkbookOnboarding: Boolean = false,
    val workbookYears: List<Int> = emptyList(),
)

class SecureSessionStore(context: Context, private val json: Json) {
    private val preferences = context.getSharedPreferences(FILE_NAME, Context.MODE_PRIVATE)

    fun read(): PersistedSession? {
        val encoded = preferences.getString(KEY_SESSION, null) ?: return null
        return runCatching {
            val payload = Base64.decode(encoded, Base64.NO_WRAP)
            val ivSize = payload.first().toInt()
            val iv = payload.copyOfRange(1, 1 + ivSize)
            val encrypted = payload.copyOfRange(1 + ivSize, payload.size)
            val cipher = Cipher.getInstance(TRANSFORMATION)
            cipher.init(Cipher.DECRYPT_MODE, secretKey(), GCMParameterSpec(128, iv))
            json.decodeFromString<PersistedSession>(cipher.doFinal(encrypted).decodeToString())
        }.getOrElse {
            clear()
            null
        }
    }

    @SuppressLint("ApplySharedPref") // Rotation must reach disk before the consumed token can be lost.
    fun write(session: PersistedSession) {
        val cipher = Cipher.getInstance(TRANSFORMATION)
        cipher.init(Cipher.ENCRYPT_MODE, secretKey())
        val encrypted = cipher.doFinal(json.encodeToString(PersistedSession.serializer(), session).encodeToByteArray())
        val payload = byteArrayOf(cipher.iv.size.toByte()) + cipher.iv + encrypted
        preferences.edit(commit = true) {
            putString(KEY_SESSION, Base64.encodeToString(payload, Base64.NO_WRAP))
        }
    }

    @SuppressLint("ApplySharedPref")
    fun clear() {
        preferences.edit(commit = true) { clear() }
    }

    private fun secretKey(): SecretKey {
        val keyStore = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
        (keyStore.getKey(KEY_ALIAS, null) as? SecretKey)?.let { return it }
        val generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore")
        generator.init(
            KeyGenParameterSpec.Builder(
                KEY_ALIAS,
                KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT,
            ).setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .setKeySize(256)
                .build(),
        )
        return generator.generateKey()
    }

    private companion object {
        const val FILE_NAME = "secure_session"
        const val KEY_SESSION = "session"
        const val KEY_ALIAS = "lajur_refresh_session_v1"
        const val TRANSFORMATION = "AES/GCM/NoPadding"
    }
}

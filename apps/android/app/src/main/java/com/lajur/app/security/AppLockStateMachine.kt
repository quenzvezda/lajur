package com.lajur.app.security

const val DEFAULT_APP_LOCK_TIMEOUT_MILLIS = 5 * 60 * 1000L

data class AppLockSettings(
    val enabled: Boolean = false,
    val deviceCredentialFallback: Boolean = true,
    val timeoutMillis: Long = DEFAULT_APP_LOCK_TIMEOUT_MILLIS,
    val screenProtectionEnabled: Boolean = true,
)

enum class AppLockStatus { RESTORING, LOCKED, UNLOCKED }

sealed interface ShareIntentGate {
    data class AwaitingUnlock(val uri: String) : ShareIntentGate
    data class Ready(val uri: String) : ShareIntentGate
}

class AppLockStateMachine {
    var status: AppLockStatus = AppLockStatus.RESTORING
        private set
    var settings: AppLockSettings = AppLockSettings()
        private set
    private var backgroundedAtMillis: Long? = null

    fun restore(restored: AppLockSettings) {
        settings = restored
        status = if (restored.enabled) AppLockStatus.LOCKED else AppLockStatus.UNLOCKED
        backgroundedAtMillis = null
    }

    fun updateSettings(updated: AppLockSettings) {
        val wasEnabled = settings.enabled
        settings = updated
        when {
            !updated.enabled -> status = AppLockStatus.UNLOCKED
            !wasEnabled && status != AppLockStatus.RESTORING -> status = AppLockStatus.UNLOCKED
        }
        backgroundedAtMillis = null
    }

    fun onBackgrounded(elapsedRealtimeMillis: Long) {
        if (settings.enabled && status == AppLockStatus.UNLOCKED) {
            backgroundedAtMillis = elapsedRealtimeMillis
        }
    }

    fun onForegrounded(elapsedRealtimeMillis: Long) {
        val backgroundedAt = backgroundedAtMillis ?: return
        if (settings.enabled && elapsedRealtimeMillis - backgroundedAt >= settings.timeoutMillis) {
            status = AppLockStatus.LOCKED
        }
        backgroundedAtMillis = null
    }

    fun authenticationSucceeded() {
        status = AppLockStatus.UNLOCKED
        backgroundedAtMillis = null
    }

    fun lockNow() {
        if (settings.enabled) status = AppLockStatus.LOCKED
    }

    fun gateSharedImage(uri: String): ShareIntentGate =
        if (settings.enabled && status != AppLockStatus.UNLOCKED) {
            ShareIntentGate.AwaitingUnlock(uri)
        } else {
            ShareIntentGate.Ready(uri)
        }
}

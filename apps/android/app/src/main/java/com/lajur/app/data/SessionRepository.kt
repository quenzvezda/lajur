package com.lajur.app.data

import androidx.room.withTransaction
import com.lajur.app.data.local.LajurDatabase
import com.lajur.app.data.remote.ApiException
import com.lajur.app.data.remote.LajurApiClient
import com.lajur.app.data.remote.LoginRequest
import com.lajur.app.data.remote.MeDto
import com.lajur.app.security.PersistedSession
import com.lajur.app.security.SecureSessionStore
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.serialization.json.Json

sealed interface SessionState {
    data object Restoring : SessionState
    data object SignedOut : SessionState
    data class SignedIn(
        val session: PersistedSession,
        val needsWorkbookOnboarding: Boolean,
        val workbookYears: List<Int>,
        val offline: Boolean,
    ) : SessionState
}

class SessionRepository(
    private val store: SecureSessionStore,
    private val database: LajurDatabase,
    private val json: Json,
    private val operationGate: FinanceOperationGate = FinanceOperationGate(),
) {
    private val refreshMutex = Mutex()
    private val mutableState = MutableStateFlow<SessionState>(SessionState.Restoring)
    val state: StateFlow<SessionState> = mutableState.asStateFlow()

    @Volatile private var accessToken: String? = null
    @Volatile private var persisted: PersistedSession? = null

    suspend fun restore() {
        val cached = store.read()
        if (cached == null) {
            mutableState.value = SessionState.SignedOut
            return
        }

        persisted = cached
        mutableState.value = SessionState.SignedIn(
            cached,
            cached.needsWorkbookOnboarding,
            cached.workbookYears,
            offline = true,
        )
        runCatching { refreshAndLoadMe() }
    }

    suspend fun login(serverUrl: String, username: String, password: String): Result<Unit> = runCatching {
        operationGate.remoteChange {
            operationGate.localChange {
                val normalizedUrl = normalizeServerUrl(serverUrl)
                val api = LajurApiClient(normalizedUrl, json)
                val previous = store.read()
                val tokens = api.login(
                    LoginRequest(
                        username = username.trim(),
                        password = password,
                        deviceId = previous?.takeIf { it.serverUrl == normalizedUrl && it.username == username.trim() }?.deviceId,
                        deviceLabel = "Android Lajur",
                    ),
                )
                val me = api.me(tokens.accessToken)
                if (previous != null && previous.userId != me.userId) {
                    clearLocalFinanceData()
                }
                val session = tokens.toPersisted(normalizedUrl, me)
                store.write(session)
                persisted = session
                accessToken = tokens.accessToken
                mutableState.value = SessionState.SignedIn(
                    session,
                    me.needsWorkbookOnboarding,
                    me.workbookYears,
                    offline = false,
                )
            }
        }
    }

    suspend fun createCurrentYearWorkbook(year: Int): Result<Unit> = runCatching {
        operationGate.remoteChange {
            authorized { api, token -> api.createWorkbook(year, token) }
            val me = authorized { api, token -> api.me(token) }
            updateFromMe(me, offline = false)
        }
    }

    suspend fun reloadWorkbookState() {
        updateFromMe(authorized { api, token -> api.me(token) }, offline = false)
    }

    suspend fun <T> authorized(action: suspend (LajurApiClient, String) -> T): T {
        val session = persisted ?: throw ApiException(401, "authentication_required", "Silakan login kembali.")
        val api = LajurApiClient(session.serverUrl, json)
        val token = accessToken ?: refreshAccessToken(api)
        return try {
            action(api, token)
        } catch (exception: ApiException) {
            if (!exception.requiresAuthentication) throw exception
            action(api, refreshAccessToken(api, force = true))
        }
    }

    fun signOutLocally() {
        accessToken = null
        persisted = null
        store.clear()
        mutableState.value = SessionState.SignedOut
    }

    suspend fun signOut() = operationGate.remoteChange {
        try {
            if (persisted != null) {
                authorized { api, token -> api.logout(token) }
            }
        } finally {
            signOutLocally()
        }
    }

    private suspend fun refreshAndLoadMe() {
        val session = persisted ?: return
        val api = LajurApiClient(session.serverUrl, json)
        try {
            val token = refreshAccessToken(api, force = true)
            val me = api.me(token)
            updateFromMe(me, offline = false)
        } catch (exception: ApiException) {
            if (exception.requiresAuthentication) signOutLocally()
        }
    }

    private suspend fun refreshAccessToken(api: LajurApiClient, force: Boolean = false): String =
        refreshMutex.withLock {
            if (!force) accessToken?.let { return@withLock it }
            val session = persisted ?: throw ApiException(401, "authentication_required", "Silakan login kembali.")
            try {
                val tokens = api.refresh(session.refreshToken)
                val updated = session.copy(
                    refreshToken = tokens.refreshToken,
                    refreshIdleExpiresAtUtc = tokens.refreshTokenIdleExpiresAtUtc,
                    refreshAbsoluteExpiresAtUtc = tokens.refreshTokenAbsoluteExpiresAtUtc,
                    deviceId = tokens.device.id,
                    deviceLabel = tokens.device.label,
                )
                store.write(updated)
                persisted = updated
                accessToken = tokens.accessToken
                tokens.accessToken
            } catch (exception: ApiException) {
                if (exception.requiresAuthentication) signOutLocally()
                throw exception
            }
        }

    private fun updateFromMe(me: MeDto, offline: Boolean) {
        val current = persisted ?: return
        val updated = current.copy(
            userId = me.userId,
            username = me.username,
            deviceId = me.deviceId,
            deviceLabel = me.deviceLabel,
            needsWorkbookOnboarding = me.needsWorkbookOnboarding,
            workbookYears = me.workbookYears,
        )
        store.write(updated)
        persisted = updated
        mutableState.value = SessionState.SignedIn(
            updated,
            me.needsWorkbookOnboarding,
            me.workbookYears,
            offline,
        )
    }

    private suspend fun clearLocalFinanceData() {
        database.withTransaction {
            database.dao().clearOutbox()
            database.dao().clearTransactions()
            database.dao().clearMaster()
        }
    }

    private fun normalizeServerUrl(value: String): String {
        val trimmed = value.trim().trimEnd('/')
        require(trimmed.startsWith("https://") || trimmed.startsWith("http://")) {
            "Alamat server harus diawali http:// atau https://."
        }
        return if (trimmed.endsWith("/api/v1")) "$trimmed/" else "$trimmed/api/v1/"
    }
}

private fun com.lajur.app.data.remote.AuthTokensDto.toPersisted(
    serverUrl: String,
    me: MeDto,
) = PersistedSession(
    serverUrl = serverUrl,
    refreshToken = refreshToken,
    refreshIdleExpiresAtUtc = refreshTokenIdleExpiresAtUtc,
    refreshAbsoluteExpiresAtUtc = refreshTokenAbsoluteExpiresAtUtc,
    userId = me.userId,
    username = me.username,
    deviceId = me.deviceId,
    deviceLabel = me.deviceLabel,
    needsWorkbookOnboarding = me.needsWorkbookOnboarding,
    workbookYears = me.workbookYears,
)

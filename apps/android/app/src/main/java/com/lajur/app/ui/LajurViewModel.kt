package com.lajur.app.ui

import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewModelScope
import android.os.SystemClock
import com.lajur.app.AppContainer
import com.lajur.app.BuildConfig
import com.lajur.app.data.SessionState
import com.lajur.app.domain.LocalTransaction
import com.lajur.app.domain.MasterData
import com.lajur.app.domain.MonthSummary
import com.lajur.app.domain.SyncCounts
import com.lajur.app.domain.TransactionDraft
import com.lajur.app.security.AppLockSettings
import com.lajur.app.security.AppLockStateMachine
import com.lajur.app.security.AppLockStatus
import com.lajur.app.scan.ScanImageRequest
import com.lajur.app.scan.ScanImageSource
import com.lajur.app.scan.SmartScanState
import kotlinx.coroutines.Job
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.flatMapLatest
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch
import java.time.YearMonth

@OptIn(kotlinx.coroutines.ExperimentalCoroutinesApi::class)
class LajurViewModel(private val container: AppContainer) : ViewModel() {
    private val appLockStateMachine = AppLockStateMachine()
    val session = container.sessionRepository.state
    private val mutableAppLockStatus = MutableStateFlow(AppLockStatus.RESTORING)
    val appLockStatus: StateFlow<AppLockStatus> = mutableAppLockStatus.asStateFlow()
    private val mutableAppLockSettings = MutableStateFlow(AppLockSettings())
    val appLockSettings: StateFlow<AppLockSettings> = mutableAppLockSettings.asStateFlow()
    private val mutablePendingSharedImage = MutableStateFlow<String?>(null)
    val pendingSharedImage: StateFlow<String?> = mutablePendingSharedImage.asStateFlow()
    private val mutableSmartScanState = MutableStateFlow<SmartScanState>(SmartScanState.Idle)
    val smartScanState: StateFlow<SmartScanState> = mutableSmartScanState.asStateFlow()
    private var smartScanJob: Job? = null
    private val mutableMonth = MutableStateFlow(YearMonth.now())
    val month: StateFlow<YearMonth> = mutableMonth.asStateFlow()
    private val mutableQuery = MutableStateFlow("")
    val query: StateFlow<String> = mutableQuery.asStateFlow()
    private val mutableBusy = MutableStateFlow(false)
    val busy: StateFlow<Boolean> = mutableBusy.asStateFlow()
    private val mutableMessage = MutableStateFlow<String?>(null)
    val message: StateFlow<String?> = mutableMessage.asStateFlow()

    val transactions = combine(mutableMonth, mutableQuery) { month, query -> month to query }
        .flatMapLatest { (month, query) -> container.financeRepository.observeTransactions(month, query) }
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyList())

    val summary = mutableMonth.flatMapLatest(container.financeRepository::observeSummary)
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), MonthSummary(0, 0, 0))

    val syncCounts = container.financeRepository.observeSyncCounts()
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), SyncCounts(0, 0))

    val master: StateFlow<MasterData?> = mutableMonth
        .flatMapLatest { container.financeRepository.observeMaster(it.year) }
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), null)

    val defaultServerUrl: String = BuildConfig.LAJUR_API_BASE_URL

    init {
        viewModelScope.launch {
            container.appLockSettingsStore.settings.collect { settings ->
                if (appLockStateMachine.status == AppLockStatus.RESTORING) {
                    appLockStateMachine.restore(settings)
                } else {
                    appLockStateMachine.updateSettings(settings)
                }
                mutableAppLockSettings.value = settings
                publishAppLockStatus()
            }
        }
        viewModelScope.launch { container.sessionRepository.restore() }
        viewModelScope.launch {
            session.collect { state ->
                if (state is SessionState.SignedIn && !state.needsWorkbookOnboarding) {
                    container.scheduler.enqueue()
                    refresh()
                }
            }
        }
    }

    fun login(serverUrl: String, username: String, password: String) {
        launchBusy {
            container.sessionRepository.login(serverUrl, username, password)
                .onFailure { mutableMessage.value = it.message ?: "Login gagal." }
        }
    }

    fun createWorkbook(year: Int = YearMonth.now().year) {
        launchBusy {
            container.sessionRepository.createCurrentYearWorkbook(year)
                .onFailure { mutableMessage.value = it.message ?: "Workbook tidak dapat dibuat." }
        }
    }

    fun setQuery(value: String) {
        mutableQuery.value = value
    }

    fun previousMonth() {
        mutableMonth.value = mutableMonth.value.minusMonths(1)
        refresh()
    }

    fun nextMonth() {
        mutableMonth.value = mutableMonth.value.plusMonths(1)
        refresh()
    }

    fun refresh() {
        viewModelScope.launch {
            container.financeRepository.refreshMonth(mutableMonth.value)
                .onFailure { mutableMessage.value = it.message ?: "Data lokal tetap tersedia; refresh gagal." }
        }
    }

    fun save(transactionId: String?, draft: TransactionDraft, onSaved: () -> Unit) {
        viewModelScope.launch {
            runCatching {
                if (transactionId == null) container.financeRepository.create(draft)
                else container.financeRepository.update(transactionId, draft)
            }.onSuccess {
                onSaved()
            }.onFailure {
                mutableMessage.value = it.message ?: "Transaksi tidak dapat disimpan."
            }
        }
    }

    fun delete(transaction: LocalTransaction, onDeleted: () -> Unit) {
        viewModelScope.launch {
            runCatching { container.financeRepository.delete(transaction.transactionId) }
                .onSuccess { onDeleted() }
                .onFailure { mutableMessage.value = it.message ?: "Transaksi tidak dapat dihapus." }
        }
    }

    fun retrySync() {
        container.scheduler.enqueue()
        mutableMessage.value = "Sinkronisasi dijadwalkan."
    }

    fun signOut() {
        viewModelScope.launch { container.sessionRepository.signOut() }
    }

    fun onAppBackgrounded(nowMillis: Long = SystemClock.elapsedRealtime()) {
        appLockStateMachine.onBackgrounded(nowMillis)
        publishAppLockStatus()
    }

    fun onAppForegrounded(nowMillis: Long = SystemClock.elapsedRealtime()) {
        appLockStateMachine.onForegrounded(nowMillis)
        publishAppLockStatus()
    }

    fun onUnlockSucceeded() {
        appLockStateMachine.authenticationSucceeded()
        publishAppLockStatus()
    }

    fun setAppLockEnabled(enabled: Boolean) {
        if (enabled) appLockStateMachine.authenticationSucceeded()
        updateAppLockSettings { it.copy(enabled = enabled) }
    }

    fun setDeviceCredentialFallback(enabled: Boolean) {
        updateAppLockSettings { it.copy(deviceCredentialFallback = enabled) }
    }

    fun setAppLockTimeout(timeoutMillis: Long) {
        updateAppLockSettings { it.copy(timeoutMillis = timeoutMillis) }
    }

    fun setScreenProtectionEnabled(enabled: Boolean) {
        updateAppLockSettings { it.copy(screenProtectionEnabled = enabled) }
    }

    fun lockNow() {
        appLockStateMachine.lockNow()
        publishAppLockStatus()
    }

    fun onSharedImage(uri: String) {
        mutablePendingSharedImage.value = uri
        selectScanImage(uri, ScanImageSource.SHARE)
    }

    fun discardSharedImage() {
        discardScan()
    }

    fun selectScanImage(uri: String, source: ScanImageSource) {
        releaseTemporaryCapture(currentScanRequest())
        smartScanJob?.cancel()
        val request = ScanImageRequest(uri, source)
        mutableSmartScanState.value = SmartScanState.Ready(request)
        mutablePendingSharedImage.value = if (source == ScanImageSource.SHARE) uri else null
    }

    fun processScan() {
        val request = when (val state = mutableSmartScanState.value) {
            is SmartScanState.Ready -> state.request
            is SmartScanState.Failed -> state.request
            else -> return
        }
        smartScanJob?.cancel()
        mutablePendingSharedImage.value = null
        mutableSmartScanState.value = SmartScanState.Processing(request)
        smartScanJob = viewModelScope.launch {
            runCatching { container.smartScanPipeline.process(request) }
                .onSuccess { output ->
                    if ((mutableSmartScanState.value as? SmartScanState.Processing)?.request == request) {
                        mutableSmartScanState.value = SmartScanState.Complete(output)
                        releaseTemporaryCapture(request)
                    }
                }
                .onFailure { error ->
                    if ((mutableSmartScanState.value as? SmartScanState.Processing)?.request == request) {
                        mutableSmartScanState.value = SmartScanState.Failed(
                            request,
                            error.message ?: "OCR tidak dapat memproses gambar.",
                        )
                    }
                }
        }
    }

    fun discardScan() {
        smartScanJob?.cancel()
        releaseTemporaryCapture(currentScanRequest())
        mutablePendingSharedImage.value = null
        mutableSmartScanState.value = SmartScanState.Idle
    }

    fun clearMessage() {
        mutableMessage.value = null
    }

    private fun launchBusy(block: suspend () -> Unit) {
        viewModelScope.launch {
            mutableBusy.value = true
            try {
                block()
            } finally {
                mutableBusy.value = false
            }
        }
    }

    private fun updateAppLockSettings(transform: (AppLockSettings) -> AppLockSettings) {
        viewModelScope.launch {
            container.appLockSettingsStore.update(transform(mutableAppLockSettings.value))
        }
    }

    private fun publishAppLockStatus() {
        mutableAppLockStatus.value = appLockStateMachine.status
    }

    private fun currentScanRequest(): ScanImageRequest? = when (val state = mutableSmartScanState.value) {
        is SmartScanState.Ready -> state.request
        is SmartScanState.Processing -> state.request
        is SmartScanState.Complete -> state.output.request
        is SmartScanState.Failed -> state.request
        SmartScanState.Idle -> null
    }

    private fun releaseTemporaryCapture(request: ScanImageRequest?) {
        if (request?.source == ScanImageSource.CAMERA) {
            container.temporaryCaptureStore.release(request.uri)
        }
    }

    class Factory(private val container: AppContainer) : ViewModelProvider.Factory {
        @Suppress("UNCHECKED_CAST")
        override fun <T : ViewModel> create(modelClass: Class<T>): T = LajurViewModel(container) as T
    }
}

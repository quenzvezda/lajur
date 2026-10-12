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
import com.lajur.app.scan.ScanReviewSession
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.Job
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.flatMapLatest
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch
import java.time.YearMonth

@OptIn(kotlinx.coroutines.ExperimentalCoroutinesApi::class)
class LajurViewModel(private val container: AppContainer) : ViewModel() {
    private val appLockStateMachine = AppLockStateMachine()
    val session = container.sessionRepository.state
    val appUpdates = container.appUpdateController.state
    fun checkAppUpdates(manual: Boolean = true) = container.appUpdateController.check(manual)
    fun setAutomaticUpdates(enabled: Boolean) = container.appUpdateController.setAutoCheck(enabled)
    fun downloadAppUpdate() = container.appUpdateController.download()
    fun cancelAppUpdateDownload() = container.appUpdateController.cancelDownload()
    fun installAppUpdate(onReady: (android.content.Intent) -> Unit) = container.appUpdateController.prepareInstallation(onReady)
    fun appUpdateInstallError() = container.appUpdateController.installationError()
    private val mutableAppLockStatus = MutableStateFlow(AppLockStatus.RESTORING)
    val appLockStatus: StateFlow<AppLockStatus> = mutableAppLockStatus.asStateFlow()
    private val mutableAppLockSettings = MutableStateFlow(AppLockSettings())
    val appLockSettings: StateFlow<AppLockSettings> = mutableAppLockSettings.asStateFlow()
    private val mutableAccountScreenVisible = MutableStateFlow(false)
    val accountScreenVisible = mutableAccountScreenVisible.asStateFlow()
    fun setAccountScreenVisible(visible: Boolean) { mutableAccountScreenVisible.value = visible }
    private val mutablePendingSharedImage = MutableStateFlow<String?>(null)
    val pendingSharedImage: StateFlow<String?> = mutablePendingSharedImage.asStateFlow()
    private val mutableSmartScanState = MutableStateFlow<SmartScanState>(SmartScanState.Idle)
    val smartScanState: StateFlow<SmartScanState> = mutableSmartScanState.asStateFlow()
    private val scanReviewSession = ScanReviewSession(
        container.transactionExtractionEngine, container.extractionValidator, viewModelScope,
    )
    val scanReview = scanReviewSession.state
    private val mutableScanDiagnosticStatus = MutableStateFlow<String?>(null)
    val scanDiagnosticStatus: StateFlow<String?> = mutableScanDiagnosticStatus.asStateFlow()
    private val mutableScanReportSaving = MutableStateFlow(false)
    val scanReportSaving: StateFlow<Boolean> = mutableScanReportSaving.asStateFlow()
    private var smartScanJob: Job? = null
    private val mutableMonth = MutableStateFlow(YearMonth.now())
    val month: StateFlow<YearMonth> = mutableMonth.asStateFlow()
    private val mutableInsight = MutableStateFlow(InsightState())
    val insight = mutableInsight.asStateFlow()
    private var insightJob: Job? = null

    fun loadInsight() {
        val signedIn = session.value as? SessionState.SignedIn ?: return
        val owner = signedIn.session.serverUrl + "|" + signedIn.session.userId
        val year = mutableMonth.value.year
        insightJob?.cancel()
        mutableInsight.value = InsightState(ownerKey = owner, loading = true)
        insightJob = viewModelScope.launch {
            val cached = container.insightRepository.read(owner, year)
            mutableInsight.value = InsightState(ownerKey = owner, cached = cached, loading = true, offline = true)
            try {
                val report = container.sessionRepository.authorized { api, token -> api.report(year, token) }
                currentCoroutineContext().ensureActive()
                val current = session.value as? SessionState.SignedIn
                if (current?.session?.userId != signedIn.session.userId || current.session.serverUrl != signedIn.session.serverUrl) return@launch
                val saved = container.insightRepository.save(owner, report)
                mutableInsight.value = InsightState(ownerKey = owner, cached = saved)
            } catch (error: CancellationException) { throw error }
            catch (_: Exception) {
                mutableInsight.value = InsightState(ownerKey = owner, cached = cached, offline = true,
                    error = if (cached == null) "Laporan belum tersedia. Hubungkan ke server lalu coba lagi."
                        else "Pembaruan gagal. Menampilkan laporan terakhir.")
            }
        }
    }
    private val mutableQuery = MutableStateFlow("")
    val query: StateFlow<String> = mutableQuery.asStateFlow()
    private val mutableHistoryFilters = MutableStateFlow(HistoryFilters())
    val historyFilters = mutableHistoryFilters.asStateFlow()
    fun setHistoryFilters(value: HistoryFilters) { mutableHistoryFilters.value = value }
    private val mutableBusy = MutableStateFlow(false)
    val busy: StateFlow<Boolean> = mutableBusy.asStateFlow()
    private val mutableMessage = MutableStateFlow<String?>(null)
    val message: StateFlow<String?> = mutableMessage.asStateFlow()
    private val mutableWorkbookMenu = MutableStateFlow(WorkbookMenuState())
    val workbookMenu = mutableWorkbookMenu.asStateFlow()
    val workbookOutboxCount = container.workbookRepository.outboxCount
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), 0)
    private var pendingExportYear: Int? = null
    private var pendingExportOwner: String? = null
    private var workbookOwner: String? = null

    fun prepareWorkbookExport(year: Int?) {
        pendingExportYear = year
        pendingExportOwner = (session.value as? SessionState.SignedIn)?.session?.userId
    }

    fun completeWorkbookExport(destination: String?) {
        val year = pendingExportYear
        val sameOwner = pendingExportOwner != null && pendingExportOwner == (session.value as? SessionState.SignedIn)?.session?.userId
        pendingExportOwner = null
        pendingExportYear = null
        if (destination != null && sameOwner) exportWorkbook(year, destination)
    }

    fun loadWorkbooks() = workbookAction {
        mutableWorkbookMenu.value = mutableWorkbookMenu.value.copy(workbooks = container.workbookRepository.list())
    }

    fun selectWorkbook(uri: String) = workbookAction {
        val selected = container.workbookRepository.select(uri)
        mutableWorkbookMenu.value.selected?.file?.delete()
        mutableWorkbookMenu.value = mutableWorkbookMenu.value.copy(selected = selected)
    }

    fun discardSelectedWorkbook() {
        if (mutableWorkbookMenu.value.busy) return
        mutableWorkbookMenu.value.selected?.file?.delete()
        mutableWorkbookMenu.value = mutableWorkbookMenu.value.copy(selected = null)
    }

    fun exportWorkbook(year: Int?, destination: String) = workbookAction {
        container.workbookRepository.export(year, destination)
        mutableWorkbookMenu.value = mutableWorkbookMenu.value.copy(
            status = "Ekspor tersimpan. File hanya memuat data server; perubahan lokal pending tidak termasuk.",
        )
    }

    fun createDataWorkbook(year: Int) = workbookAction {
        container.sessionRepository.createCurrentYearWorkbook(year).getOrThrow()
        mutableMonth.value = YearMonth.of(year, mutableMonth.value.monthValue)
        mutableWorkbookMenu.value = mutableWorkbookMenu.value.copy(
            workbooks = container.workbookRepository.list(), status = "Workbook $year dibuat dari template server.",
        )
    }

    fun importWorkbook(year: Int, replace: Boolean) = workbookAction {
        val selected = requireNotNull(mutableWorkbookMenu.value.selected) { "Pilih workbook terlebih dahulu." }
        try {
            val result = container.workbookRepository.import(selected, year, replace)
            mutableMonth.value = YearMonth.of(result.year, mutableMonth.value.monthValue)
            selected.file.delete()
            mutableWorkbookMenu.value = mutableWorkbookMenu.value.copy(
                selected = null,
                status = "Impor ${result.year} berhasil: ${result.indexedTransactionCount} transaksi aktif, " +
                    "${result.updatedTransactionCount} diperbarui, ${result.tombstonedTransactionCount} dihapus." +
                    if (result.replacedExisting) " Backup sebelum penggantian tersimpan di server." else "",
            )
            mutableWorkbookMenu.value = mutableWorkbookMenu.value.copy(workbooks = container.workbookRepository.list())
        } finally {
            // Also reload after uncertain network failures. The server may have committed.
            runCatching { container.sessionRepository.reloadWorkbookState() }
            container.financeRepository.refreshMonth(mutableMonth.value)
        }
    }

    private fun workbookAction(action: suspend () -> Unit) {
        if (mutableWorkbookMenu.value.busy) return
        mutableWorkbookMenu.value = mutableWorkbookMenu.value.copy(busy = true, error = null, status = null)
        viewModelScope.launch {
            try {
                action()
            } catch (error: CancellationException) {
                throw error
            } catch (error: Exception) {
                mutableWorkbookMenu.value = mutableWorkbookMenu.value.copy(
                    error = (error.message ?: "Operasi workbook gagal.") +
                        " Jika koneksi putus saat impor, refresh dan periksa data server sebelum mencoba lagi.",
                )
            } finally {
                mutableWorkbookMenu.value = mutableWorkbookMenu.value.copy(busy = false)
            }
        }
    }

    // Home filters this complete month snapshot; reminders must not disappear during text search.
    val transactions = mutableMonth
        .flatMapLatest { month -> container.financeRepository.observeTransactions(month, "") }
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyList())

    val summary = mutableMonth.flatMapLatest(container.financeRepository::observeSummary)
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), MonthSummary(0, 0, 0))

    val syncCounts = container.financeRepository.observeSyncCounts()
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), SyncCounts(0, 0))

    val master: StateFlow<MasterData?> = mutableMonth
        .flatMapLatest { container.financeRepository.observeMaster(it.year) }
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), null)

    val defaultServerUrl: String = BuildConfig.LAJUR_API_BASE_URL
    private val mutableAccount = MutableStateFlow(AccountUiState())
    val account = mutableAccount.asStateFlow()

    fun clearAccountResult() { if (!mutableAccount.value.busy) mutableAccount.value = AccountUiState() }

    fun registerAccount(server: String, username: String, password: String, invitation: String) = accountAction {
        val api = accountApi(server)
        val result = api.register(com.lajur.app.data.remote.RegistrationRequest(username.trim(), password, invitation.trim()))
        mutableAccount.value = AccountUiState(codes = result.codes, message = "Akun ${result.username} dibuat. Simpan kode sebelum masuk.")
    }

    fun recoverAccount(server: String, username: String, code: String, password: String) = accountAction {
        accountApi(server).recover(com.lajur.app.data.remote.RecoveryRequest(username.trim(), code.trim(), password))
        mutableAccount.value = AccountUiState(message = "Password diganti. Masuk kembali, lalu buat kode pemulihan baru.")
    }

    fun createRecoveryCodes(password: String) = accountAction {
        val result = container.sessionRepository.authorized { api, token -> api.recoveryCodes(password, token) }
        mutableAccount.value = AccountUiState(codes = result.codes, message = "Kode lama dibatalkan. Simpan kode baru.")
    }

    fun createInvitation(password: String) = accountAction {
        val result = container.sessionRepository.authorized { api, token -> api.invitation(password, token) }
        mutableAccount.value = AccountUiState(invitation = result.code, message = "Undangan sekali pakai, berlaku 7 hari.")
    }

    fun changeAccountPassword(password: String, newPassword: String) = accountAction {
        container.sessionRepository.authorized { api, token ->
            api.changePassword(com.lajur.app.data.remote.ChangePasswordRequest(password, newPassword), token)
        }
        container.sessionRepository.signOutLocally()
        mutableMessage.value = "Password diganti. Masuk kembali dengan password baru."
        mutableAccount.value = AccountUiState()
    }

    private fun accountApi(server: String): com.lajur.app.data.remote.LajurApiClient {
        val root = server.trim().trimEnd('/')
        require(root.startsWith("https://") || (BuildConfig.DEBUG && root.startsWith("http://"))) { "Gunakan alamat HTTPS." }
        val normalized = if (root.endsWith("/api/v1")) "$root/" else "$root/api/v1/"
        return com.lajur.app.data.remote.LajurApiClient(normalized, kotlinx.serialization.json.Json { ignoreUnknownKeys = true })
    }

    private fun accountAction(action: suspend () -> Unit) {
        if (mutableAccount.value.busy) return
        mutableAccount.value = AccountUiState(busy = true)
        viewModelScope.launch {
            try { action() }
            catch (error: CancellationException) { throw error }
            catch (error: Exception) {
                mutableAccount.value = AccountUiState(error = (error.message ?: "Permintaan gagal.") +
                    " Jika koneksi putus setelah mengirim, periksa akun sebelum mencoba lagi. Kode dapat dibuat ulang setelah login.")
            } finally { mutableAccount.value = mutableAccount.value.copy(busy = false) }
        }
    }

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
                if (state !is SessionState.SignedIn) {
                    mutableHistoryFilters.value = HistoryFilters()
                    insightJob?.cancel()
                    mutableInsight.value = InsightState()
                }
                if (state is SessionState.SignedIn && workbookOwner != state.session.userId) {
                    mutableHistoryFilters.value = HistoryFilters()
                    insightJob?.cancel()
                    mutableInsight.value = InsightState()
                    mutableWorkbookMenu.value.selected?.file?.delete()
                    mutableWorkbookMenu.value = WorkbookMenuState()
                    workbookOwner = state.session.userId
                }
                if (state is SessionState.SignedIn && !state.needsWorkbookOnboarding) {
                    container.scheduler.enqueue()
                    refresh()
                }
            }
        }
        viewModelScope.launch {
            master.collect { currentMaster ->
                scanReviewSession.revalidate(currentMaster)
            }
        }
    }

    fun login(serverUrl: String, username: String, password: String) {
        if (mutableWorkbookMenu.value.busy) {
            mutableMessage.value = "Tunggu operasi workbook selesai sebelum login kembali."
            return
        }
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
        mutableHistoryFilters.value = HistoryFilters()
        mutableMonth.value = mutableMonth.value.minusMonths(1)
        refresh()
    }

    fun selectMonth(value: YearMonth) {
        if (value.year !in 1900..9999) return
        mutableMonth.value = value
        mutableHistoryFilters.value = HistoryFilters()
        refresh()
    }

    fun nextMonth() {
        mutableHistoryFilters.value = HistoryFilters()
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
        if (mutableWorkbookMenu.value.busy) return
        discardSelectedWorkbook()
        mutableWorkbookMenu.value = WorkbookMenuState()
        pendingExportOwner = null
        viewModelScope.launch { container.sessionRepository.signOut() }
    }

    fun onAppBackgrounded(nowMillis: Long = SystemClock.elapsedRealtime()) {
        appLockStateMachine.onBackgrounded(nowMillis)
        publishAppLockStatus()
    }

    fun onAppForegrounded(nowMillis: Long = SystemClock.elapsedRealtime()) {
        appLockStateMachine.onForegrounded(nowMillis)
        publishAppLockStatus()
        checkAppUpdates(manual = false)
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
        scanReviewSession.clear()
        mutableScanDiagnosticStatus.value = null
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
            try {
                val output = container.smartScanPipeline.process(request)
                currentCoroutineContext().ensureActive()
                if ((mutableSmartScanState.value as? SmartScanState.Processing)?.request != request) return@launch
                scanReviewSession.show(output, master.value)
                mutableSmartScanState.value = SmartScanState.Complete(output)
                releaseTemporaryCapture(request)
            } catch (error: CancellationException) {
                throw error
            } catch (error: Exception) {
                if ((mutableSmartScanState.value as? SmartScanState.Processing)?.request == request) {
                    mutableSmartScanState.value = SmartScanState.Failed(
                        request, error.message ?: "OCR tidak dapat memproses gambar.",
                    )
                }
            }
        }
    }

    fun requestScanSuggestions() {
        if (mutableScanReportSaving.value) return
        mutableScanDiagnosticStatus.value = null
        scanReviewSession.requestSuggestions()
    }

    fun cancelScanSuggestions() {
        scanReviewSession.cancelSuggestions()
    }


    fun discardScan() {
        smartScanJob?.cancel()
        releaseTemporaryCapture(currentScanRequest())
        mutablePendingSharedImage.value = null
        mutableSmartScanState.value = SmartScanState.Idle
        scanReviewSession.clear()
        mutableScanDiagnosticStatus.value = null
    }

    fun saveScanDiagnostic() {
        if (!BuildConfig.DEBUG || mutableScanReportSaving.value) return
        val snapshot = scanReview.value ?: return
        if (snapshot.suggestions == com.lajur.app.scan.ScanSuggestionState.RUNNING) return
        mutableScanReportSaving.value = true
        viewModelScope.launch {
            val message = runCatching { container.debugScanDiagnosticsStore.save(snapshot.output, snapshot.result, snapshot.review) }
                .fold({ "Laporan uji tersimpan. Bisa diambil lewat ADB." }, { "Laporan gagal disimpan: ${it.message}" })
            mutableScanReportSaving.value = false
            if (scanReview.value?.output == snapshot.output) {
                mutableScanDiagnosticStatus.value = message
            }
        }
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

    override fun onCleared() {
        mutableWorkbookMenu.value.selected?.file?.delete()
        super.onCleared()
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

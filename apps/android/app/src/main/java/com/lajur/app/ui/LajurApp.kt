package com.lajur.app.ui

import android.widget.Toast
import androidx.compose.foundation.clickable
import androidx.compose.foundation.verticalScroll
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyListState
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FloatingActionButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.luminance
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.lajur.app.data.SessionState
import com.lajur.app.domain.LocalTransaction
import com.lajur.app.domain.SyncCounts
import com.lajur.app.domain.SyncState
import com.lajur.app.security.AppLockSettings
import com.lajur.app.security.AppLockStatus
import com.lajur.app.scan.SmartScanState
import com.lajur.app.scan.ScanSuggestionState
import java.text.NumberFormat
import java.time.YearMonth
import java.time.format.DateTimeFormatter
import java.util.Locale

@Composable
fun LajurApp(
    viewModel: LajurViewModel,
    authenticate: (Boolean, () -> Unit, (String) -> Unit) -> Unit,
    pickImage: () -> Unit,
    takePhoto: () -> Unit,
    onExit: () -> Unit,
) {
    val session by viewModel.session.collectAsStateWithLifecycle()
    val busy by viewModel.busy.collectAsStateWithLifecycle()
    val message by viewModel.message.collectAsStateWithLifecycle()
    val lockStatus by viewModel.appLockStatus.collectAsStateWithLifecycle()
    val lockSettings by viewModel.appLockSettings.collectAsStateWithLifecycle()
    var workbookSetup by remember { mutableStateOf(false) }
    when (val state = session) {
        SessionState.Restoring -> LoadingScreen()
        SessionState.SignedOut -> LoginScreen(
            defaultServerUrl = viewModel.defaultServerUrl,
            busy = busy,
            errorMessage = message,
            onLogin = { server, username, password ->
                viewModel.clearMessage()
                viewModel.login(server, username, password)
            },
            updateContent = { AppUpdateRoute(viewModel) },
        )
        is SessionState.SignedIn -> when (lockStatus) {
            AppLockStatus.RESTORING -> LoadingScreen()
            AppLockStatus.LOCKED -> UnlockScreen(
                allowDeviceCredential = lockSettings.deviceCredentialFallback,
                authenticate = authenticate,
                onUnlocked = viewModel::onUnlockSucceeded,
                onSignOut = viewModel::signOut,
            )
            AppLockStatus.UNLOCKED -> if (state.needsWorkbookOnboarding) {
                if (workbookSetup) WorkbookRoute(viewModel) { workbookSetup = false }
                else OnboardingScreen(busy, { viewModel.createWorkbook() }, { workbookSetup = true }, viewModel::signOut)
            } else {
                SignedInApp(viewModel, state, authenticate, pickImage, takePhoto, onExit)
            }
        }
    }
}

@Composable
private fun SignedInApp(
    viewModel: LajurViewModel,
    session: SessionState.SignedIn,
    authenticate: (Boolean, () -> Unit, (String) -> Unit) -> Unit,
    pickImage: () -> Unit,
    takePhoto: () -> Unit,
    onExit: () -> Unit,
) {
    val transactions by viewModel.transactions.collectAsStateWithLifecycle()
    val summary by viewModel.summary.collectAsStateWithLifecycle()
    val master by viewModel.master.collectAsStateWithLifecycle()
    val month by viewModel.month.collectAsStateWithLifecycle()
    val query by viewModel.query.collectAsStateWithLifecycle()
    val syncCounts by viewModel.syncCounts.collectAsStateWithLifecycle()
    val appUpdate by viewModel.appUpdates.collectAsStateWithLifecycle()
    val message by viewModel.message.collectAsStateWithLifecycle()
    val lockSettings by viewModel.appLockSettings.collectAsStateWithLifecycle()
    val pendingSharedImage by viewModel.pendingSharedImage.collectAsStateWithLifecycle()
    val smartScanState by viewModel.smartScanState.collectAsStateWithLifecycle()
    val scanReview by viewModel.scanReview.collectAsStateWithLifecycle()
    val scanReportSaving by viewModel.scanReportSaving.collectAsStateWithLifecycle()
    val scanDiagnosticStatus by viewModel.scanDiagnosticStatus.collectAsStateWithLifecycle()
    var screen: AppScreen by remember { mutableStateOf(AppScreen.Main()) }
    val snackbar = remember { SnackbarHostState() }
    val context = LocalContext.current
    val goBack = rememberAppBackAction(screen,
        onNavigate = { target ->
            if (screen == AppScreen.SmartScan) viewModel.discardScan()
            screen = target
        }, onExit = onExit, onExitPrompt = {
            Toast.makeText(context, "Tekan sekali lagi untuk keluar", Toast.LENGTH_SHORT).show()
        })

    LaunchedEffect(message) {
        message?.let {
            snackbar.showSnackbar(it)
            viewModel.clearMessage()
        }
    }
    LaunchedEffect(appUpdate.release?.versionCode) {
        val release = appUpdate.release
        if (release != null && appUpdate.incompatibility == null && screen != AppScreen.Main(MainTab.SETTINGS)) {
            if (snackbar.showSnackbar("Pembaruan ${release.versionName} tersedia", actionLabel = "Lihat") ==
                androidx.compose.material3.SnackbarResult.ActionPerformed) screen = AppScreen.Main(MainTab.SETTINGS)
        }
    }

    LaunchedEffect(smartScanState) {
        if (smartScanState is SmartScanState.Ready) screen = AppScreen.SmartScan
        if (smartScanState is SmartScanState.Idle && screen == AppScreen.SmartScan) screen = AppScreen.Main(MainTab.SCAN)
    }

    when (val current = screen) {
        is AppScreen.Main -> if (current.tab != MainTab.SETTINGS) HomeScreen(
            month = month,
            transactions = transactions,
            incomeIdr = summary.incomeIdr,
            expenseIdr = summary.expenseIdr,
            query = query,
            syncCounts = syncCounts,
            offlineSession = session.offline,
            hasWorkbook = month.year in session.workbookYears,
            hasPendingSharedImage = pendingSharedImage != null,
            snackbar = snackbar,
            onQueryChange = viewModel::setQuery,
            onPreviousMonth = viewModel::previousMonth,
            onNextMonth = viewModel::nextMonth,
            onRefresh = viewModel::refresh,
            onRetry = viewModel::retrySync,
            onCreateWorkbook = { viewModel.createWorkbook(month.year) },
            onAdd = { screen = AppScreen.Editor(null, origin = current.tab) },
            onTransaction = { screen = AppScreen.Detail(it, current.tab) },
            onPickImage = pickImage,
            onTakePhoto = takePhoto,
            onOpenSharedImage = { screen = AppScreen.SmartScan },
            onDiscardSharedImage = viewModel::discardSharedImage,
            tab = current.tab,
            onTab = { screen = AppScreen.Main(it) },
        ) else SettingsScreen(
            settings = lockSettings,
            onBack = goBack,
            onEnableRequest = { onError ->
                authenticate(
                    lockSettings.deviceCredentialFallback,
                    { viewModel.setAppLockEnabled(true) },
                    onError,
                )
            },
            onDisable = { viewModel.setAppLockEnabled(false) },
            onDeviceCredentialFallbackRequest = { enabled, onError ->
                if (enabled) {
                    viewModel.setDeviceCredentialFallback(true)
                } else {
                    authenticate(
                        false,
                        { viewModel.setDeviceCredentialFallback(false) },
                        onError,
                    )
                }
            },
            onTimeout = viewModel::setAppLockTimeout,
            onScreenProtection = viewModel::setScreenProtectionEnabled,
            onLockNow = viewModel::lockNow,
            onData = { screen = AppScreen.Workbooks },
            onSignOut = viewModel::signOut,
            bottomBar = { MainNavigationBar(MainTab.SETTINGS, pendingSharedImage != null) { screen = AppScreen.Main(it) } },
            updateContent = { AppUpdateRoute(viewModel, showAutoPreference = true) },
        )
        AppScreen.Workbooks -> WorkbookRoute(viewModel, goBack)
        AppScreen.SmartScan -> SmartScanScreen(
            state = smartScanState,
            snapshot = scanReview,
            canSaveScanReport = scanReview != null && scanReview?.suggestions != ScanSuggestionState.RUNNING && !scanReportSaving,
            scanDiagnosticStatus = scanDiagnosticStatus,
            onSaveScanReport = viewModel::saveScanDiagnostic,
            onRequestSuggestions = viewModel::requestScanSuggestions,
            onCancelSuggestions = viewModel::cancelScanSuggestions,
            onProcess = viewModel::processScan,
            onRetry = viewModel::processScan,
            onBack = goBack,
            onManualEntry = { extraction ->
                viewModel.discardScan()
                screen = AppScreen.Editor(null, extraction)
            },
        )
        is AppScreen.Editor -> TransactionEditor(
            transaction = current.transaction,
            initialExtraction = current.extraction,
            master = master,
            onBack = goBack,
            onSave = { draft ->
                viewModel.save(current.transaction?.transactionId, draft) { screen = AppScreen.Main(current.origin) }
            },
        )
        is AppScreen.Detail -> TransactionDetail(
            transaction = current.transaction,
            onBack = goBack,
            onEdit = { screen = AppScreen.Editor(current.transaction, origin = current.origin) },
            onDelete = { viewModel.delete(current.transaction) { screen = AppScreen.Main(current.origin) } },
        )
    }
}

@Composable
private fun LoadingScreen() = Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
    CircularProgressIndicator()
}

@Composable
private fun UnlockScreen(
    allowDeviceCredential: Boolean,
    authenticate: (Boolean, () -> Unit, (String) -> Unit) -> Unit,
    onUnlocked: () -> Unit,
    onSignOut: () -> Unit,
) {
    var attempt by remember { mutableIntStateOf(0) }
    var error by remember { mutableStateOf<String?>(null) }
    LaunchedEffect(attempt) {
        authenticate(allowDeviceCredential, onUnlocked) { error = it }
    }
    Column(
        Modifier.fillMaxSize().padding(24.dp),
        verticalArrangement = Arrangement.Center,
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        LajurBrand()
        Spacer(Modifier.height(24.dp))
        Text("Lajur terkunci", style = MaterialTheme.typography.headlineMedium)
        Text("Buka dengan biometrik atau kunci layar perangkat.")
        error?.let { Text(it, color = MaterialTheme.colorScheme.error) }
        Spacer(Modifier.height(16.dp))
        Button(onClick = { error = null; attempt++ }) { Text("Buka Lajur") }
        TextButton(onClick = onSignOut) { Text("Keluar dari akun") }
    }
}

@Composable
private fun LoginScreen(
    defaultServerUrl: String,
    busy: Boolean,
    errorMessage: String?,
    onLogin: (String, String, String) -> Unit,
    updateContent: @Composable () -> Unit,
) {
    var server by rememberSaveable { mutableStateOf(defaultServerUrl) }
    var username by rememberSaveable { mutableStateOf("") }
    var password by rememberSaveable { mutableStateOf("") }
    Column(
        Modifier.fillMaxSize().imePadding().verticalScroll(rememberScrollState()).padding(24.dp),
        verticalArrangement = Arrangement.Center,
    ) {
        LajurBrand()
        Spacer(Modifier.height(12.dp))
        Text("Catat cepat, Excel tetap milikmu.", style = MaterialTheme.typography.bodyLarge)
        Spacer(Modifier.height(24.dp))
        OutlinedTextField(server, { server = it }, label = { Text("Alamat server") }, modifier = Modifier.fillMaxWidth())
        OutlinedTextField(username, { username = it }, label = { Text("Username") }, modifier = Modifier.fillMaxWidth())
        OutlinedTextField(
            password,
            { password = it },
            label = { Text("Password") },
            visualTransformation = PasswordVisualTransformation(),
            modifier = Modifier.fillMaxWidth(),
        )
        errorMessage?.let {
            Spacer(Modifier.height(8.dp))
            Text(it, color = MaterialTheme.colorScheme.error)
        }
        Spacer(Modifier.height(16.dp))
        Button(
            onClick = { onLogin(server, username, password) },
            enabled = !busy && username.isNotBlank() && password.isNotBlank(),
            modifier = Modifier.fillMaxWidth(),
        ) {
            if (busy) CircularProgressIndicator(Modifier.height(20.dp)) else Text("Login")
        }
        Spacer(Modifier.height(24.dp))
        updateContent()
    }
}

@Composable
private fun OnboardingScreen(busy: Boolean, onCreate: () -> Unit, onImport: () -> Unit, onSignOut: () -> Unit) {
    Column(
        Modifier.fillMaxSize().padding(24.dp),
        verticalArrangement = Arrangement.Center,
    ) {
        Text("Siapkan workbook", style = MaterialTheme.typography.headlineMedium)
        Text("Akun ini belum memiliki workbook. Buat workbook tahun berjalan dari template server.")
        Spacer(Modifier.height(16.dp))
        Button(onClick = onCreate, enabled = !busy, modifier = Modifier.fillMaxWidth()) {
            Text(if (busy) "Membuat…" else "Buat workbook ${YearMonth.now().year}")
        }
        OutlinedButton(onClick = onImport, enabled = !busy, modifier = Modifier.fillMaxWidth()) { Text("Impor workbook yang sudah ada") }
        TextButton(onClick = onSignOut, modifier = Modifier.align(Alignment.End)) { Text("Keluar") }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
internal fun HomeScreen(
    month: YearMonth,
    transactions: List<LocalTransaction>,
    incomeIdr: Long,
    expenseIdr: Long,
    query: String,
    syncCounts: SyncCounts,
    offlineSession: Boolean,
    hasWorkbook: Boolean,
    hasPendingSharedImage: Boolean,
    snackbar: SnackbarHostState,
    onQueryChange: (String) -> Unit,
    onPreviousMonth: () -> Unit,
    onNextMonth: () -> Unit,
    onRefresh: () -> Unit,
    onRetry: () -> Unit,
    onCreateWorkbook: () -> Unit,
    onAdd: () -> Unit,
    onTransaction: (LocalTransaction) -> Unit,
    onPickImage: () -> Unit,
    onTakePhoto: () -> Unit,
    onOpenSharedImage: () -> Unit,
    onDiscardSharedImage: () -> Unit,
    tab: MainTab = MainTab.HOME,
    onTab: (MainTab) -> Unit,
) {
    var incompleteOnly by rememberSaveable(month.toString()) { mutableStateOf(false) }
    val listState = remember(tab, month) { LazyListState() }
    val incompleteCount = transactions.count { !it.isDeleted && missingTransactionFields(it.draft).isNotEmpty() }
    val visibleTransactions = if (tab == MainTab.HOME) transactions.filterNot { it.isDeleted }
        .sortedWith(compareByDescending<LocalTransaction> { it.draft.date }.thenBy { it.transactionId }).take(3)
        else filterTransactionReview(transactions, query, incompleteOnly)
    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text(when (tab) {
                    MainTab.HOME -> "Lajur"
                    MainTab.SCAN -> "Smart Scan"
                    else -> tab.label
                }) },
            )
        },
        bottomBar = { MainNavigationBar(tab, hasPendingSharedImage, onTab) },
        snackbarHost = { SnackbarHost(snackbar) },
        floatingActionButton = {
            if (hasWorkbook && (tab == MainTab.HOME || tab == MainTab.HISTORY))
                FloatingActionButton(onClick = onAdd, modifier = Modifier.testTag("add-transaction")) {
                    Text("+ Tambah", Modifier.padding(horizontal = 12.dp))
                }
        },
    ) { padding ->
        LazyColumn(
            Modifier.fillMaxSize().padding(padding).padding(horizontal = 16.dp).testTag("home-content"),
            state = listState,
            verticalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            if (tab == MainTab.HOME || tab == MainTab.HISTORY) item {
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
                    OutlinedButton(onClick = onPreviousMonth) { Text("‹") }
                    Text(month.format(DateTimeFormatter.ofPattern("MMMM yyyy", Locale.forLanguageTag("id-ID"))), style = MaterialTheme.typography.titleLarge)
                    OutlinedButton(onClick = onNextMonth) { Text("›") }
                }
            }
            if (tab == MainTab.HOME) item {
                Card(Modifier.fillMaxWidth()) {
                    Column(Modifier.padding(16.dp)) {
                        Text("Ringkasan bulan ini", style = MaterialTheme.typography.titleMedium)
                        Spacer(Modifier.height(12.dp))
                        Text("Pemasukan", style = MaterialTheme.typography.labelLarge)
                        Text(formatIdr(incomeIdr), style = MaterialTheme.typography.headlineSmall)
                        Spacer(Modifier.height(10.dp))
                        Text("Pengeluaran", style = MaterialTheme.typography.labelLarge)
                        Text(formatIdr(expenseIdr), style = MaterialTheme.typography.headlineSmall)
                        Spacer(Modifier.height(10.dp))
                        Text("Selisih bulan ini ${formatIdr(incomeIdr - expenseIdr)}")
                        Text("${transactions.count { !it.isDeleted }} transaksi", style = MaterialTheme.typography.bodySmall)
                    }
                }
            }
            if (!hasWorkbook && (tab == MainTab.HOME || tab == MainTab.HISTORY)) {
                item {
                    Card(Modifier.fillMaxWidth()) {
                        Column(Modifier.padding(16.dp)) {
                            Text("Workbook ${month.year} belum tersedia.")
                            Button(onClick = onCreateWorkbook, enabled = !offlineSession) {
                                Text("Buat workbook ${month.year}")
                            }
                        }
                    }
                }
            }
            if (hasPendingSharedImage && tab == MainTab.SCAN) {
                item {
                    Card(Modifier.fillMaxWidth()) {
                        Column(Modifier.padding(16.dp)) {
                            Text("Gambar diterima", style = MaterialTheme.typography.titleMedium)
                            Text("Gambar aman ditahan setelah unlock dan siap diproses secara lokal.")
                            Button(onClick = onOpenSharedImage) { Text("Buka Smart Scan") }
                            TextButton(onClick = onDiscardSharedImage) { Text("Buang gambar") }
                        }
                    }
                }
            }
            if (tab == MainTab.SCAN) item {
                Card(Modifier.fillMaxWidth()) {
                    Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        Text("Smart Scan", style = MaterialTheme.typography.titleMedium)
                        Text("OCR berjalan di perangkat. Gambar dan teks tidak dikirim ke server.")
                        Text("Pilih struk, lalu periksa draft sebelum menyimpan. Saran AI tetap opsional.")
                        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                            OutlinedButton(onClick = onPickImage) { Text("Pilih gambar") }
                            OutlinedButton(onClick = onTakePhoto) { Text("Kamera") }
                        }
                    }
                }
            }
            if (tab == MainTab.INSIGHTS) item {
                Card(Modifier.fillMaxWidth().testTag("insights-placeholder")) {
                    Column(Modifier.padding(20.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                        Text("Insight · segera hadir", style = MaterialTheme.typography.titleLarge)
                        Text("TODO: ringkasan dan analitik tambahan. Belum tersedia pada versi ini.")
                        Text("Untuk saat ini, lihat ringkasan bulanan di Beranda atau analitik pada workbook Excel.")
                        OutlinedButton(onClick = { onTab(MainTab.HOME) }) { Text("Lihat ringkasan bulan") }
                    }
                }
            }
            if (tab == MainTab.HOME || tab == MainTab.HISTORY) item {
                val syncText = when {
                    syncCounts.failed > 0 -> "Error ${syncCounts.failed} — ketuk untuk coba lagi"
                    syncCounts.pending > 0 -> "Pending ${syncCounts.pending}"
                    offlineSession -> "Mode offline"
                    else -> "Tersinkronisasi"
                }
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                    Text(syncText, Modifier.clickable(enabled = syncCounts.failed > 0, onClick = onRetry))
                    TextButton(onClick = onRefresh) { Text("Refresh") }
                }
            }
            if ((tab == MainTab.HOME || tab == MainTab.HISTORY) && (incompleteCount > 0 || incompleteOnly)) item {
                Card(Modifier.fillMaxWidth().testTag("transaction-review-reminder")) {
                    Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                        Text("Perlu dilengkapi", style = MaterialTheme.typography.titleMedium)
                        Text("$incompleteCount transaksi pada data bulan ini yang sudah dimuat. " +
                            "Pembayaran boleh diisi nanti; transaksi tetap dihitung dalam ringkasan.")
                        TextButton(onClick = {
                            incompleteOnly = !incompleteOnly
                            onQueryChange("")
                            if (tab == MainTab.HOME) onTab(MainTab.HISTORY)
                        }, modifier = Modifier.testTag("transaction-review-toggle")) {
                            Text(if (incompleteOnly) "Lihat semua transaksi" else "Lihat transaksi")
                        }
                    }
                }
            }
            if (tab == MainTab.HISTORY) item {
                OutlinedTextField(
                    query,
                    onQueryChange,
                    label = { Text("Cari transaksi") },
                    modifier = Modifier.fillMaxWidth(),
                    singleLine = true,
                )
            }
            if (tab == MainTab.HOME) item {
                Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.SpaceBetween) {
                    Text("Transaksi terbaru", style = MaterialTheme.typography.titleMedium)
                    TextButton(onClick = { incompleteOnly = false; onQueryChange(""); onTab(MainTab.HISTORY) }) {
                        Text("Lihat semua")
                    }
                }
            }
            if (tab == MainTab.HOME || tab == MainTab.HISTORY) if (visibleTransactions.isEmpty()) {
                item { Text(if (tab == MainTab.HISTORY && incompleteOnly) "Tidak ada transaksi yang perlu dilengkapi pada hasil ini."
                    else if (tab == MainTab.HISTORY && query.isNotBlank()) "Transaksi tidak ditemukan."
                    else "Belum ada transaksi untuk bulan ini.", Modifier.padding(vertical = 24.dp)) }
            } else {
                items(visibleTransactions, key = { it.transactionId }) { transaction ->
                    TransactionRow(transaction) { onTransaction(transaction) }
                }
            }
            item { Spacer(Modifier.height(80.dp)) }
        }
    }
}


@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun SettingsScreen(
    settings: AppLockSettings,
    onBack: () -> Unit,
    onEnableRequest: ((String) -> Unit) -> Unit,
    onDisable: () -> Unit,
    onDeviceCredentialFallbackRequest: (Boolean, (String) -> Unit) -> Unit,
    onTimeout: (Long) -> Unit,
    onScreenProtection: (Boolean) -> Unit,
    onLockNow: () -> Unit,
    onData: () -> Unit,
    onSignOut: () -> Unit,
    bottomBar: @Composable () -> Unit,
    updateContent: @Composable () -> Unit,
) {
    var enableError by remember { mutableStateOf<String?>(null) }
    val timeoutChoices = listOf(
        0L to "Segera",
        60_000L to "1 menit",
        300_000L to "5 menit",
        900_000L to "15 menit",
        1_800_000L to "30 menit",
    )
    Scaffold(
        bottomBar = bottomBar,
        topBar = {
            TopAppBar(
                title = { Text("Pengaturan") },
                navigationIcon = { TextButton(onClick = onBack) { Text("Kembali") } },
            )
        },
    ) { padding ->
        LazyColumn(
            Modifier.fillMaxSize().padding(padding).padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(14.dp),
        ) {
            item { updateContent() }
            item {
                Text("Data", style = MaterialTheme.typography.headlineSmall)
                OutlinedButton(onClick = onData, modifier = Modifier.fillMaxWidth()) { Text("Data / Workbook") }
                Text("Impor, ekspor, buat workbook tahunan, dan status perubahan lokal.")
            }
            item { Text("Keamanan", style = MaterialTheme.typography.headlineSmall) }
            item {
                Row(
                    Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Column(Modifier.weight(1f)) {
                        Text("Kunci aplikasi", style = MaterialTheme.typography.titleMedium)
                        Text("Minta autentikasi saat kembali setelah timeout.")
                    }
                    Switch(
                        checked = settings.enabled,
                        onCheckedChange = { enabled ->
                            if (enabled) onEnableRequest { enableError = it } else onDisable()
                        },
                    )
                }
            }
            enableError?.let { error ->
                item { Text(error, color = MaterialTheme.colorScheme.error) }
            }
            if (settings.enabled) {
                item {
                    Row(
                        Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.SpaceBetween,
                        verticalAlignment = Alignment.CenterVertically,
                    ) {
                        Column(Modifier.weight(1f)) {
                            Text("Fallback kunci perangkat")
                            Text("Izinkan PIN, pola, atau password perangkat.")
                        }
                        Switch(
                            checked = settings.deviceCredentialFallback,
                            onCheckedChange = { enabled ->
                                onDeviceCredentialFallbackRequest(enabled) { enableError = it }
                            },
                        )
                    }
                }
                item { Text("Kunci otomatis", style = MaterialTheme.typography.titleMedium) }
                items(timeoutChoices) { (millis, label) ->
                    OutlinedButton(
                        onClick = { onTimeout(millis) },
                        enabled = settings.timeoutMillis != millis,
                        modifier = Modifier.fillMaxWidth(),
                    ) {
                        Text(if (settings.timeoutMillis == millis) "$label (aktif)" else label)
                    }
                }
                item {
                    Button(onClick = onLockNow, modifier = Modifier.fillMaxWidth()) {
                        Text("Kunci sekarang")
                    }
                }
            }
            item { Text("Privasi layar", style = MaterialTheme.typography.headlineSmall) }
            item {
                Row(
                    Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Column(Modifier.weight(1f)) {
                        Text("Lindungi layar & Recent Apps", style = MaterialTheme.typography.titleMedium)
                        Text(
                            if (settings.screenProtectionEnabled) {
                                "Screenshot diblokir dan preview Recent Apps disembunyikan."
                            } else {
                                "Screenshot dan preview Recent Apps dapat menampilkan data keuangan."
                            },
                        )
                    }
                    Switch(
                        checked = settings.screenProtectionEnabled,
                        onCheckedChange = onScreenProtection,
                    )
                }
            }
            item {
                Text("Perubahan perlindungan layar berlaku langsung.")
            }
            item { OutlinedButton(onClick = onSignOut, modifier = Modifier.fillMaxWidth()) { Text("Keluar akun") } }
        }
    }
}

@Composable
private fun TransactionRow(transaction: LocalTransaction, onClick: () -> Unit) {
    val dark = MaterialTheme.colorScheme.surface.luminance() < 0.5f
    val income = transaction.draft.type == "Pemasukan"
    val expense = transaction.draft.type == "Pengeluaran"
    val container = when {
        income -> if (dark) Color(0xFF163D2C) else Color(0xFFE3F4E8)
        expense -> if (dark) Color(0xFF46252A) else Color(0xFFFCE8E8)
        else -> MaterialTheme.colorScheme.surfaceVariant
    }
    val foreground = when {
        income -> if (dark) Color(0xFFB3E9C5) else Color(0xFF164A2A)
        expense -> if (dark) Color(0xFFFFC5CA) else Color(0xFF78272D)
        else -> MaterialTheme.colorScheme.onSurfaceVariant
    }
    Card(Modifier.fillMaxWidth().testTag("transaction-${transaction.transactionId}").clickable(onClick = onClick),
        colors = CardDefaults.cardColors(containerColor = container, contentColor = foreground)) {
        Row(Modifier.fillMaxWidth().padding(14.dp), horizontalArrangement = Arrangement.SpaceBetween) {
            Column(Modifier.weight(1f)) {
                Text(transaction.draft.category, style = MaterialTheme.typography.titleMedium)
                Text("${transaction.draft.date} · ${transaction.draft.paymentMethod.ifBlank { "Pembayaran belum diisi" }}")
                val missing = missingTransactionFields(transaction.draft)
                if (missing.any { it != "Pembayaran" }) Text("Periksa: " + missing.joinToString(),
                    style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.tertiary)
                transaction.draft.description?.let { Text(it, style = MaterialTheme.typography.bodySmall) }
            }
            Column(horizontalAlignment = Alignment.End) {
                Text(formatIdr(transaction.draft.amountIdr), style = MaterialTheme.typography.titleMedium)
                Text(transaction.draft.type.ifBlank { "Tipe belum diisi" }, style = MaterialTheme.typography.labelSmall)
                if (transaction.syncState != SyncState.SYNCED) Text(transaction.syncState.name.lowercase())
            }
        }
    }
}


@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun TransactionDetail(
    transaction: LocalTransaction,
    onBack: () -> Unit,
    onEdit: () -> Unit,
    onDelete: () -> Unit,
) {
    var confirmDelete by remember { mutableStateOf(false) }
    Scaffold(topBar = { TopAppBar(title = { Text("Detail transaksi") }, navigationIcon = { TextButton(onClick = onBack) { Text("Kembali") } }) }) { padding ->
        Column(Modifier.fillMaxSize().padding(padding).padding(20.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
            Text(formatIdr(transaction.draft.amountIdr), style = MaterialTheme.typography.headlineLarge)
            Text(transaction.draft.date)
            Text("${transaction.draft.type} · ${transaction.draft.category}")
            Text("Pembayaran: ${transaction.draft.paymentMethod.ifBlank { "Belum diisi (opsional)" }}")
            transaction.draft.description?.let { Text(it) }
            Text("Status: ${transaction.syncState.name}")
            transaction.lastSyncError?.let { Text(it, color = MaterialTheme.colorScheme.error) }
            Spacer(Modifier.height(12.dp))
            Button(onClick = onEdit, modifier = Modifier.fillMaxWidth()) { Text("Edit") }
            OutlinedButton(onClick = { confirmDelete = true }, modifier = Modifier.fillMaxWidth()) { Text("Hapus") }
        }
    }
    if (confirmDelete) AlertDialog(
        onDismissRequest = { confirmDelete = false },
        title = { Text("Hapus transaksi?") },
        text = { Text("Transaksi akan dihapus secara lokal dan disinkronkan ke workbook.") },
        confirmButton = { TextButton(onClick = onDelete) { Text("Hapus") } },
        dismissButton = { TextButton(onClick = { confirmDelete = false }) { Text("Batal") } },
    )
}

internal fun formatIdr(value: Long): String =
    NumberFormat.getCurrencyInstance(Locale.forLanguageTag("id-ID")).format(value).replace(",00", "")

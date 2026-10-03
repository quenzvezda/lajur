package com.lajur.app.ui

import androidx.compose.foundation.clickable
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
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Card
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
import com.lajur.app.scan.extraction.ValidatedTransactionExtraction
import java.text.NumberFormat
import java.time.YearMonth
import java.time.format.DateTimeFormatter
import java.util.Locale

private sealed interface AppScreen {
    data object Home : AppScreen
    data object Settings : AppScreen
    data object Workbooks : AppScreen
    data object SmartScan : AppScreen
    data class Editor(
        val transaction: LocalTransaction?,
        val extraction: ValidatedTransactionExtraction? = null,
    ) : AppScreen
    data class Detail(val transaction: LocalTransaction) : AppScreen
}

@Composable
fun LajurApp(
    viewModel: LajurViewModel,
    authenticate: (Boolean, () -> Unit, (String) -> Unit) -> Unit,
    pickImage: () -> Unit,
    takePhoto: () -> Unit,
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
                SignedInApp(viewModel, state, authenticate, pickImage, takePhoto)
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
) {
    val transactions by viewModel.transactions.collectAsStateWithLifecycle()
    val summary by viewModel.summary.collectAsStateWithLifecycle()
    val master by viewModel.master.collectAsStateWithLifecycle()
    val month by viewModel.month.collectAsStateWithLifecycle()
    val query by viewModel.query.collectAsStateWithLifecycle()
    val syncCounts by viewModel.syncCounts.collectAsStateWithLifecycle()
    val message by viewModel.message.collectAsStateWithLifecycle()
    val lockSettings by viewModel.appLockSettings.collectAsStateWithLifecycle()
    val pendingSharedImage by viewModel.pendingSharedImage.collectAsStateWithLifecycle()
    val smartScanState by viewModel.smartScanState.collectAsStateWithLifecycle()
    val scanReview by viewModel.scanReview.collectAsStateWithLifecycle()
    val scanReportSaving by viewModel.scanReportSaving.collectAsStateWithLifecycle()
    val scanDiagnosticStatus by viewModel.scanDiagnosticStatus.collectAsStateWithLifecycle()
    var screen: AppScreen by remember { mutableStateOf(AppScreen.Home) }
    val snackbar = remember { SnackbarHostState() }

    LaunchedEffect(message) {
        message?.let {
            snackbar.showSnackbar(it)
            viewModel.clearMessage()
        }
    }

    LaunchedEffect(smartScanState) {
        if (smartScanState is SmartScanState.Ready) screen = AppScreen.SmartScan
        if (smartScanState is SmartScanState.Idle && screen == AppScreen.SmartScan) screen = AppScreen.Home
    }

    when (val current = screen) {
        AppScreen.Home -> HomeScreen(
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
            onAdd = { screen = AppScreen.Editor(null) },
            onTransaction = { screen = AppScreen.Detail(it) },
            onPickImage = pickImage,
            onTakePhoto = takePhoto,
            onOpenSharedImage = { screen = AppScreen.SmartScan },
            onDiscardSharedImage = viewModel::discardSharedImage,
            onSettings = { screen = AppScreen.Settings },
            onSignOut = viewModel::signOut,
        )
        AppScreen.Settings -> SettingsScreen(
            settings = lockSettings,
            onBack = { screen = AppScreen.Home },
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
        )
        AppScreen.Workbooks -> WorkbookRoute(viewModel) { screen = AppScreen.Settings }
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
            onBack = {
                viewModel.discardScan()
                screen = AppScreen.Home
            },
            onManualEntry = { extraction ->
                viewModel.discardScan()
                screen = AppScreen.Editor(null, extraction)
            },
        )
        is AppScreen.Editor -> TransactionEditor(
            transaction = current.transaction,
            initialExtraction = current.extraction,
            master = master,
            onBack = { screen = AppScreen.Home },
            onSave = { draft ->
                viewModel.save(current.transaction?.transactionId, draft) { screen = AppScreen.Home }
            },
        )
        is AppScreen.Detail -> TransactionDetail(
            transaction = current.transaction,
            onBack = { screen = AppScreen.Home },
            onEdit = { screen = AppScreen.Editor(current.transaction) },
            onDelete = { viewModel.delete(current.transaction) { screen = AppScreen.Home } },
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
) {
    var server by rememberSaveable { mutableStateOf(defaultServerUrl) }
    var username by rememberSaveable { mutableStateOf("") }
    var password by rememberSaveable { mutableStateOf("") }
    Column(
        Modifier.fillMaxSize().padding(24.dp),
        verticalArrangement = Arrangement.Center,
    ) {
        Text("Lajur", style = MaterialTheme.typography.displaySmall)
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
    onSettings: () -> Unit,
    onSignOut: () -> Unit,
) {
    var incompleteOnly by rememberSaveable(month.toString()) { mutableStateOf(false) }
    val incompleteCount = transactions.count { !it.isDeleted && missingTransactionFields(it.draft).isNotEmpty() }
    val visibleTransactions = filterTransactionReview(transactions, query, incompleteOnly)
    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Lajur") },
                actions = {
                    TextButton(onClick = onSettings) { Text("Pengaturan") }
                    TextButton(onClick = onSignOut) { Text("Keluar") }
                },
            )
        },
        snackbarHost = { SnackbarHost(snackbar) },
        floatingActionButton = {
            if (hasWorkbook) FloatingActionButton(onClick = onAdd) { Text("+") }
        },
    ) { padding ->
        LazyColumn(
            Modifier.fillMaxSize().padding(padding).padding(horizontal = 16.dp).testTag("home-content"),
            verticalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            item {
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
                    OutlinedButton(onClick = onPreviousMonth) { Text("‹") }
                    Text(month.format(DateTimeFormatter.ofPattern("MMMM yyyy", Locale.forLanguageTag("id-ID"))), style = MaterialTheme.typography.titleLarge)
                    OutlinedButton(onClick = onNextMonth) { Text("›") }
                }
            }
            item {
                Card(Modifier.fillMaxWidth()) {
                    Column(Modifier.padding(16.dp)) {
                        Text("Pengeluaran", style = MaterialTheme.typography.labelLarge)
                        Text(formatIdr(expenseIdr), style = MaterialTheme.typography.headlineMedium)
                        Text("Pemasukan ${formatIdr(incomeIdr)}")
                    }
                }
            }
            if (!hasWorkbook) {
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
            if (hasPendingSharedImage) {
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
            item {
                Card(Modifier.fillMaxWidth()) {
                    Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        Text("Smart Scan", style = MaterialTheme.typography.titleMedium)
                        Text("OCR berjalan di perangkat. Gambar dan teks tidak dikirim ke server.")
                        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                            OutlinedButton(onClick = onPickImage) { Text("Pilih gambar") }
                            OutlinedButton(onClick = onTakePhoto) { Text("Kamera") }
                        }
                    }
                }
            }
            item {
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
            if (incompleteCount > 0 || incompleteOnly) item {
                Card(Modifier.fillMaxWidth().testTag("transaction-review-reminder")) {
                    Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                        Text("Perlu dilengkapi", style = MaterialTheme.typography.titleMedium)
                        Text("$incompleteCount transaksi pada data bulan ini yang sudah dimuat. " +
                            "Pembayaran boleh diisi nanti; transaksi tetap dihitung dalam ringkasan.")
                        TextButton(onClick = {
                            incompleteOnly = !incompleteOnly
                            onQueryChange("")
                        }, modifier = Modifier.testTag("transaction-review-toggle")) {
                            Text(if (incompleteOnly) "Lihat semua transaksi" else "Lihat transaksi")
                        }
                    }
                }
            }
            item {
                OutlinedTextField(
                    query,
                    onQueryChange,
                    label = { Text("Cari transaksi") },
                    modifier = Modifier.fillMaxWidth(),
                    singleLine = true,
                )
            }
            if (visibleTransactions.isEmpty()) {
                item { Text(if (incompleteOnly) "Tidak ada transaksi yang perlu dilengkapi pada hasil ini."
                    else if (query.isNotBlank()) "Transaksi tidak ditemukan."
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
        }
    }
}

@Composable
private fun TransactionRow(transaction: LocalTransaction, onClick: () -> Unit) {
    Card(Modifier.fillMaxWidth().testTag("transaction-${transaction.transactionId}").clickable(onClick = onClick)) {
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

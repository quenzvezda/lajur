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
import androidx.compose.foundation.text.selection.SelectionContainer
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
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
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.lajur.app.data.SessionState
import com.lajur.app.domain.LocalTransaction
import com.lajur.app.domain.MasterData
import com.lajur.app.domain.SyncCounts
import com.lajur.app.domain.SyncState
import com.lajur.app.domain.TransactionDraft
import com.lajur.app.security.AppLockSettings
import com.lajur.app.security.AppLockStatus
import com.lajur.app.scan.ScanImageSource
import com.lajur.app.scan.SmartScanState
import java.text.NumberFormat
import java.time.LocalDate
import java.time.YearMonth
import java.time.format.DateTimeFormatter
import java.util.Locale

private sealed interface AppScreen {
    data object Home : AppScreen
    data object Settings : AppScreen
    data object SmartScan : AppScreen
    data class Editor(val transaction: LocalTransaction?) : AppScreen
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
                OnboardingScreen(busy, { viewModel.createWorkbook() }, viewModel::signOut)
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
        )
        AppScreen.SmartScan -> SmartScanScreen(
            state = smartScanState,
            onProcess = viewModel::processScan,
            onRetry = viewModel::processScan,
            onBack = {
                viewModel.discardScan()
                screen = AppScreen.Home
            },
            onManualEntry = {
                viewModel.discardScan()
                screen = AppScreen.Editor(null)
            },
        )
        is AppScreen.Editor -> TransactionEditor(
            transaction = current.transaction,
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
private fun OnboardingScreen(busy: Boolean, onCreate: () -> Unit, onSignOut: () -> Unit) {
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
        TextButton(onClick = onSignOut, modifier = Modifier.align(Alignment.End)) { Text("Keluar") }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun HomeScreen(
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
            Modifier.fillMaxSize().padding(padding).padding(horizontal = 16.dp),
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
            item {
                OutlinedTextField(
                    query,
                    onQueryChange,
                    label = { Text("Cari transaksi") },
                    modifier = Modifier.fillMaxWidth(),
                    singleLine = true,
                )
            }
            if (transactions.isEmpty()) {
                item { Text("Belum ada transaksi untuk bulan ini.", Modifier.padding(vertical = 24.dp)) }
            } else {
                items(transactions, key = { it.transactionId }) { transaction ->
                    TransactionRow(transaction) { onTransaction(transaction) }
                }
            }
            item { Spacer(Modifier.height(80.dp)) }
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun SmartScanScreen(
    state: SmartScanState,
    onProcess: () -> Unit,
    onRetry: () -> Unit,
    onBack: () -> Unit,
    onManualEntry: () -> Unit,
) {
    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Smart Scan") },
                navigationIcon = { TextButton(onClick = onBack) { Text("Kembali") } },
            )
        },
    ) { padding ->
        LazyColumn(
            Modifier.fillMaxSize().padding(padding).padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            when (state) {
                SmartScanState.Idle -> item { Text("Tidak ada gambar yang dipilih.") }
                is SmartScanState.Ready -> {
                    item {
                        LaunchedEffect(state.request) { onProcess() }
                        Text("Menyiapkan gambar…")
                    }
                }
                is SmartScanState.Processing -> item {
                    Column(
                        Modifier.fillMaxWidth(),
                        horizontalAlignment = Alignment.CenterHorizontally,
                        verticalArrangement = Arrangement.spacedBy(12.dp),
                    ) {
                        CircularProgressIndicator()
                        Text("Membaca teks secara lokal…")
                        Text(scanSourceLabel(state.request.source), style = MaterialTheme.typography.bodySmall)
                        TextButton(onClick = onBack) { Text("Batalkan") }
                    }
                }
                is SmartScanState.Failed -> {
                    item { Text("OCR gagal", style = MaterialTheme.typography.headlineSmall) }
                    item { Text(state.message, color = MaterialTheme.colorScheme.error) }
                    item {
                        Button(onClick = onRetry, modifier = Modifier.fillMaxWidth()) { Text("Coba lagi") }
                    }
                    item {
                        OutlinedButton(onClick = onBack, modifier = Modifier.fillMaxWidth()) { Text("Buang gambar") }
                    }
                }
                is SmartScanState.Complete -> {
                    val output = state.output
                    item {
                        Text("Teks berhasil dibaca", style = MaterialTheme.typography.headlineSmall)
                        Text(
                            "${output.image.originalWidth}×${output.image.originalHeight} → " +
                                "${output.image.processedWidth}×${output.image.processedHeight} · " +
                                "OCR ${output.ocr.recognitionMillis} ms",
                            style = MaterialTheme.typography.bodySmall,
                        )
                    }
                    output.warnings.forEach { warning ->
                        item { Text(warning, color = MaterialTheme.colorScheme.error) }
                    }
                    item {
                        Card(Modifier.fillMaxWidth()) {
                            Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                                Text("Hasil OCR", style = MaterialTheme.typography.titleMedium)
                                SelectionContainer {
                                    Text(output.ocr.text.ifBlank { "Tidak ada teks yang terbaca." })
                                }
                            }
                        }
                    }
                    item {
                        Card(Modifier.fillMaxWidth()) {
                            Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                                Text("Draft transaksi", style = MaterialTheme.typography.titleMedium)
                                Text("Tanggal: belum diekstrak", color = MaterialTheme.colorScheme.error)
                                Text("Jumlah: belum diekstrak", color = MaterialTheme.colorScheme.error)
                                Text("Pembayaran: belum diekstrak", color = MaterialTheme.colorScheme.error)
                                Text("Kategori: belum diekstrak", color = MaterialTheme.colorScheme.error)
                                Text(
                                    "Phase 7 hanya membaca teks. Ekstraksi lokal dan penanda confidence ditambahkan pada Phase 8.",
                                    style = MaterialTheme.typography.bodySmall,
                                )
                            }
                        }
                    }
                    item {
                        Button(onClick = onManualEntry, modifier = Modifier.fillMaxWidth()) {
                            Text("Isi transaksi manual")
                        }
                    }
                    item {
                        OutlinedButton(onClick = onBack, modifier = Modifier.fillMaxWidth()) { Text("Buang hasil") }
                    }
                }
            }
        }
    }
}

private fun scanSourceLabel(source: ScanImageSource): String = when (source) {
    ScanImageSource.SHARE -> "Sumber: dibagikan ke Lajur"
    ScanImageSource.GALLERY -> "Sumber: galeri"
    ScanImageSource.CAMERA -> "Sumber: kamera"
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
    Card(Modifier.fillMaxWidth().clickable(onClick = onClick)) {
        Row(Modifier.fillMaxWidth().padding(14.dp), horizontalArrangement = Arrangement.SpaceBetween) {
            Column(Modifier.weight(1f)) {
                Text(transaction.draft.category, style = MaterialTheme.typography.titleMedium)
                Text("${transaction.draft.date} · ${transaction.draft.paymentMethod}")
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
private fun TransactionEditor(
    transaction: LocalTransaction?,
    master: MasterData?,
    onBack: () -> Unit,
    onSave: (TransactionDraft) -> Unit,
) {
    var date by rememberSaveable(transaction?.transactionId) { mutableStateOf(transaction?.draft?.date ?: LocalDate.now().toString()) }
    var type by rememberSaveable(transaction?.transactionId) { mutableStateOf(transaction?.draft?.type ?: "Pengeluaran") }
    var payment by rememberSaveable(transaction?.transactionId) { mutableStateOf(transaction?.draft?.paymentMethod ?: "") }
    var category by rememberSaveable(transaction?.transactionId) { mutableStateOf(transaction?.draft?.category ?: "") }
    var amount by rememberSaveable(transaction?.transactionId) { mutableStateOf(transaction?.draft?.amountIdr?.toString() ?: "") }
    var description by rememberSaveable(transaction?.transactionId) { mutableStateOf(transaction?.draft?.description ?: "") }
    val categories = master?.categories?.filter { it.kind == type }?.map { it.name }.orEmpty()
    val validDate = runCatching { LocalDate.parse(date) }.isSuccess
    val staysInWorkbook = transaction?.revision == null || date.take(4) == transaction.draft.date.take(4)
    val valid = validDate && staysInWorkbook && payment.isNotBlank() && category.isNotBlank() && (amount.toLongOrNull() ?: 0) > 0

    Scaffold(topBar = { TopAppBar(title = { Text(if (transaction == null) "Transaksi baru" else "Edit transaksi") }, navigationIcon = { TextButton(onClick = onBack) { Text("Kembali") } }) }) { padding ->
        LazyColumn(
            Modifier.fillMaxSize().padding(padding).padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            item { OutlinedTextField(date, { date = it }, label = { Text("Tanggal (YYYY-MM-DD)") }, modifier = Modifier.fillMaxWidth()) }
            item { SelectionField("Tipe", type, master?.transactionTypes.orEmpty()) { type = it; category = "" } }
            item { SelectionField("Pembayaran", payment, master?.paymentMethods.orEmpty()) { payment = it } }
            item { SelectionField("Kategori", category, categories) { category = it } }
            item {
                OutlinedTextField(
                    amount,
                    { amount = it.filter(Char::isDigit) },
                    label = { Text("Jumlah (Rp)") },
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                    modifier = Modifier.fillMaxWidth(),
                )
            }
            item { OutlinedTextField(description, { description = it }, label = { Text("Keterangan (opsional)") }, modifier = Modifier.fillMaxWidth()) }
            if (!staysInWorkbook) item { Text("Edit tidak dapat memindahkan transaksi ke tahun lain.", color = MaterialTheme.colorScheme.error) }
            item {
                Button(
                    onClick = { onSave(TransactionDraft(date, type, payment, category, amount.toLong(), description.ifBlank { null })) },
                    enabled = valid && master != null,
                    modifier = Modifier.fillMaxWidth(),
                ) { Text("Simpan") }
            }
            if (master == null) item { Text("Master belum tersedia. Hubungkan internet dan tekan Refresh.") }
        }
    }
}

@Composable
private fun SelectionField(label: String, value: String, choices: List<String>, onSelect: (String) -> Unit) {
    var expanded by remember { mutableStateOf(false) }
    Box {
        OutlinedButton(onClick = { expanded = true }, modifier = Modifier.fillMaxWidth()) {
            Text("$label: ${value.ifBlank { "Pilih" }}")
        }
        DropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
            choices.forEach { choice ->
                DropdownMenuItem(text = { Text(choice) }, onClick = { onSelect(choice); expanded = false })
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
            Text("Pembayaran: ${transaction.draft.paymentMethod}")
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

private fun formatIdr(value: Long): String =
    NumberFormat.getCurrencyInstance(Locale.forLanguageTag("id-ID")).format(value).replace(",00", "")

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
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
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
import java.text.NumberFormat
import java.time.LocalDate
import java.time.YearMonth
import java.time.format.DateTimeFormatter
import java.util.Locale

private sealed interface AppScreen {
    data object Home : AppScreen
    data class Editor(val transaction: LocalTransaction?) : AppScreen
    data class Detail(val transaction: LocalTransaction) : AppScreen
}

@Composable
fun LajurApp(viewModel: LajurViewModel) {
    val session by viewModel.session.collectAsStateWithLifecycle()
    val busy by viewModel.busy.collectAsStateWithLifecycle()
    when (val state = session) {
        SessionState.Restoring -> LoadingScreen()
        SessionState.SignedOut -> LoginScreen(viewModel.defaultServerUrl, busy, viewModel::login)
        is SessionState.SignedIn -> if (state.needsWorkbookOnboarding) {
            OnboardingScreen(busy, { viewModel.createWorkbook() }, viewModel::signOut)
        } else {
            SignedInApp(viewModel, state)
        }
    }
}

@Composable
private fun SignedInApp(viewModel: LajurViewModel, session: SessionState.SignedIn) {
    val transactions by viewModel.transactions.collectAsStateWithLifecycle()
    val summary by viewModel.summary.collectAsStateWithLifecycle()
    val master by viewModel.master.collectAsStateWithLifecycle()
    val month by viewModel.month.collectAsStateWithLifecycle()
    val query by viewModel.query.collectAsStateWithLifecycle()
    val syncCounts by viewModel.syncCounts.collectAsStateWithLifecycle()
    val message by viewModel.message.collectAsStateWithLifecycle()
    var screen: AppScreen by remember { mutableStateOf(AppScreen.Home) }
    val snackbar = remember { SnackbarHostState() }

    LaunchedEffect(message) {
        message?.let {
            snackbar.showSnackbar(it)
            viewModel.clearMessage()
        }
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
            snackbar = snackbar,
            onQueryChange = viewModel::setQuery,
            onPreviousMonth = viewModel::previousMonth,
            onNextMonth = viewModel::nextMonth,
            onRefresh = viewModel::refresh,
            onRetry = viewModel::retrySync,
            onCreateWorkbook = { viewModel.createWorkbook(month.year) },
            onAdd = { screen = AppScreen.Editor(null) },
            onTransaction = { screen = AppScreen.Detail(it) },
            onSignOut = viewModel::signOut,
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
private fun LoginScreen(defaultServerUrl: String, busy: Boolean, onLogin: (String, String, String) -> Unit) {
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
    snackbar: SnackbarHostState,
    onQueryChange: (String) -> Unit,
    onPreviousMonth: () -> Unit,
    onNextMonth: () -> Unit,
    onRefresh: () -> Unit,
    onRetry: () -> Unit,
    onCreateWorkbook: () -> Unit,
    onAdd: () -> Unit,
    onTransaction: (LocalTransaction) -> Unit,
    onSignOut: () -> Unit,
) {
    Scaffold(
        topBar = { TopAppBar(title = { Text("Lajur") }, actions = { TextButton(onClick = onSignOut) { Text("Keluar") } }) },
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

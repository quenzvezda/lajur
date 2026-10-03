package com.lajur.app.ui

import androidx.activity.compose.BackHandler
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.Checkbox
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
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
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.lajur.app.data.WorkbookTransfers
import java.time.Year
import java.time.Instant
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.util.Locale

@Composable
internal fun WorkbookRoute(viewModel: LajurViewModel, onBack: () -> Unit) {
    val state by viewModel.workbookMenu.collectAsStateWithLifecycle()
    val outboxCount by viewModel.workbookOutboxCount.collectAsStateWithLifecycle()
    val pick = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocument()) { uri ->
        uri?.let { viewModel.selectWorkbook(it.toString()) }
    }
    val saveAnnual = rememberLauncherForActivityResult(ActivityResultContracts.CreateDocument(WorkbookTransfers.XLSM_TYPE)) { uri ->
        viewModel.completeWorkbookExport(uri?.toString())
    }
    val saveAll = rememberLauncherForActivityResult(ActivityResultContracts.CreateDocument(WorkbookTransfers.ZIP_TYPE)) { uri ->
        viewModel.completeWorkbookExport(uri?.toString())
    }
    LaunchedEffect(Unit) { viewModel.loadWorkbooks() }
    WorkbookScreen(
        state, outboxCount, onBack, viewModel::loadWorkbooks, viewModel::retrySync,
        onPick = { pick.launch(arrayOf("*/*")) },
        onDiscard = viewModel::discardSelectedWorkbook,
        onCreate = viewModel::createDataWorkbook,
        onImport = viewModel::importWorkbook,
        onExport = { year ->
            viewModel.prepareWorkbookExport(year)
            if (year == null) saveAll.launch(WorkbookTransfers.fileName(null))
            else saveAnnual.launch(WorkbookTransfers.fileName(year))
        },
    )
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
internal fun WorkbookScreen(
    state: WorkbookMenuState,
    outboxCount: Int,
    onBack: () -> Unit,
    onRefresh: () -> Unit,
    onSync: () -> Unit,
    onPick: () -> Unit,
    onDiscard: () -> Unit,
    onCreate: (Int) -> Unit,
    onImport: (Int, Boolean) -> Unit,
    onExport: (Int?) -> Unit,
) {
    var yearText by rememberSaveable { mutableStateOf(Year.now().value.toString()) }
    val year = WorkbookTransfers.year(yearText)
    var replace by rememberSaveable(state.selected?.file?.name, yearText) { mutableStateOf(false) }
    var confirmImport by remember { mutableStateOf(false) }
    var confirmCreate by remember { mutableStateOf(false) }
    var warnExport by remember { mutableStateOf(false) }
    var warningYear by remember { mutableStateOf<Int?>(null) }
    val exists = state.workbooks.any { it.year == year }
    val canImport = !state.busy && state.selected != null && year != null && outboxCount == 0 && (!exists || replace)
    BackHandler { if (!state.busy) onBack() }
    fun requestExport(value: Int?) {
        if (outboxCount > 0) { warningYear = value; warnExport = true } else onExport(value)
    }
    Scaffold(topBar = {
        TopAppBar(title = { Text("Data / Workbook") }, navigationIcon = {
            TextButton(onClick = onBack, enabled = !state.busy) { Text("Kembali") }
        })
    }) { padding ->
        LazyColumn(
            Modifier.fillMaxSize().padding(padding).padding(horizontal = 16.dp).testTag("workbook-content"),
            verticalArrangement = Arrangement.spacedBy(14.dp),
        ) {
            item {
                Text("Workbook Excel adalah data utama. Impor dan ekspor memerlukan koneksi ke server.")
                Text("File ekspor mengandung data keuangan lengkap dan macro. Simpan di lokasi tepercaya.", style = MaterialTheme.typography.bodySmall)
            }
            if (state.busy) item { LinearProgressIndicator(Modifier.fillMaxWidth()); Text("Memproses workbook…") }
            state.error?.let { error -> item { Text(error, color = MaterialTheme.colorScheme.error, modifier = Modifier.testTag("workbook-error")) } }
            state.status?.let { status -> item { Text(status, modifier = Modifier.testTag("workbook-status")) } }
            item {
                Text("Perubahan lokal: $outboxCount belum tersinkron.", Modifier.testTag("workbook-pending"))
                if (outboxCount > 0) Text("Impor diblokir sampai seluruh pending/gagal selesai. Ekspor tidak memasukkan perubahan ini.")
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    OutlinedButton(onClick = onSync, enabled = !state.busy) { Text("Sync sekarang") }
                    OutlinedButton(onClick = onRefresh, enabled = !state.busy) { Text("Refresh daftar") }
                }
            }
            item { Text("Workbook di server", style = MaterialTheme.typography.titleLarge) }
            if (state.workbooks.isEmpty()) item { Text("Belum ada daftar workbook. Coba refresh atau buat/impor workbook.") }
            items(state.workbooks, key = { it.year }) { workbook ->
                Card(Modifier.fillMaxWidth()) {
                    Column(Modifier.padding(14.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                        Text(workbook.year.toString(), style = MaterialTheme.typography.titleMedium)
                        Text("${formatWorkbookSize(workbook.sizeBytes)} · ${formatWorkbookDate(workbook.lastModifiedAtUtc)}", style = MaterialTheme.typography.bodySmall)
                        OutlinedButton(onClick = { requestExport(workbook.year) }, enabled = !state.busy, modifier = Modifier.fillMaxWidth()) {
                            Text("Ekspor ${workbook.year} (.xlsm)")
                        }
                    }
                }
            }
            item {
                OutlinedButton(onClick = { requestExport(null) }, enabled = !state.busy && state.workbooks.isNotEmpty(), modifier = Modifier.fillMaxWidth()) {
                    Text("Ekspor semua tahun (.zip)")
                }
            }
            item {
                Text("Buat atau impor", style = MaterialTheme.typography.titleLarge)
                OutlinedTextField(
                    value = yearText, onValueChange = { yearText = it }, label = { Text("Tahun workbook") },
                    supportingText = { Text("1900–9999. Server memeriksa kecocokan tahun pada file impor.") },
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number), singleLine = true,
                    isError = year == null, enabled = !state.busy, modifier = Modifier.fillMaxWidth().testTag("workbook-year"),
                )
                OutlinedButton(onClick = { confirmCreate = true }, enabled = !state.busy && year != null && !exists, modifier = Modifier.fillMaxWidth()) {
                    Text("Buat workbook kosong dari template")
                }
            }
            item {
                OutlinedButton(onClick = onPick, enabled = !state.busy && outboxCount == 0, modifier = Modifier.fillMaxWidth().testTag("workbook-pick")) {
                    Text("Pilih file .xlsm untuk impor")
                }
                Text("Maksimal 20 MiB. File dipilih dahulu; belum dikirim sampai Anda mengonfirmasi impor.", style = MaterialTheme.typography.bodySmall)
            }
            state.selected?.let { selected ->
                item {
                    Card(Modifier.fillMaxWidth()) {
                        Column(Modifier.padding(14.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                            Text(selected.displayName, style = MaterialTheme.typography.titleMedium)
                            Text(formatWorkbookSize(selected.sizeBytes))
                            Row(verticalAlignment = Alignment.CenterVertically) {
                                Checkbox(replace, { replace = it }, enabled = !state.busy)
                                Text("Izinkan penggantian seluruh workbook tahun $yearText jika sudah ada.", Modifier.weight(1f))
                            }
                            if (exists) Text("Workbook $yearText sudah ada. Ini mengganti isinya, bukan menggabungkan transaksi.", color = MaterialTheme.colorScheme.error)
                            Button(onClick = { confirmImport = true }, enabled = canImport, modifier = Modifier.fillMaxWidth().testTag("workbook-import")) { Text("Periksa & impor") }
                            TextButton(onClick = onDiscard, enabled = !state.busy) { Text("Buang pilihan file") }
                        }
                    }
                }
            }
            item { Text("Workflow Excel: ekspor → edit salinan di Excel → tutup Excel → impor kembali. Jangan mengedit file aktif server secara langsung.", style = MaterialTheme.typography.bodySmall) }
        }
    }
    if (confirmCreate && year != null) AlertDialog(
        onDismissRequest = { confirmCreate = false }, title = { Text("Buat workbook $year?") },
        text = { Text("Menggunakan template server. Tidak mengganti tahun yang sudah ada.") },
        confirmButton = { TextButton(onClick = { confirmCreate = false; onCreate(year) }) { Text("Buat") } },
        dismissButton = { TextButton(onClick = { confirmCreate = false }) { Text("Batal") } },
    )
    if (confirmImport && year != null) AlertDialog(
        onDismissRequest = { confirmImport = false }, title = { Text("Impor workbook $year?") },
        text = { Text(if (replace) "Seluruh workbook tahun ini dapat diganti. Server memvalidasi file dan membuat backup sebelum penggantian. Ini bukan merge." else "Server memvalidasi file. Jika tahun sudah ada, impor ditolak tanpa penggantian.") },
        confirmButton = { TextButton(onClick = { confirmImport = false; onImport(year, replace) }, enabled = canImport) { Text("Impor") } },
        dismissButton = { TextButton(onClick = { confirmImport = false }) { Text("Batal") } },
    )
    if (warnExport) AlertDialog(
        onDismissRequest = { warnExport = false }, title = { Text("Pending tidak ikut diekspor") },
        text = { Text("Ada $outboxCount perubahan lokal belum tersinkron. File hanya memuat workbook di server. Sync dahulu untuk mendapatkan salinan terbaru.") },
        confirmButton = { TextButton(onClick = { warnExport = false; onExport(warningYear) }) { Text("Ekspor data server") } },
        dismissButton = { TextButton(onClick = { warnExport = false }) { Text("Batal") } },
    )
}

private fun formatWorkbookSize(bytes: Long): String = String.format(Locale.forLanguageTag("id-ID"), "%.1f MiB", bytes / 1048576.0)
private fun formatWorkbookDate(value: String): String = runCatching {
    DateTimeFormatter.ofPattern("dd MMM yyyy HH:mm", Locale.forLanguageTag("id-ID")).withZone(ZoneId.systemDefault()).format(Instant.parse(value))
}.getOrDefault("Waktu belum tersedia")

package com.lajur.app.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.text.selection.SelectionContainer
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.unit.dp
import com.lajur.app.BuildConfig
import com.lajur.app.scan.ScanReviewSnapshot
import com.lajur.app.scan.ScanSuggestionState
import com.lajur.app.scan.SmartScanState
import com.lajur.app.scan.extraction.ExtractedField
import com.lajur.app.scan.extraction.ExtractionConfidence
import com.lajur.app.scan.extraction.ExtractionFieldName
import com.lajur.app.scan.extraction.ExtractionSource
import com.lajur.app.scan.extraction.OcrReadingOrder
import com.lajur.app.scan.extraction.ValidatedTransactionExtraction

@OptIn(ExperimentalMaterial3Api::class)
@Composable
internal fun SmartScanScreen(
    state: SmartScanState,
    snapshot: ScanReviewSnapshot?,
    canSaveScanReport: Boolean,
    scanDiagnosticStatus: String?,
    onSaveScanReport: () -> Unit,
    onRequestSuggestions: () -> Unit,
    onCancelSuggestions: () -> Unit,
    onProcess: () -> Unit,
    onRetry: () -> Unit,
    onBack: () -> Unit,
    onManualEntry: (ValidatedTransactionExtraction?) -> Unit,
) {
    var showDetails by rememberSaveable(state is SmartScanState.Complete) { mutableStateOf(false) }
    Scaffold(topBar = {
        TopAppBar(title = { Text("Smart Scan") },
            navigationIcon = { TextButton(onClick = onBack) { Text("Kembali") } })
    }) { padding ->
        LazyColumn(
            Modifier.fillMaxSize().padding(padding).padding(horizontal = 16.dp).testTag("scan-content"),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            when (state) {
                SmartScanState.Idle -> item { Text("Tidak ada gambar yang dipilih.") }
                is SmartScanState.Ready -> item {
                    LaunchedEffect(state.request) { onProcess() }
                    Text("Menyiapkan gambar…")
                }
                is SmartScanState.Processing -> item {
                    Column(Modifier.fillMaxWidth(), horizontalAlignment = Alignment.CenterHorizontally,
                        verticalArrangement = Arrangement.spacedBy(12.dp)) {
                        CircularProgressIndicator()
                        Text("Membaca struk di perangkat…")
                        TextButton(onClick = onBack) { Text("Batalkan") }
                    }
                }
                is SmartScanState.Failed -> {
                    item { Text("Gambar belum dapat dibaca", style = MaterialTheme.typography.headlineSmall) }
                    item { Text(state.message, color = MaterialTheme.colorScheme.error) }
                    item { Button(onClick = onRetry, modifier = Modifier.fillMaxWidth()) { Text("Coba lagi") } }
                    item { OutlinedButton(onClick = onBack, modifier = Modifier.fillMaxWidth()) { Text("Buang gambar") } }
                }
                is SmartScanState.Complete -> {
                    item {
                        Text("Periksa draft transaksi", style = MaterialTheme.typography.headlineSmall)
                        Text("Hasil baca adalah draft. Lengkapi dan periksa sebelum menyimpan.",
                            style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
                    }
                    state.output.warnings.forEach { warning ->
                        item { Text(warning, color = MaterialTheme.colorScheme.error) }
                    }
                    item { ExtractionReviewCard(snapshot?.review) }
                    item {
                        Button(onClick = { onManualEntry(snapshot?.review) }, enabled = snapshot != null,
                            modifier = Modifier.fillMaxWidth().testTag("scan-edit")) { Text("Periksa & lengkapi transaksi") }
                    }
                    item {
                        Card(Modifier.fillMaxWidth()) {
                            Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                                Text("Bantuan AI lokal · opsional", style = MaterialTheme.typography.titleMedium)
                                Text(suggestionMessage(snapshot?.suggestions), style = MaterialTheme.typography.bodySmall)
                                when (snapshot?.suggestions) {
                                    ScanSuggestionState.RUNNING -> {
                                        CircularProgressIndicator()
                                        TextButton(onClick = onCancelSuggestions) { Text("Batalkan saran AI") }
                                        Text("Anda tetap bisa langsung melanjutkan ke editor.",
                                            style = MaterialTheme.typography.bodySmall)
                                    }
                                    ScanSuggestionState.COMPLETE -> Unit
                                    else -> OutlinedButton(onClick = onRequestSuggestions, enabled = snapshot != null,
                                        modifier = Modifier.fillMaxWidth().testTag("scan-request-ai")) {
                                        Text(if (snapshot?.suggestions == ScanSuggestionState.FAILED ||
                                            snapshot?.suggestions == ScanSuggestionState.UNAVAILABLE)
                                            "Coba saran AI lagi" else "Tambahkan saran AI")
                                    }
                                }
                            }
                        }
                    }
                    item {
                        TextButton(onClick = { showDetails = !showDetails }, modifier = Modifier.fillMaxWidth()
                            .testTag("scan-details-toggle")) {
                            Text(if (showDetails) "Sembunyikan teks & detail" else "Lihat teks struk & detail")
                        }
                    }
                    if (showDetails) {
                        item {
                            Card(Modifier.fillMaxWidth()) {
                                Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                                    Text("Teks struk", style = MaterialTheme.typography.titleMedium)
                                    SelectionContainer {
                                        Text(OcrReadingOrder.text(state.output.ocr).ifBlank { "Tidak ada teks yang terbaca." },
                                            Modifier.testTag("scan-raw-ocr"))
                                    }
                                    val image = state.output.image
                                    Text("${image.originalWidth}×${image.originalHeight} → ${image.processedWidth}×${image.processedHeight} · OCR ${state.output.ocr.recognitionMillis} ms",
                                        style = MaterialTheme.typography.bodySmall)
                                    snapshot?.result?.trace?.let { trace ->
                                        trace.modelLoadMillis?.let { Text("Load model: $it ms", style = MaterialTheme.typography.bodySmall) }
                                        trace.inferenceMillis?.let { Text("Inferensi model: $it ms", style = MaterialTheme.typography.bodySmall) }
                                        trace.generation?.let { Text("Token total: ${it.totalTokens ?: "?"}/${it.maxContextTokens}",
                                            style = MaterialTheme.typography.bodySmall) }
                                    }
                                }
                            }
                        }
                        if (BuildConfig.DEBUG) item {
                            Text("Laporan memuat teks struk dan saran model. Gunakan data dummy; tersimpan lokal.",
                                style = MaterialTheme.typography.bodySmall)
                            OutlinedButton(onClick = onSaveScanReport, enabled = canSaveScanReport,
                                modifier = Modifier.fillMaxWidth()) { Text("Simpan laporan uji (debug)") }
                            scanDiagnosticStatus?.let { Text(it, style = MaterialTheme.typography.bodySmall) }
                        }
                    }
                    item {
                        TextButton(onClick = onBack, modifier = Modifier.fillMaxWidth()) { Text("Buang hasil") }
                    }
                }
            }
        }
    }
}

private fun suggestionMessage(state: ScanSuggestionState?): String = when (state) {
    ScanSuggestionState.RUNNING -> "Menyiapkan saran kategori dan keterangan di perangkat…"
    ScanSuggestionState.COMPLETE -> "Saran tersedia. Periksa semua saran AI; hasilnya bukan kepastian."
    ScanSuggestionState.UNAVAILABLE -> "Model belum tersedia. Lengkapi draft secara manual; OCR tetap bisa digunakan."
    ScanSuggestionState.FAILED -> "AI belum dapat memberi saran. Draft dari struk tetap tersedia."
    ScanSuggestionState.CANCELLED -> "Saran AI dibatalkan. Anda bisa melanjutkan draft dari struk."
    else -> "Bantu menyarankan kategori, merchant, dan keterangan. Tidak diperlukan untuk mengisi transaksi."
}

@Composable
private fun ExtractionReviewCard(review: ValidatedTransactionExtraction?) {
    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
            if (review == null) { Text("Menyiapkan draft…"); return@Column }
            val extraction = review.extraction
            review.issues.filter { it.field == ExtractionFieldName.SCHEMA || it.field == ExtractionFieldName.MASTER_DATA }
                .forEach { Text(it.message, color = MaterialTheme.colorScheme.error, style = MaterialTheme.typography.bodySmall) }
            ExtractionFieldRow("Jumlah", extraction.amountIdr, extraction.amountIdr.value?.let(::formatIdr), ::formatIdr)
            ExtractionFieldRow("Tanggal", extraction.date, extraction.date.value)
            ExtractionFieldRow("Tipe", extraction.transactionType, extraction.transactionType.value)
            ExtractionFieldRow("Pembayaran (opsional)", extraction.paymentMethod, extraction.paymentMethod.value, optional = true)
            ExtractionFieldRow("Kategori", extraction.category, extraction.category.value)
            extraction.merchant.value?.let { ExtractionFieldRow("Merchant", extraction.merchant, it) }
            extraction.description.value?.let { ExtractionFieldRow("Keterangan", extraction.description, it) }
            extraction.warnings.forEach { Text(it, color = MaterialTheme.colorScheme.tertiary, style = MaterialTheme.typography.bodySmall) }
        }
    }
}

@Composable
private fun <T> ExtractionFieldRow(
    label: String, field: ExtractedField<T>, display: String?, alternativeDisplay: (T) -> String = { it.toString() },
    optional: Boolean = false,
) {
    var details by rememberSaveable { mutableStateOf(false) }
    Column(verticalArrangement = Arrangement.spacedBy(2.dp)) {
        Text(label, style = MaterialTheme.typography.labelMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
        Text(display ?: if (optional) "Belum diisi — boleh diisi nanti" else "Belum terbaca — lengkapi di editor",
            style = if (label == "Jumlah" && display != null) MaterialTheme.typography.headlineSmall else MaterialTheme.typography.bodyLarge,
            color = if (display == null && !optional) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.onSurface)
        if (display != null || field.alternatives.isNotEmpty()) {
            val hint = when {
                field.source == ExtractionSource.LOCAL_MODEL -> "Saran AI · periksa"
                field.value == null || field.confidence != ExtractionConfidence.HIGH -> "Hasil baca perlu diperiksa"
                field.source == ExtractionSource.OCR_RULES -> "Terbaca dari struk"
                else -> "Periksa nilai ini"
            }
            Text(hint, style = MaterialTheme.typography.bodySmall,
                color = if (field.source == ExtractionSource.LOCAL_MODEL) MaterialTheme.colorScheme.tertiary
                    else MaterialTheme.colorScheme.onSurfaceVariant)
        }
        if (field.evidence != null || field.alternatives.isNotEmpty()) {
            TextButton(onClick = { details = !details }) { Text(if (details) "Tutup bukti" else "Bukti & alternatif") }
            if (details) {
                field.evidence?.let { Text(it, style = MaterialTheme.typography.bodySmall) }
                if (field.alternatives.isNotEmpty()) Text("Alternatif: " +
                    field.alternatives.joinToString { alternativeDisplay(it) }, style = MaterialTheme.typography.bodySmall)
            }
        }
    }
}

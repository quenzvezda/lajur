package com.lajur.app.ui

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import com.lajur.app.domain.LocalTransaction
import com.lajur.app.domain.SyncState
import java.time.LocalDate
import java.time.ZoneOffset

@OptIn(ExperimentalMaterial3Api::class)
@Composable internal fun HistoryFilterSheet(current: HistoryFilters, rows: List<LocalTransaction>, onClose: () -> Unit, onApply: (HistoryFilters) -> Unit) {
    var draft by remember { mutableStateOf(current) }
    var minimum by remember { mutableStateOf(current.minimum?.toString().orEmpty()) }
    var maximum by remember { mutableStateOf(current.maximum?.toString().orEmpty()) }
    var dateField by remember { mutableStateOf<String?>(null) }
    val error = when {
        minimum.isNotEmpty() && minimum.toLongOrNull() == null || maximum.isNotEmpty() && maximum.toLongOrNull() == null -> "Nominal terlalu besar."
        minimum.toLongOrNull() != null && maximum.toLongOrNull() != null && minimum.toLong() > maximum.toLong() -> "Nominal minimum melebihi maksimum."
        draft.dateFrom != null && draft.dateTo != null && draft.dateFrom!! > draft.dateTo!! -> "Tanggal awal melewati tanggal akhir."
        else -> null
    }
    ModalBottomSheet(onDismissRequest = onClose, sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true)) {
        Column(Modifier.fillMaxWidth().imePadding()) {
        Column(Modifier.weight(1f, fill = false).verticalScroll(rememberScrollState()).padding(horizontal = 20.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp)) {
            Text("Filter Riwayat", style = MaterialTheme.typography.titleLarge)
            Choice("Tipe", draft.type, rows.map { it.draft.type }.distinct().sorted().map { it to it }) { draft = draft.copy(type = it, category = null) }
            Choice("Kategori", draft.category, rows.filter { draft.type == null || it.draft.type == draft.type }.map { it.draft.category }.distinct().sorted().map { it to it }) { draft = draft.copy(category = it) }
            Choice("Pembayaran", draft.payment, rows.map { it.draft.paymentMethod }.distinct().sorted().map { it to it.ifBlank { "Belum diisi" } }) { draft = draft.copy(payment = it) }
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                OutlinedButton(onClick = { dateField = "from" }, modifier = Modifier.weight(1f)) { Text(draft.dateFrom ?: "Dari tanggal") }
                OutlinedButton(onClick = { dateField = "to" }, modifier = Modifier.weight(1f)) { Text(draft.dateTo ?: "Sampai tanggal") }
            }
            if (draft.dateFrom != null || draft.dateTo != null) TextButton(onClick = { draft = draft.copy(dateFrom = null, dateTo = null) }) { Text("Kosongkan tanggal") }
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                OutlinedTextField(minimum, { minimum = editIdrInput(minimum, it) }, label = { Text("Minimum (Rp)") },
                    modifier = Modifier.weight(1f), singleLine = true, keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                    visualTransformation = IdrGroupingTransformation, textStyle = LocalTextStyle.current.copy(textAlign = TextAlign.End))
                OutlinedTextField(maximum, { maximum = editIdrInput(maximum, it) }, label = { Text("Maksimum (Rp)") },
                    modifier = Modifier.weight(1f), singleLine = true, keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                    visualTransformation = IdrGroupingTransformation, textStyle = LocalTextStyle.current.copy(textAlign = TextAlign.End))
            }
            Choice("Status sinkron", draft.sync?.name, SyncState.entries.map { it.name to when (it) {
                SyncState.SYNCED -> "Tersinkron"; SyncState.PENDING -> "Antre"; SyncState.SYNCING -> "Sedang sinkron"; SyncState.FAILED -> "Gagal"
            } }) { draft = draft.copy(sync = it?.let(SyncState::valueOf)) }
            Choice("Urutan", draft.order.name, HistoryOrder.entries.map { it.name to it.label }, allowAll = false) { draft = draft.copy(order = HistoryOrder.valueOf(it!!)) }
            Row(Modifier.fillMaxWidth()) {
                Text("Perlu dilengkapi", Modifier.weight(1f))
                Switch(draft.incompleteOnly, onCheckedChange = { draft = draft.copy(incompleteOnly = it) })
            }
            error?.let { Text(it, color = MaterialTheme.colorScheme.error) }
        }
            Row(Modifier.fillMaxWidth().padding(20.dp), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                TextButton(onClick = { draft = HistoryFilters(); minimum = ""; maximum = "" }) { Text("Reset") }
                Button(onClick = { onApply(draft.copy(minimum = minimum.toLongOrNull(), maximum = maximum.toLongOrNull())) }, enabled = error == null,
                    modifier = Modifier.weight(1f)) { Text("Terapkan") }
            }
        }
    }
    dateField?.let { field ->
        val date = if (field == "from") draft.dateFrom else draft.dateTo
        val state = rememberDatePickerState(initialSelectedDateMillis = (date?.let(LocalDate::parse) ?: LocalDate.now()).atStartOfDay(ZoneOffset.UTC).toInstant().toEpochMilli())
        DatePickerDialog(onDismissRequest = { dateField = null }, confirmButton = {
            TextButton(onClick = {
                state.selectedDateMillis?.let { millis ->
                    val value = java.time.Instant.ofEpochMilli(millis).atZone(ZoneOffset.UTC).toLocalDate().toString()
                    draft = if (field == "from") draft.copy(dateFrom = value) else draft.copy(dateTo = value)
                }; dateField = null
            }) { Text("Pilih") }
        }, dismissButton = { TextButton(onClick = { dateField = null }) { Text("Batal") } }) { DatePicker(state) }
    }
}

@Composable private fun Choice(label: String, selected: String?, choices: List<Pair<String, String>>, allowAll: Boolean = true, onChange: (String?) -> Unit) {
    var open by remember { mutableStateOf(false) }
    Box {
        OutlinedButton(onClick = { open = true }, modifier = Modifier.fillMaxWidth()) {
            Text("$label: ${choices.firstOrNull { it.first == selected }?.second ?: "Semua"}", Modifier.weight(1f)); Text("▾")
        }
        DropdownMenu(expanded = open, onDismissRequest = { open = false }, modifier = Modifier.heightIn(max = 280.dp)) {
            if (allowAll) DropdownMenuItem(text = { Text("Semua") }, onClick = { onChange(null); open = false })
            choices.forEach { (value, label) -> DropdownMenuItem(text = { Text(label) }, onClick = { onChange(value); open = false }) }
        }
    }
}

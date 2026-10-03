package com.lajur.app.ui

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.Button
import androidx.compose.material3.DatePicker
import androidx.compose.material3.DatePickerDialog
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.ExposedDropdownMenuAnchorType
import androidx.compose.material3.ExposedDropdownMenuBox
import androidx.compose.material3.ExposedDropdownMenuDefaults
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.RadioButton
import androidx.compose.material3.Scaffold
import androidx.compose.material3.SelectableDates
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.material3.rememberDatePickerState
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import com.lajur.app.domain.LocalTransaction
import com.lajur.app.domain.MasterData
import com.lajur.app.domain.TransactionDraft
import com.lajur.app.scan.extraction.ValidatedTransactionExtraction
import java.time.LocalDate
import java.time.format.DateTimeFormatter
import java.util.Locale

@OptIn(ExperimentalMaterial3Api::class)
@Composable
internal fun TransactionEditor(
    transaction: LocalTransaction?,
    initialExtraction: ValidatedTransactionExtraction?,
    master: MasterData?,
    onBack: () -> Unit,
    onSave: (TransactionDraft) -> Unit,
) {
    val extracted = initialExtraction?.extraction
    val key = transaction?.transactionId ?: extracted?.hashCode()
    var date by rememberSaveable(key) { mutableStateOf(transaction?.draft?.date
        ?: if (extracted != null) extracted.date.value.orEmpty() else LocalDate.now().toString()) }
    var type by rememberSaveable(key) { mutableStateOf(transaction?.draft?.type
        ?: if (extracted != null) extracted.transactionType.value.orEmpty() else "Pengeluaran") }
    var payment by rememberSaveable(key) { mutableStateOf(transaction?.draft?.paymentMethod ?: extracted?.paymentMethod?.value.orEmpty()) }
    var category by rememberSaveable(key) { mutableStateOf(transaction?.draft?.category ?: extracted?.category?.value.orEmpty()) }
    var amount by rememberSaveable(key) { mutableStateOf(transaction?.draft?.amountIdr?.toString() ?: extracted?.amountIdr?.value?.toString().orEmpty()) }
    var description by rememberSaveable(key) { mutableStateOf(transaction?.draft?.description ?: extracted?.description?.value.orEmpty()) }
    val originalYear = transaction?.takeIf { it.revision != null }?.draft?.date?.take(4)?.toIntOrNull()
    val input = TransactionEditorInput(date, type, payment, category, amount, description)
    val errors = input.errors(master, originalYear)
    val categories = master?.categories?.filter { it.kind == type }?.map { it.name }.orEmpty()
    val focusManager = LocalFocusManager.current

    Scaffold(topBar = {
        TopAppBar(title = { Text(if (transaction == null) "Transaksi baru" else "Edit transaksi") },
            navigationIcon = { TextButton(onClick = onBack) { Text("Kembali") } })
    }) { padding ->
        LazyColumn(
            Modifier.fillMaxSize().padding(padding).imePadding().padding(horizontal = 16.dp).testTag("editor-content"),
            verticalArrangement = Arrangement.spacedBy(16.dp),
        ) {
            if (initialExtraction != null) item {
                Text("Periksa hasil scan dan lengkapi kolom yang kosong sebelum menyimpan.",
                    color = MaterialTheme.colorScheme.tertiary, style = MaterialTheme.typography.bodyMedium)
            }
            item {
                OutlinedTextField(
                    value = amount, onValueChange = { amount = editIdrInput(amount, it) },
                    label = { Text("Jumlah") }, prefix = { Text("Rp ") }, singleLine = true,
                    textStyle = MaterialTheme.typography.headlineSmall,
                    visualTransformation = IdrGroupingTransformation,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number, imeAction = ImeAction.Next),
                    keyboardActions = KeyboardActions(onNext = { focusManager.clearFocus() }),
                    isError = amount.isNotBlank() && "amount" in errors,
                    supportingText = { Text(if (amount.isNotBlank() && "amount" in errors)
                        errors.getValue("amount") else "Rupiah bulat, tanpa sen.") },
                    modifier = Modifier.fillMaxWidth().testTag("editor-amount"),
                )
            }
            item { TransactionDateField(date, originalYear) { date = it } }
            item {
                SelectionField("Tipe", type, master?.transactionTypes.orEmpty(),
                    error = errors["type"]?.takeIf { type.isNotBlank() }) {
                    if (type != it) { type = it; category = "" }
                }
            }
            item {
                SelectionField("Pembayaran", payment, master?.paymentMethods.orEmpty(), optional = true,
                    error = errors["payment"]?.takeIf { payment.isNotBlank() }) { payment = it }
            }
            item {
                SelectionField("Kategori", category, categories,
                    error = errors["category"]?.takeIf { category.isNotBlank() },
                    searchable = true, contextLabel = "Kategori untuk " + type.ifBlank { "tipe yang dipilih" }) { category = it }
            }
            item {
                OutlinedTextField(description, { description = it },
                    label = { Text("Keterangan (opsional)") }, maxLines = 3,
                    keyboardOptions = KeyboardOptions(imeAction = ImeAction.Done),
                    keyboardActions = KeyboardActions(onDone = { focusManager.clearFocus() }),
                    modifier = Modifier.fillMaxWidth().testTag("editor-description"))
            }
            item {
                errors["master"]?.let { Text(it, color = MaterialTheme.colorScheme.error) }
                if (errors.isNotEmpty() && master != null) {
                    val names = mapOf("date" to "tanggal", "type" to "tipe", "payment" to "pembayaran",
                        "category" to "kategori", "amount" to "jumlah")
                    Text("Lengkapi atau periksa: " + errors.keys.mapNotNull(names::get).joinToString() + ".",
                        style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
                Button(
                    onClick = { input.toDraft(master, originalYear)?.let(onSave) },
                    enabled = errors.isEmpty(), modifier = Modifier.fillMaxWidth().testTag("editor-save"),
                ) { Text("Simpan transaksi") }
                Spacer(Modifier.height(24.dp))
            }
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
internal fun SelectionField(
    label: String,
    value: String,
    choices: List<String>,
    error: String? = null,
    searchable: Boolean = false,
    contextLabel: String? = null,
    optional: Boolean = false,
    onSelect: (String) -> Unit,
) {
    var expanded by rememberSaveable(label) { mutableStateOf(false) }
    if (searchable) {
        PickerButton(label, value.ifBlank { "Pilih " + label.lowercase() }, choices.isNotEmpty(),
            "selection-$label", error) { expanded = true }
        if (expanded) {
            var query by rememberSaveable { mutableStateOf("") }
            val filtered = remember(choices, query) { filterSelectionChoices(choices, query).distinct() }
            ModalBottomSheet(
                onDismissRequest = { expanded = false },
                sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true),
            ) {
                Column(Modifier.fillMaxWidth().heightIn(max = 560.dp).padding(horizontal = 20.dp),
                    verticalArrangement = Arrangement.spacedBy(12.dp)) {
                    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                        Text("Pilih " + label.lowercase(), Modifier.weight(1f), style = MaterialTheme.typography.titleLarge)
                        TextButton(onClick = { expanded = false }) { Text("Tutup") }
                    }
                    contextLabel?.let { Text(it, style = MaterialTheme.typography.bodySmall) }
                    OutlinedTextField(query, { query = it }, label = { Text("Cari " + label.lowercase()) },
                        singleLine = true, modifier = Modifier.fillMaxWidth().testTag("selection-search"),
                        trailingIcon = {
                            if (query.isNotEmpty()) TextButton(onClick = { query = "" }) { Text("Hapus") }
                        })
                    LazyColumn(Modifier.fillMaxWidth().weight(1f).testTag("selection-results")) {
                        if (filtered.isEmpty()) item {
                            Text("Kategori tidak ditemukan. Coba kata lain.", Modifier.padding(vertical = 16.dp))
                        }
                        items(filtered, key = { it }) { choice ->
                            Row(
                                Modifier.fillMaxWidth().heightIn(min = 56.dp)
                                    .selectable(selected = value == choice, role = Role.RadioButton) {
                                        onSelect(choice); expanded = false
                                    },
                                verticalAlignment = Alignment.CenterVertically,
                            ) {
                                Text(choice, Modifier.weight(1f), style = MaterialTheme.typography.bodyLarge)
                                RadioButton(selected = value == choice, onClick = null)
                            }
                        }
                    }
                    Spacer(Modifier.height(12.dp))
                }
            }
        }
    } else {
        ExposedDropdownMenuBox(
            expanded = expanded,
            onExpandedChange = { if (optional || choices.isNotEmpty()) expanded = it },
            modifier = Modifier.fillMaxWidth(),
        ) {
            OutlinedTextField(
                value = value, onValueChange = {}, readOnly = true, singleLine = true,
                label = { Text(if (optional) "$label (opsional)" else label) },
                placeholder = { Text(if (optional) "Belum diisi" else "Pilih " + label.lowercase()) },
                trailingIcon = { ExposedDropdownMenuDefaults.TrailingIcon(expanded) },
                isError = error != null,
                supportingText = if (error != null || optional || value.isBlank()) ({
                    Text(error ?: if (optional) "Opsional. Bisa diisi nanti."
                        else if (choices.isEmpty()) "Daftar belum tersedia." else "Wajib dipilih.")
                }) else null,
                modifier = Modifier.fillMaxWidth().menuAnchor(ExposedDropdownMenuAnchorType.PrimaryNotEditable)
                    .testTag("selection-$label"),
            )
            ExposedDropdownMenu(expanded, onDismissRequest = { expanded = false }) {
                if (optional) DropdownMenuItem(
                    text = { Text("Kosongkan pembayaran") },
                    onClick = { onSelect(""); expanded = false },
                    contentPadding = ExposedDropdownMenuDefaults.ItemContentPadding,
                )
                choices.forEach { choice ->
                    DropdownMenuItem(text = { Text(choice) }, onClick = { onSelect(choice); expanded = false },
                        contentPadding = ExposedDropdownMenuDefaults.ItemContentPadding)
                }
            }
        }
    }
}

@Composable
private fun PickerButton(label: String, value: String, enabled: Boolean, tag: String, error: String?, onClick: () -> Unit) {
    Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
        OutlinedButton(
            onClick = onClick, enabled = enabled, shape = MaterialTheme.shapes.extraSmall,
            border = BorderStroke(1.dp, if (error != null) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.outline),
            modifier = Modifier.fillMaxWidth().heightIn(min = 64.dp).testTag(tag),
        ) {
            Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                Text(label, style = MaterialTheme.typography.labelMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
                Text(value, style = MaterialTheme.typography.bodyLarge, color = MaterialTheme.colorScheme.onSurface)
            }
            Text("Pilih", style = MaterialTheme.typography.labelLarge)
        }
        error?.let { Text(it, color = MaterialTheme.colorScheme.error, style = MaterialTheme.typography.bodySmall) }
        if (!enabled) Text("Pilih tipe terlebih dahulu atau muat Master.", style = MaterialTheme.typography.bodySmall)
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun TransactionDateField(date: String, originalYear: Int?, onSelect: (String) -> Unit) {
    var showPicker by rememberSaveable { mutableStateOf(false) }
    val display = runCatching { LocalDate.parse(date).format(
        DateTimeFormatter.ofPattern("d MMMM yyyy", Locale.forLanguageTag("id-ID"))) }.getOrDefault("Pilih tanggal transaksi")
    PickerButton("Tanggal", display, true, "editor-date", null) { showPicker = true }
    if (showPicker) {
        val picker = rememberDatePickerState(
            initialSelectedDateMillis = dateToPickerMillis(date),
            selectableDates = object : SelectableDates {
                override fun isSelectableYear(year: Int) = originalYear == null || year == originalYear
                override fun isSelectableDate(utcTimeMillis: Long) = isSelectableYear(pickerMillisToDate(utcTimeMillis).take(4).toInt())
            },
        )
        DatePickerDialog(onDismissRequest = { showPicker = false },
            confirmButton = {
                TextButton(enabled = picker.selectedDateMillis != null, onClick = {
                    picker.selectedDateMillis?.let { onSelect(pickerMillisToDate(it)) }; showPicker = false
                }) { Text("Pilih tanggal") }
            },
            dismissButton = { TextButton(onClick = { showPicker = false }) { Text("Batal") } },
        ) { DatePicker(state = picker) }
    }
}

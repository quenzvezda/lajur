package com.lajur.app.ui

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyListState
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.luminance
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import com.lajur.app.data.remote.*
import com.lajur.app.domain.SyncCounts
import java.time.Instant
import java.time.YearMonth
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.util.Locale

internal fun monthlyPayments(month: MonthReportDto): List<Pair<String, Long>> =
    month.rows.filter { it.type == "Pengeluaran" }.groupBy { it.paymentMethod.ifBlank { "Belum diisi" } }
        .map { (name, rows) -> name to rows.sumOf { it.amountIdr } }.sortedByDescending { it.second }

internal fun dailyExpenses(month: MonthReportDto, days: Int): List<Long> = (1..days).map { day ->
    month.rows.filter { it.type == "Pengeluaran" && it.date.takeLast(2).toIntOrNull() == day }.sumOf { it.amountIdr }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
internal fun InsightScreen(
    month: YearMonth, state: InsightState, counts: SyncCounts,
    onPrevious: () -> Unit, onNext: () -> Unit, onRefresh: () -> Unit,
    onPeriod: (YearMonth) -> Unit,
    onHistory: (String) -> Unit, onRow: (ReportRowDto) -> Unit,
    bottomBar: @Composable () -> Unit,
) {
    var tab by rememberSaveable { mutableIntStateOf(0) }
    var pickPeriod by remember { mutableStateOf(false) }
    val report = state.cached?.report?.takeIf { it.year == month.year }
    val selected = report?.months?.firstOrNull { it.month == month.monthValue }
    val listState = remember(tab, month) { LazyListState() }
    Scaffold(topBar = { TopAppBar(title = { Text("Insight") }, actions = {
        TextButton(onClick = onRefresh, enabled = !state.loading) { Text("Perbarui") }
    }) }, bottomBar = bottomBar) { padding ->
        Column(Modifier.fillMaxSize().padding(padding)) {
            Row(Modifier.fillMaxWidth().padding(horizontal = 16.dp), verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.SpaceBetween) {
                TextButton(onClick = onPrevious, modifier = Modifier.semantics { contentDescription = "Bulan sebelumnya" }) { Text("‹") }
                TextButton(onClick = { pickPeriod = true }) {
                    Text(month.format(DateTimeFormatter.ofPattern("MMMM yyyy", Locale.forLanguageTag("id-ID"))), style = MaterialTheme.typography.titleMedium)
                }
                TextButton(onClick = onNext, modifier = Modifier.semantics { contentDescription = "Bulan berikutnya" }) { Text("›") }
            }
            PrimaryTabRow(selectedTabIndex = tab) {
                listOf("Ringkasan", "Kategori", "Tren").forEachIndexed { index, label ->
                    Tab(selected = tab == index, onClick = { tab = index }, text = { Text(label) })
                }
            }
            if (state.loading) LinearProgressIndicator(Modifier.fillMaxWidth())
            LazyColumn(Modifier.fillMaxSize().testTag("insight-content"), contentPadding = PaddingValues(16.dp),
                state = listState,
                verticalArrangement = Arrangement.spacedBy(20.dp)) {
                item {
                    Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
                        val updated = state.cached?.fetchedAt?.let { instant -> runCatching {
                            DateTimeFormatter.ofPattern("dd MMM, HH:mm", Locale.forLanguageTag("id-ID"))
                                .format(Instant.parse(instant).atZone(ZoneId.systemDefault()))
                        }.getOrNull() }
                        Text(if (updated == null) "Laporan workbook" else "${if (state.offline) "Salinan offline" else "Data server"} · $updated",
                            style = MaterialTheme.typography.labelMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
                        if (counts.pending + counts.failed > 0) Text("${counts.pending + counts.failed} transaksi antre/gagal belum tercakup. Perbarui setelah sinkron.",
                            style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.error)
                        state.error?.let { Text(it, style = MaterialTheme.typography.bodySmall) }
                    }
                }
                if (selected == null) item {
                    Text(if (state.loading) "Memuat laporan…" else "Belum ada laporan untuk tahun ini.")
                    TextButton(onClick = onRefresh, enabled = !state.loading) { Text("Coba lagi") }
                } else when (tab) {
                    0 -> {
                        item { MonthlySummary(selected) }
                        if (selected.openingBalanceIdr != null || selected.actualBalanceIdr != null) item {
                            Section("Saldo workbook") {
                                MoneyLine("Saldo awal", selected.openingBalanceIdr)
                                MoneyLine("Saldo penutup (estimasi)", selected.estimatedClosingIdr)
                                MoneyLine("Saldo aktual", selected.actualBalanceIdr)
                                MoneyLine("Selisih saldo", selected.balanceDifferenceIdr)
                            }
                        }
                        item { ExpenseChart(selected, month) }
                        selected.pace?.let { pace -> item {
                            Section("Laju belanja") {
                                MoneyLine("Rata-rata per hari", pace.averagePerDayIdr)
                                MoneyLine("Perkiraan akhir bulan", pace.projectedMonthEndIdr)
                                MoneyLine("Budget Master", pace.totalMasterBudgetIdr)
                                Text("Perkiraan dari laju belanja, bukan transaksi baru.", style = MaterialTheme.typography.bodySmall)
                            }
                        } }
                    }
                    1 -> {
                        item { Text("Pengeluaran per kategori", style = MaterialTheme.typography.titleMedium) }
                        if (selected.expenseByCategory.isEmpty()) item { Text("Belum ada pengeluaran pada bulan ini.") }
                        items(selected.expenseByCategory.sortedByDescending { it.amountIdr }, key = { it.category }) { category ->
                            Column(Modifier.fillMaxWidth().clickable { onHistory(category.category) }, verticalArrangement = Arrangement.spacedBy(6.dp)) {
                                MoneyLine(category.category, category.amountIdr)
                                val budget = category.monthlyBudgetIdr
                                val denominator = (selected.expenseByCategory.maxOfOrNull { it.amountIdr } ?: 0L).coerceAtLeast(1)
                                LinearProgressIndicator(progress = { (category.amountIdr.toDouble() / denominator).toFloat().coerceIn(0f, 1f) },
                                    modifier = Modifier.fillMaxWidth(), color = if (budget != null && budget > 0 && category.amountIdr > budget)
                                        MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.primary)
                                Text(if (budget != null && budget > 0) "${percent(category.amountIdr.toDouble() / budget)} dari ${formatIdr(budget)} · ${category.transactionCount} transaksi"
                                    else "${category.transactionCount} transaksi · tanpa budget", style = MaterialTheme.typography.bodySmall,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant)
                            }
                        }
                        item { Section("Metode pembayaran") {
                            val payments = monthlyPayments(selected)
                            if (payments.isEmpty()) Text("Belum ada pengeluaran.")
                            payments.forEach { (name, value) -> MoneyLine(name, value) }
                        } }
                        item { Text("5 belanja terbesar", style = MaterialTheme.typography.titleMedium) }
                        items(selected.rows.filter { it.type == "Pengeluaran" }.sortedByDescending { it.amountIdr }.take(5)) { row ->
                            Column(Modifier.fillMaxWidth().clickable { onRow(row) }, verticalArrangement = Arrangement.spacedBy(4.dp)) {
                                MoneyLine(row.description?.takeIf { it.isNotBlank() } ?: row.category, row.amountIdr)
                                Text("${row.date} · ${row.category} · ${row.paymentMethod.ifBlank { "Belum diisi" }}", style = MaterialTheme.typography.bodySmall,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant)
                            }
                        }
                    }
                    else -> {
                        item { Section("Sepanjang ${report.year}") {
                            MoneyLine("Pemasukan", report.incomeIdr)
                            MoneyLine("Pengeluaran", report.expenseIdr)
                            MoneyLine("Selisih", report.netIdr)
                        } }
                        item { Text("Pengeluaran antarbulan", style = MaterialTheme.typography.titleMedium) }
                        val maximum = report.months.maxOfOrNull { it.expenseIdr }?.coerceAtLeast(1) ?: 1
                        items(report.months, key = { it.month }) { value ->
                            Column(verticalArrangement = Arrangement.spacedBy(5.dp)) {
                                MoneyLine(YearMonth.of(report.year, value.month).format(DateTimeFormatter.ofPattern("MMM", Locale.forLanguageTag("id-ID"))), value.expenseIdr)
                                LinearProgressIndicator(progress = { (value.expenseIdr.toDouble() / maximum).toFloat().coerceIn(0f, 1f) }, modifier = Modifier.fillMaxWidth())
                                Text("Masuk ${formatIdr(value.incomeIdr)} · selisih ${formatIdr(value.netIdr)}", style = MaterialTheme.typography.bodySmall)
                            }
                        }
                        item { Projection(selected) }
                    }
                }
            }
        }
    }
    if (pickPeriod) PeriodPicker(month, onClose = { pickPeriod = false }, onSelect = { onPeriod(it); pickPeriod = false })
}

@Composable private fun PeriodPicker(current: YearMonth, onClose: () -> Unit, onSelect: (YearMonth) -> Unit) {
    var year by remember { mutableIntStateOf(current.year) }
    AlertDialog(onDismissRequest = onClose, title = { Text("Bulan dan tahun") }, text = {
        Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.SpaceBetween) {
                TextButton(onClick = { year-- }, enabled = year > 1900) { Text("‹") }
                Text("$year", style = MaterialTheme.typography.titleMedium)
                TextButton(onClick = { year++ }, enabled = year < 9999) { Text("›") }
            }
            for (row in 0..3) Row(Modifier.fillMaxWidth()) {
                for (column in 1..3) {
                    val value = YearMonth.of(year, row * 3 + column)
                    TextButton(onClick = { onSelect(value) }, modifier = Modifier.weight(1f)) {
                        Text(value.format(DateTimeFormatter.ofPattern("MMM", Locale.forLanguageTag("id-ID"))))
                    }
                }
            }
        }
    }, confirmButton = { TextButton(onClick = onClose) { Text("Tutup") } })
}

private fun percent(value: Double?): String = value?.let { String.format(Locale.forLanguageTag("id-ID"), "%.1f%%", it * 100) } ?: "—"

@Composable private fun Section(title: String, content: @Composable ColumnScope.() -> Unit) {
    Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Text(title, style = MaterialTheme.typography.titleMedium)
        content()
    }
}

@Composable private fun MoneyLine(label: String, value: Long?, color: Color = MaterialTheme.colorScheme.onSurface) {
    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(12.dp), verticalAlignment = Alignment.Top) {
        Text(label, Modifier.weight(1f), style = MaterialTheme.typography.bodyMedium)
        Text(value?.let(::formatIdr) ?: "—", style = MaterialTheme.typography.titleSmall, color = color)
    }
}

@Composable private fun MonthlySummary(month: MonthReportDto) {
    val dark = MaterialTheme.colorScheme.surface.luminance() < .5f
    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            Text("Selisih bulan ini", style = MaterialTheme.typography.labelLarge)
            Text(formatIdr(month.netIdr), style = MaterialTheme.typography.headlineMedium)
            MoneyLine("Pemasukan", month.incomeIdr, if (dark) Color(0xFF81C784) else Color(0xFF18733C))
            MoneyLine("Pengeluaran", month.expenseIdr, if (dark) Color(0xFFFFAB91) else Color(0xFFB3261E))
            Text("Saving rate ${percent(month.savingRate)} · ${month.transactionCount} transaksi", style = MaterialTheme.typography.bodySmall)
            Text("Selisih adalah pemasukan dikurangi pengeluaran.", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
    }
}

@Composable private fun Projection(month: MonthReportDto) {
    Section("Proyeksi bulan terpilih") {
        val projection = month.projection
        if (projection == null) Text("Proyeksi belum tersedia untuk bulan ini.") else {
            projection.sources.forEach { source ->
                Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
                    MoneyLine(source.label, source.estimateIdr)
                    Text("${source.payoutDay?.let { "Tanggal $it · " } ?: ""}Tercatat ${source.actualIdr?.let(::formatIdr) ?: "—"} · sisa ${source.remainingIdr?.let(::formatIdr) ?: "—"}", style = MaterialTheme.typography.bodySmall)
                }
            }
            MoneyLine("Perkiraan pemasukan", projection.projectedIncomeIdr)
            MoneyLine("Perkiraan pengeluaran", projection.projectedExpenseIdr)
            MoneyLine("Perkiraan selisih", projection.projectedNetIdr)
            if (!projection.complete) Text("Sebagian formula proyeksi belum dikenali; nilai yang tidak tersedia ditampilkan —.", style = MaterialTheme.typography.bodySmall)
            Text("Proyeksi adalah perkiraan, bukan pemasukan yang sudah diterima.", style = MaterialTheme.typography.bodySmall)
        }
    }
}

@Composable private fun ExpenseChart(month: MonthReportDto, period: YearMonth) {
    var cumulative by rememberSaveable { mutableStateOf(false) }
    var selectedDay by remember(period) { mutableStateOf<Int?>(null) }
    val daily = remember(month, period) { dailyExpenses(month, period.lengthOfMonth()) }
    val cumulativeValues = remember(daily) { var total = 0L; daily.map { total += it; total } }
    val values = if (cumulative) cumulativeValues else daily
    val max = values.maxOrNull()?.coerceAtLeast(1) ?: 1
    val accent = MaterialTheme.colorScheme.primary
    val grid = MaterialTheme.colorScheme.outlineVariant
    Section("Pengeluaran harian") {
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            FilterChip(selected = !cumulative, onClick = { cumulative = false }, label = { Text("Harian") })
            FilterChip(selected = cumulative, onClick = { cumulative = true }, label = { Text("Kumulatif") })
        }
        Text("Skala maksimum ${formatIdr(max)}", style = MaterialTheme.typography.labelSmall)
        Canvas(Modifier.fillMaxWidth().height(160.dp).semantics { contentDescription = "Grafik pengeluaran ${if (cumulative) "kumulatif" else "harian"}. Ketuk untuk detail tanggal." }
            .pointerInput(values) { detectTapGestures { point -> selectedDay = (point.x / size.width * values.size).toInt().coerceIn(0, values.lastIndex) } }) {
            for (line in 0..3) {
                val y = size.height * line / 3
                drawLine(grid, Offset(0f, y), Offset(size.width, y))
            }
            val step = size.width / values.size
            if (cumulative) {
                val path = Path()
                values.forEachIndexed { index, value ->
                    val x = step * (index + .5f); val y = size.height * (1 - value.toDouble() / max).toFloat()
                    if (index == 0) path.moveTo(x, y) else path.lineTo(x, y)
                }
                drawPath(path, accent, style = Stroke(2.dp.toPx()))
            } else values.forEachIndexed { index, value ->
                val height = size.height * (value.toDouble() / max).toFloat()
                drawRect(accent, Offset(step * index + 1, size.height - height), Size((step - 2).coerceAtLeast(1f), height))
            }
        }
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
            Text("1", style = MaterialTheme.typography.labelSmall)
            Text("Ketuk grafik untuk detail", style = MaterialTheme.typography.labelSmall)
            Text("${period.lengthOfMonth()}", style = MaterialTheme.typography.labelSmall)
        }
    }
    selectedDay?.let { index -> AlertDialog(onDismissRequest = { selectedDay = null },
        title = { Text("${index + 1} ${period.format(DateTimeFormatter.ofPattern("MMMM", Locale.forLanguageTag("id-ID")))}") },
        text = { Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
            MoneyLine("Harian", daily[index]); MoneyLine("Kumulatif", cumulativeValues[index])
        } }, confirmButton = { TextButton(onClick = { selectedDay = null }) { Text("Tutup") } }) }
}

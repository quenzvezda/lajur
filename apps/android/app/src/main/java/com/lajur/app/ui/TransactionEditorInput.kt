package com.lajur.app.ui

import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.input.OffsetMapping
import androidx.compose.ui.text.input.TransformedText
import androidx.compose.ui.text.input.VisualTransformation
import com.lajur.app.domain.MasterData
import com.lajur.app.domain.TransactionDraft
import java.time.LocalDate
import java.time.ZoneOffset

internal data class TransactionEditorInput(
    val date: String,
    val type: String,
    val payment: String,
    val category: String,
    val amount: String,
    val description: String,
) {
    fun errors(master: MasterData?, originalYear: Int?): Map<String, String> = buildMap {
        val parsedDate = runCatching { LocalDate.parse(date) }.getOrNull()
        if (parsedDate == null) put("date", "Pilih tanggal transaksi.")
        else if (originalYear != null && parsedDate.year != originalYear)
            put("date", "Edit tidak dapat memindahkan transaksi ke tahun lain.")
        if (master == null) put("master", "Master belum tersedia. Hubungkan internet dan tekan Refresh.")
        if (type !in master?.transactionTypes.orEmpty()) put("type", "Pilih tipe transaksi dari daftar.")
        if (payment.isNotBlank() && payment.trim() !in master?.paymentMethods.orEmpty())
            put("payment", "Pilih metode pembayaran dari daftar atau kosongkan.")
        if (master?.categories?.none { it.name == category && it.kind == type } != false)
            put("category", "Pilih kategori yang sesuai dengan tipe transaksi.")
        if (amount.isEmpty() || amount.any { it !in '0'..'9' } || (amount.toLongOrNull() ?: 0) <= 0)
            put("amount", "Isi jumlah rupiah bulat, lebih dari 0, dan dalam batas yang didukung.")
    }

    fun toDraft(master: MasterData?, originalYear: Int?): TransactionDraft? =
        if (errors(master, originalYear).isNotEmpty()) null
        else TransactionDraft(date, type, payment.trim(), category, amount.toLong(), description.trim().ifBlank { null })
}

internal fun filterSelectionChoices(choices: List<String>, query: String): List<String> {
    val words = query.trim().split(Regex("\\s+")).filter(String::isNotEmpty)
    return choices.filter { choice -> words.all { choice.contains(it, ignoreCase = true) } }
}

/** Accept digits or pasted Indonesian grouping, never silently turn decimals/negatives into money. */
internal fun editIdrInput(current: String, proposed: String): String = when {
    proposed.all { it in '0'..'9' } -> proposed
    Regex("[0-9]{1,3}(\\.[0-9]{3})+").matches(proposed) -> proposed.replace(".", "")
    else -> current
}

internal fun dateToPickerMillis(date: String): Long? = runCatching {
    LocalDate.parse(date).atStartOfDay(ZoneOffset.UTC).toInstant().toEpochMilli()
}.getOrNull()

internal fun pickerMillisToDate(millis: Long): String =
    java.time.Instant.ofEpochMilli(millis).atZone(ZoneOffset.UTC).toLocalDate().toString()

internal object IdrGroupingTransformation : VisualTransformation {
    override fun filter(text: AnnotatedString): TransformedText {
        val raw = text.text
        val grouped = raw.reversed().chunked(3).joinToString(".").reversed()
        val mapping = object : OffsetMapping {
            override fun originalToTransformed(offset: Int): Int {
                if (raw.isEmpty() || offset == 0) return 0
                val before = (raw.length - 1) / 3 - (raw.length - offset) / 3
                return offset + before
            }
            override fun transformedToOriginal(offset: Int): Int = grouped.take(offset).count { it != '.' }
        }
        return TransformedText(AnnotatedString(grouped), mapping)
    }
}

package com.lajur.app.scan.extraction

import com.lajur.app.domain.MasterData
import java.time.LocalDate

private val DATE_TOKEN = Regex("(?<![\\d/.-])(?:(\\d{1,2})([/.-])(\\d{1,2})\\2(\\d{4})|(\\d{4})-(\\d{2})-(\\d{2}))(?![\\d/.-])")
private val NAMED_DATE_TOKEN = Regex("(?<!\\d)(\\d{1,2})\\s+([A-Za-z]+)\\.?\\s+(\\d{4})(?!\\d)")
private val MONTH_NAMES = listOf(
    listOf("jan", "januari", "january"), listOf("feb", "februari", "february"),
    listOf("mar", "maret", "march"), listOf("apr", "april"), listOf("mei", "may"),
    listOf("jun", "juni", "june"), listOf("jul", "juli", "july"),
    listOf("agu", "agustus", "aug", "august"), listOf("sep", "sept", "september"),
    listOf("okt", "oktober", "oct", "october"), listOf("nov", "november"),
    listOf("des", "desember", "dec", "december"),
).flatMapIndexed { index, names -> names.map { it to index + 1 } }.toMap()
private val NON_TRANSACTION_DATE = Regex("(?i)\\b(?:kedaluwarsa|kadaluarsa|expired|expiry|berlaku|jatuh\\s+tempo)\\b")
private val TOTAL_LINE = Regex("(?i)^(?:GRAND\\s+TOTAL|TOTAL(?:\\s+(?:BAYAR|PEMBAYARAN|TAGIHAN|BELANJA))?)\\s*[:=]?\\s*(.*)$")
private val OTHER_AMOUNT_LINE = Regex("(?i)^(?:NOMINAL(?:\\s+TRANSFER)?|JUMLAH\\s+PEMBAYARAN|BIAYA\\s+PERJALANAN)\\s*[:=]?\\s*(.*)$")
private val RUPIAH = Regex("(?i)^(?:(?:RP|IDR)\\.?\\s*)?([1-9]\\d{0,2}(?:\\.\\d{3})+|[1-9]\\d*)(?:,00)?$")

/** Explicit evidence only: never selects the largest number or guesses a category. */
class EvidenceTransactionExtractor {
    fun extract(text: String, master: MasterData?): TransactionExtraction {
        val rows = text.lineSequence().map(String::trim).filter(String::isNotEmpty).toList()
        val dates = rows.filterNot { NON_TRANSACTION_DATE.containsMatchIn(it) }.flatMap { row ->
            val numeric = DATE_TOKEN.findAll(row).mapNotNull { match ->
                val groups = match.groupValues
                val date = runCatching {
                    if (groups[1].isNotEmpty()) LocalDate.of(groups[4].toInt(), groups[3].toInt(), groups[1].toInt())
                    else LocalDate.of(groups[5].toInt(), groups[6].toInt(), groups[7].toInt())
                }.getOrNull()
                date?.toString()?.let { it to row }
            }.toList()
            val named = NAMED_DATE_TOKEN.findAll(row).mapNotNull { match ->
                val month = MONTH_NAMES[match.groupValues[2].lowercase(java.util.Locale.ROOT)]
                    ?: return@mapNotNull null
                val date = runCatching {
                    LocalDate.of(match.groupValues[3].toInt(), month, match.groupValues[1].toInt())
                }.getOrNull()
                date?.toString()?.let { it to row }
            }.toList()
            numeric + named
        }.distinctBy { it.first }
        val totalRows = rows.filter { TOTAL_LINE.matches(it) }
        val amountRows = if (totalRows.isNotEmpty()) totalRows else rows.filter { OTHER_AMOUNT_LINE.matches(it) }
        val totals = amountRows.mapNotNull { row ->
            val suffix = (TOTAL_LINE.matchEntire(row) ?: OTHER_AMOUNT_LINE.matchEntire(row))
                ?.groupValues?.get(1) ?: return@mapNotNull null
            val amount = RUPIAH.matchEntire(suffix)?.groupValues?.get(1)
                ?.replace(".", "")?.toLongOrNull() ?: return@mapNotNull null
            amount to row
        }.distinctBy { it.first }
        val receipt = totals.isNotEmpty() && rows.any { it.startsWith("SUBTOTAL", ignoreCase = true) } &&
            rows.any { it.startsWith("KEMBALI", ignoreCase = true) }
        val type = if (receipt) master?.transactionTypes?.firstOrNull {
            it.equals("Pengeluaran", ignoreCase = true)
        } else null
        return TransactionExtraction(
            documentKind = if (receipt) DocumentKind.RECEIPT else DocumentKind.UNKNOWN,
            date = candidates(dates),
            amountIdr = candidates(totals),
            transactionType = if (type == null) ExtractedField() else ExtractedField(
                value = type,
                confidence = ExtractionConfidence.MEDIUM,
                evidence = "SUBTOTAL / TOTAL / KEMBALI",
                source = ExtractionSource.OCR_RULES,
            ),
            warnings = buildList {
                if (dates.size > 1) add("Ada beberapa tanggal di OCR; pilih tanggal transaksi.")
                if (totals.size > 1) add("Ada beberapa TOTAL berbeda di OCR; pilih jumlah transaksi.")
            },
        )
    }

    fun merge(model: TransactionExtraction, rules: TransactionExtraction): TransactionExtraction = model.copy(
        documentKind = if (rules.documentKind != DocumentKind.UNKNOWN) rules.documentKind else model.documentKind,
        date = preferEvidence(model.date, rules.date),
        amountIdr = preferEvidence(model.amountIdr, rules.amountIdr),
        paymentMethod = preferEvidence(model.paymentMethod, rules.paymentMethod),
        transactionType = if (model.transactionType.value == null) rules.transactionType else model.transactionType,
        warnings = (model.warnings + rules.warnings).distinct(),
    )

    private fun <T> candidates(values: List<Pair<T, String>>): ExtractedField<T> = when (values.size) {
        0 -> ExtractedField()
        1 -> ExtractedField(values.single().first, confidence = ExtractionConfidence.HIGH,
            evidence = values.single().second, source = ExtractionSource.OCR_RULES)
        else -> ExtractedField(alternatives = values.map { it.first },
            evidence = values.joinToString(" | ") { it.second }, source = ExtractionSource.OCR_RULES)
    }

    private fun <T> preferEvidence(model: ExtractedField<T>, rules: ExtractedField<T>): ExtractedField<T> {
        if (rules.source != ExtractionSource.OCR_RULES) return model
        // Multiple explicit candidates must stay missing even if the model guesses one.
        if (rules.value == null) return rules
        val conflict = model.value?.takeIf { it != rules.value }
        return if (conflict == null) rules else rules.copy(
            alternatives = listOf(conflict), confidence = ExtractionConfidence.MEDIUM,
        )
    }
}

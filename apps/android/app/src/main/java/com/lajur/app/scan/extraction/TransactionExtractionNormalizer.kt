package com.lajur.app.scan.extraction

import com.lajur.app.domain.MasterData
import java.time.DateTimeException
import java.time.LocalDate

private val INDONESIAN_NUMERIC_DATE = Regex("^(\\d{1,2})/(\\d{1,2})/(\\d{4})$")
private val QRIS_EVIDENCE = Regex("(?i)\\bQRIS\\b")
private val RECEIPT_STRUCTURE_LABEL = Regex(
    "(?i)^(?:KEMBALI|(?:GRAND\\s+)?TOTAL|SUBTOTAL|PPN(?:\\s+\\d+(?:[.,]\\d+)?%)?)(?:\\s*[:=]?\\s*(?:RP\\.?\\s*)?-?\\d[\\d.,]*)?$",
)

class TransactionExtractionNormalizer {
    fun normalize(
        input: TransactionExtraction,
        ocrText: String,
        master: MasterData?,
    ): TransactionExtraction = input.copy(
        date = normalizeDate(input.date),
        paymentMethod = normalizePayment(input.paymentMethod, ocrText, master),
        description = normalizeDescription(input.documentKind, input.description),
    )

    private fun normalizeDate(field: ExtractedField<String>): ExtractedField<String> = field.copy(
        value = normalizeDateValue(field.value),
        alternatives = field.alternatives.mapNotNull(::normalizeDateValue).distinct(),
        evidence = field.evidence ?: field.value,
    )

    private fun normalizeDateValue(value: String?): String? {
        val candidate = value?.trim()?.takeIf(String::isNotEmpty) ?: return null
        val match = INDONESIAN_NUMERIC_DATE.matchEntire(candidate) ?: return candidate
        return try {
            LocalDate.of(
                match.groupValues[3].toInt(),
                match.groupValues[2].toInt(),
                match.groupValues[1].toInt(),
            ).toString()
        } catch (_: DateTimeException) {
            candidate
        }
    }

    private fun normalizePayment(
        field: ExtractedField<String>,
        ocrText: String,
        master: MasterData?,
    ): ExtractedField<String> {
        if (!QRIS_EVIDENCE.containsMatchIn(ocrText)) return field
        val bank = master?.paymentMethods?.firstOrNull { it.equals("Bank", ignoreCase = true) }
        return if (bank == null) {
            ExtractedField()
        } else {
            ExtractedField(
                value = bank,
                confidence = ExtractionConfidence.HIGH,
                evidence = "QRIS",
                source = ExtractionSource.OCR_RULES,
            )
        }
    }

    private fun normalizeDescription(
        documentKind: DocumentKind,
        field: ExtractedField<String>,
    ): ExtractedField<String> {
        if (documentKind != DocumentKind.RECEIPT) return field
        val candidate = field.value?.trim()?.uppercase() ?: return field
        return if (RECEIPT_STRUCTURE_LABEL.matches(candidate)) ExtractedField() else field
    }
}

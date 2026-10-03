package com.lajur.app.scan.extraction

import com.lajur.app.domain.MasterData
import java.time.LocalDate

class TransactionExtractionValidator {
    fun validate(
        input: TransactionExtraction,
        master: MasterData?,
    ): ValidatedTransactionExtraction {
        val issues = mutableListOf<ExtractionIssue>()
        if (input.schemaVersion != CURRENT_EXTRACTION_SCHEMA_VERSION) {
            issues += blocking(
                ExtractionFieldName.SCHEMA,
                "unsupported_schema",
                "Versi hasil ekstraksi tidak didukung.",
            )
        }
        if (master == null) {
            issues += blocking(
                ExtractionFieldName.MASTER_DATA,
                "master_unavailable",
                "Master data belum tersedia untuk memvalidasi draft.",
            )
        }

        val date = validateDate(input.date, issues)
        val amount = validateAmount(input.amountIdr, issues)
        val transactionType = validateMasterChoice(
            input.transactionType,
            master?.transactionTypes.orEmpty(),
            ExtractionFieldName.TRANSACTION_TYPE,
            "Tipe transaksi tidak tersedia di Master.",
            issues,
        )
        val paymentMethod = validateMasterChoice(
            input.paymentMethod.copy(value = input.paymentMethod.value?.takeIf(String::isNotBlank)),
            master?.paymentMethods.orEmpty(),
            ExtractionFieldName.PAYMENT_METHOD,
            "Metode pembayaran tidak tersedia di Master.",
            issues,
        )
        val allowedCategories = master?.categories
            .orEmpty()
            .filter { category ->
                transactionType.value != null && category.kind.equals(transactionType.value, ignoreCase = true)
            }
            .map { it.name }
        val category = validateMasterChoice(
            input.category,
            allowedCategories,
            ExtractionFieldName.CATEGORY,
            "Kategori tidak tersedia untuk tipe transaksi ini.",
            issues,
        )
        val merchant = validateOptionalText(
            input.merchant,
            ExtractionFieldName.MERCHANT,
            issues,
        )
        val description = validateOptionalText(
            input.description,
            ExtractionFieldName.DESCRIPTION,
            issues,
        )

        addMissingRequired(date.value, ExtractionFieldName.DATE, "Tanggal wajib diperiksa.", issues)
        addMissingRequired(amount.value, ExtractionFieldName.AMOUNT_IDR, "Jumlah wajib diperiksa.", issues)
        addMissingRequired(
            transactionType.value,
            ExtractionFieldName.TRANSACTION_TYPE,
            "Tipe transaksi wajib diperiksa.",
            issues,
        )
        addMissingRequired(category.value, ExtractionFieldName.CATEGORY, "Kategori wajib diperiksa.", issues)

        return ValidatedTransactionExtraction(
            extraction = input.copy(
                date = date,
                amountIdr = amount,
                transactionType = transactionType,
                paymentMethod = paymentMethod,
                category = category,
                merchant = merchant,
                description = description,
                warnings = input.warnings.map(String::trim).filter(String::isNotEmpty).distinct(),
            ),
            issues = issues.distinctBy { Triple(it.field, it.code, it.message) },
        )
    }

    private fun validateDate(
        field: ExtractedField<String>,
        issues: MutableList<ExtractionIssue>,
    ): ExtractedField<String> {
        val value = field.value?.trim()?.takeIf { candidate ->
            runCatching { LocalDate.parse(candidate) }.isSuccess
        }
        if (field.value != null && value == null) {
            issues += blocking(ExtractionFieldName.DATE, "invalid_date", "Tanggal harus berformat YYYY-MM-DD.")
        }
        val alternatives = field.alternatives.map(String::trim)
            .filter { runCatching { LocalDate.parse(it) }.isSuccess }
            .distinct()
            .filterNot { it == value }
        return normalizedField(field, value, alternatives, issues, ExtractionFieldName.DATE)
    }

    private fun validateAmount(
        field: ExtractedField<Long>,
        issues: MutableList<ExtractionIssue>,
    ): ExtractedField<Long> {
        val value = field.value?.takeIf { it > 0 }
        if (field.value != null && value == null) {
            issues += blocking(
                ExtractionFieldName.AMOUNT_IDR,
                "invalid_amount",
                "Jumlah harus berupa rupiah bulat positif.",
            )
        }
        val alternatives = field.alternatives.filter { it > 0 }.distinct().filterNot { it == value }
        return normalizedField(field, value, alternatives, issues, ExtractionFieldName.AMOUNT_IDR)
    }

    private fun validateMasterChoice(
        field: ExtractedField<String>,
        allowed: List<String>,
        fieldName: ExtractionFieldName,
        invalidMessage: String,
        issues: MutableList<ExtractionIssue>,
    ): ExtractedField<String> {
        val value = canonicalChoice(field.value, allowed)
        if (field.value != null && value == null) {
            issues += blocking(fieldName, "out_of_master", invalidMessage)
        }
        val alternatives = field.alternatives.mapNotNull { canonicalChoice(it, allowed) }
            .distinct()
            .filterNot { it == value }
        return normalizedField(field, value, alternatives, issues, fieldName)
    }

    private fun validateOptionalText(
        field: ExtractedField<String>,
        fieldName: ExtractionFieldName,
        issues: MutableList<ExtractionIssue>,
    ): ExtractedField<String> {
        val value = field.value?.trim()?.takeIf(String::isNotEmpty)
        val alternatives = field.alternatives.map(String::trim)
            .filter(String::isNotEmpty)
            .distinct()
            .filterNot { it == value }
        return normalizedField(field, value, alternatives, issues, fieldName)
    }

    private fun <T> normalizedField(
        field: ExtractedField<T>,
        value: T?,
        alternatives: List<T>,
        issues: MutableList<ExtractionIssue>,
        fieldName: ExtractionFieldName,
    ): ExtractedField<T> {
        if (value != null && field.confidence == ExtractionConfidence.MISSING) {
            issues += ExtractionIssue(
                fieldName,
                "confidence_mismatch",
                "Nilai tersedia tetapi confidence ditandai missing; perlakukan sebagai low.",
                ExtractionIssueSeverity.WARNING,
            )
        }
        return field.copy(
            value = value,
            alternatives = alternatives,
            confidence = normalizedConfidence(field.confidence, value),
            evidence = field.evidence?.trim()?.takeIf(String::isNotEmpty),
        )
    }

    private fun normalizedConfidence(confidence: ExtractionConfidence, value: Any?): ExtractionConfidence = when {
        value == null -> ExtractionConfidence.MISSING
        confidence == ExtractionConfidence.MISSING -> ExtractionConfidence.LOW
        else -> confidence
    }

    private fun canonicalChoice(value: String?, allowed: List<String>): String? {
        val candidate = value?.trim()?.takeIf(String::isNotEmpty) ?: return null
        return allowed.firstOrNull { it.equals(candidate, ignoreCase = true) }
    }

    private fun addMissingRequired(
        value: Any?,
        field: ExtractionFieldName,
        message: String,
        issues: MutableList<ExtractionIssue>,
    ) {
        if (value == null && issues.none { it.field == field && it.severity == ExtractionIssueSeverity.BLOCKING }) {
            issues += blocking(field, "missing_required", message)
        }
    }

    private fun blocking(field: ExtractionFieldName, code: String, message: String) = ExtractionIssue(
        field = field,
        code = code,
        message = message,
        severity = ExtractionIssueSeverity.BLOCKING,
    )
}

package com.lajur.app.scan.extraction

import com.lajur.app.domain.TransactionDraft
import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable

const val CURRENT_EXTRACTION_SCHEMA_VERSION = 1

@Serializable
enum class ExtractionConfidence {
    @SerialName("high") HIGH,
    @SerialName("medium") MEDIUM,
    @SerialName("low") LOW,
    @SerialName("missing") MISSING,
}

@Serializable
enum class DocumentKind {
    @SerialName("payment_success") PAYMENT_SUCCESS,
    @SerialName("receipt") RECEIPT,
    @SerialName("transfer") TRANSFER,
    @SerialName("unknown") UNKNOWN,
}

@Serializable
enum class ExtractionSource {
    @SerialName("unknown") UNKNOWN,
    @SerialName("ocr_rules") OCR_RULES,
    @SerialName("local_model") LOCAL_MODEL,
}

@Serializable
data class ExtractedField<T>(
    val value: T? = null,
    val alternatives: List<T> = emptyList(),
    val confidence: ExtractionConfidence = ExtractionConfidence.MISSING,
    val evidence: String? = null,
    val source: ExtractionSource = ExtractionSource.UNKNOWN,
)

@Serializable
data class TransactionExtraction(
    val schemaVersion: Int = CURRENT_EXTRACTION_SCHEMA_VERSION,
    val documentKind: DocumentKind = DocumentKind.UNKNOWN,
    val date: ExtractedField<String> = ExtractedField(),
    val amountIdr: ExtractedField<Long> = ExtractedField(),
    val transactionType: ExtractedField<String> = ExtractedField(),
    val paymentMethod: ExtractedField<String> = ExtractedField(),
    val category: ExtractedField<String> = ExtractedField(),
    val merchant: ExtractedField<String> = ExtractedField(),
    val description: ExtractedField<String> = ExtractedField(),
    val warnings: List<String> = emptyList(),
) {
    companion object {
        fun empty(): TransactionExtraction = TransactionExtraction()
    }
}

@Serializable
enum class ExtractionFieldName {
    SCHEMA,
    MASTER_DATA,
    DATE,
    AMOUNT_IDR,
    TRANSACTION_TYPE,
    PAYMENT_METHOD,
    CATEGORY,
    MERCHANT,
    DESCRIPTION,
}

@Serializable
enum class ExtractionIssueSeverity { WARNING, BLOCKING }

@Serializable
data class ExtractionIssue(
    val field: ExtractionFieldName,
    val code: String,
    val message: String,
    val severity: ExtractionIssueSeverity,
)

@Serializable
data class ValidatedTransactionExtraction(
    val extraction: TransactionExtraction,
    val issues: List<ExtractionIssue>,
) {
    val isReadyToSave: Boolean
        get() = issues.none { it.severity == ExtractionIssueSeverity.BLOCKING } &&
            extraction.date.value != null &&
            extraction.amountIdr.value != null &&
            extraction.transactionType.value != null &&
            extraction.category.value != null

    fun toTransactionDraftOrNull(): TransactionDraft? {
        if (!isReadyToSave) return null
        return TransactionDraft(
            date = requireNotNull(extraction.date.value),
            type = requireNotNull(extraction.transactionType.value),
            paymentMethod = extraction.paymentMethod.value.orEmpty(),
            category = requireNotNull(extraction.category.value),
            amountIdr = requireNotNull(extraction.amountIdr.value),
            description = extraction.description.value,
        )
    }
}

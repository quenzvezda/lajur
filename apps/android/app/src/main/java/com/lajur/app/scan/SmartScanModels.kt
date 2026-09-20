package com.lajur.app.scan

enum class ScanImageSource { SHARE, GALLERY, CAMERA }

data class ScanImageRequest(
    val uri: String,
    val source: ScanImageSource,
)

data class ScanImageMetadata(
    val originalWidth: Int,
    val originalHeight: Int,
    val processedWidth: Int,
    val processedHeight: Int,
    val sampleSize: Int,
)

data class OcrTextBlock(
    val text: String,
    val left: Int?,
    val top: Int?,
    val right: Int?,
    val bottom: Int?,
)

data class OcrResult(
    val text: String,
    val blocks: List<OcrTextBlock>,
    val recognitionMillis: Long,
)

data class SmartScanOutput(
    val request: ScanImageRequest,
    val image: ScanImageMetadata,
    val ocr: OcrResult,
    val warnings: List<String>,
)

interface PreparedScanImage : AutoCloseable {
    val metadata: ScanImageMetadata
}
fun interface ImagePreprocessor<T : PreparedScanImage> {
    suspend fun preprocess(request: ScanImageRequest): T
}

fun interface OcrEngine<T : PreparedScanImage> {
    suspend fun recognize(image: T): OcrResult
}

class SmartScanPipeline<T : PreparedScanImage>(
    private val preprocessor: ImagePreprocessor<T>,
    private val ocrEngine: OcrEngine<T>,
) {
    suspend fun process(request: ScanImageRequest): SmartScanOutput {
        val prepared = preprocessor.preprocess(request)
        return prepared.use {
            val result = ocrEngine.recognize(it)
            SmartScanOutput(
                request = request,
                image = it.metadata,
                ocr = result,
                warnings = if (result.text.isBlank()) {
                    listOf("Tidak ada teks yang terbaca. Coba foto yang lebih terang dan tajam.")
                } else {
                    emptyList()
                },
            )
        }
    }
}

sealed interface SmartScanState {
    data object Idle : SmartScanState
    data class Ready(val request: ScanImageRequest) : SmartScanState
    data class Processing(val request: ScanImageRequest) : SmartScanState
    data class Complete(val output: SmartScanOutput) : SmartScanState
    data class Failed(val request: ScanImageRequest, val message: String) : SmartScanState
}

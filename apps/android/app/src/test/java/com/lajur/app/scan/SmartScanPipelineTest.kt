package com.lajur.app.scan

import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class SmartScanPipelineTest {
    @Test
    fun `pipeline preprocesses recognizes and closes prepared image`() = runBlocking {
        val prepared = FakePreparedImage()
        val request = ScanImageRequest("content://receipt", ScanImageSource.GALLERY)
        val pipeline = SmartScanPipeline<FakePreparedImage>(
            preprocessor = ImagePreprocessor {
                assertEquals(request, it)
                prepared
            },
            ocrEngine = OcrEngine {
                assertEquals(prepared, it)
                OcrResult("TOTAL 150000", emptyList(), 24)
            },
        )

        val output = pipeline.process(request)

        assertEquals("TOTAL 150000", output.ocr.text)
        assertTrue(output.warnings.isEmpty())
        assertTrue(prepared.closed)
    }

    @Test
    fun `blank OCR result becomes an explicit review warning`() = runBlocking {
        val pipeline = SmartScanPipeline<FakePreparedImage>(
            preprocessor = ImagePreprocessor { FakePreparedImage() },
            ocrEngine = OcrEngine { OcrResult("", emptyList(), 5) },
        )

        val output = pipeline.process(ScanImageRequest("content://blank", ScanImageSource.SHARE))

        assertEquals(1, output.warnings.size)
    }

    @Test
    fun `prepared image is closed when OCR fails`() = runBlocking {
        val prepared = FakePreparedImage()
        val pipeline = SmartScanPipeline<FakePreparedImage>(
            preprocessor = ImagePreprocessor { prepared },
            ocrEngine = OcrEngine { error("recognizer failed") },
        )

        runCatching {
            pipeline.process(ScanImageRequest("content://broken", ScanImageSource.CAMERA))
        }

        assertTrue(prepared.closed)
    }

    private class FakePreparedImage : PreparedScanImage {
        var closed = false
        override val metadata = ScanImageMetadata(100, 200, 100, 200, 1)

        override fun close() {
            closed = true
        }
    }
}

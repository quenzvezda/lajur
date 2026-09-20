package com.lajur.app.scan

import android.os.SystemClock
import com.google.mlkit.vision.common.InputImage
import com.google.mlkit.vision.text.Text
import com.google.mlkit.vision.text.TextRecognition
import com.google.mlkit.vision.text.latin.TextRecognizerOptions
import kotlin.coroutines.resume
import kotlin.coroutines.resumeWithException
import kotlin.coroutines.suspendCoroutine

class MlKitOcrEngine : OcrEngine<PreparedBitmap>, AutoCloseable {
    private val recognizer = TextRecognition.getClient(TextRecognizerOptions.DEFAULT_OPTIONS)

    override suspend fun recognize(image: PreparedBitmap): OcrResult {
        val startedAt = SystemClock.elapsedRealtime()
        val recognized = suspendCoroutine<Text> { continuation ->
            recognizer.process(InputImage.fromBitmap(image.bitmap, 0))
                .addOnSuccessListener(continuation::resume)
                .addOnFailureListener(continuation::resumeWithException)
        }
        return OcrResult(
            text = recognized.text.trim(),
            blocks = recognized.textBlocks.map { block ->
                val box = block.boundingBox
                OcrTextBlock(
                    text = block.text,
                    left = box?.left,
                    top = box?.top,
                    right = box?.right,
                    bottom = box?.bottom,
                )
            },
            recognitionMillis = SystemClock.elapsedRealtime() - startedAt,
        )
    }

    override fun close() = recognizer.close()
}

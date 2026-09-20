package com.lajur.app.scan

import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.Typeface
import android.util.Log
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class BundledOcrSmokeTest {
    @Test
    fun bundledLatinModelRecognizesSyntheticPaymentTextWhenExplicitlyEnabled() = runBlocking {
        val arguments = InstrumentationRegistry.getArguments()
        if (arguments.getString("lajurOcrE2e") != "true") return@runBlocking
        val bitmap = Bitmap.createBitmap(1_200, 500, Bitmap.Config.ARGB_8888)
        Canvas(bitmap).apply {
            drawColor(Color.WHITE)
            val paint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
                color = Color.BLACK
                textSize = 120f
                typeface = Typeface.DEFAULT_BOLD
            }
            drawText("LAJUR", 50f, 170f, paint)
            drawText("TOTAL RP 150000", 50f, 350f, paint)
        }
        val prepared = PreparedBitmap(
            bitmap,
            ScanImageMetadata(1_200, 500, 1_200, 500, 1),
        )
        val engine = MlKitOcrEngine()

        val result = try {
            engine.recognize(prepared)
        } finally {
            engine.close()
            prepared.close()
        }

        assertTrue(result.text.contains("LAJUR", ignoreCase = true))
        assertTrue(result.text.filter(Char::isDigit).contains("150000"))
        Log.i(
            "LajurOcrBenchmark",
            "synthetic=1200x500 recognitionMillis=${result.recognitionMillis} blocks=${result.blocks.size}",
        )
    }
}

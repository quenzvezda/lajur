package com.lajur.app.scan

import android.content.Context
import android.graphics.Bitmap
import android.graphics.Color
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class AndroidImagePreprocessorTest {
    @Test
    fun largeCameraImageIsDecodedWithinBoundedDimensions() = runBlocking {
        val context = ApplicationProvider.getApplicationContext<Context>()
        val store = TemporaryCaptureStore(context)
        val uri = store.createUri()
        val source = Bitmap.createBitmap(3_000, 1_000, Bitmap.Config.ARGB_8888).apply {
            eraseColor(Color.WHITE)
        }
        context.contentResolver.openOutputStream(uri)?.use {
            assertTrue(source.compress(Bitmap.CompressFormat.JPEG, 90, it))
        }
        source.recycle()

        val prepared = AndroidImagePreprocessor(context).preprocess(
            ScanImageRequest(uri.toString(), ScanImageSource.CAMERA),
        )

        try {
            assertEquals(3_000, prepared.metadata.originalWidth)
            assertEquals(1_000, prepared.metadata.originalHeight)
            assertTrue(prepared.metadata.processedWidth <= 2_048)
            assertTrue(prepared.metadata.processedHeight <= 2_048)
            assertEquals(2, prepared.metadata.sampleSize)
        } finally {
            prepared.close()
            store.release(uri.toString())
        }
    }

    @Test
    fun fileUrisAreRejected() = runBlocking {
        val context = ApplicationProvider.getApplicationContext<Context>()

        val failure = runCatching {
            AndroidImagePreprocessor(context).preprocess(
                ScanImageRequest("file:///sdcard/receipt.jpg", ScanImageSource.SHARE),
            )
        }.exceptionOrNull()

        assertTrue(failure is IllegalArgumentException)
    }
}

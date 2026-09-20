package com.lajur.app.scan

import android.content.Context
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Matrix
import android.net.Uri
import androidx.exifinterface.media.ExifInterface
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlin.math.max

private const val MAX_SOURCE_BYTES = 20L * 1024 * 1024
private const val MAX_SOURCE_PIXELS = 50_000_000L
private const val MAX_PROCESSED_EDGE = 2_048

class PreparedBitmap(
    val bitmap: Bitmap,
    override val metadata: ScanImageMetadata,
) : PreparedScanImage {
    override fun close() {
        if (!bitmap.isRecycled) bitmap.recycle()
    }
}

class AndroidImagePreprocessor(context: Context) : ImagePreprocessor<PreparedBitmap> {
    private val resolver = context.applicationContext.contentResolver

    override suspend fun preprocess(request: ScanImageRequest): PreparedBitmap = withContext(Dispatchers.IO) {
        val uri = Uri.parse(request.uri)
        require(uri.scheme == "content") { "Sumber gambar tidak didukung." }
        resolver.getType(uri)?.let { mime ->
            require(mime.startsWith("image/")) { "File yang dipilih bukan gambar." }
        }
        resolver.openAssetFileDescriptor(uri, "r")?.use { descriptor ->
            if (descriptor.length >= 0) {
                require(descriptor.length <= MAX_SOURCE_BYTES) { "Ukuran gambar melebihi batas 20 MB." }
            }
        }

        val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
        val boundsStream = resolver.openInputStream(uri) ?: error("Gambar tidak dapat dibuka.")
        boundsStream.use { BitmapFactory.decodeStream(it, null, bounds) }
        require(bounds.outWidth > 0 && bounds.outHeight > 0) { "Dimensi gambar tidak valid." }
        require(bounds.outWidth.toLong() * bounds.outHeight <= MAX_SOURCE_PIXELS) {
            "Resolusi gambar terlalu besar. Maksimum 50 megapiksel."
        }

        var sampleSize = 1
        while (max(bounds.outWidth / sampleSize, bounds.outHeight / sampleSize) > MAX_PROCESSED_EDGE) {
            sampleSize *= 2
        }
        val options = BitmapFactory.Options().apply {
            inSampleSize = sampleSize
            inPreferredConfig = Bitmap.Config.ARGB_8888
        }
        val decoded = resolver.openInputStream(uri)?.use { BitmapFactory.decodeStream(it, null, options) }
            ?: error("Gambar tidak dapat didekode.")
        val orientation = runCatching {
            resolver.openInputStream(uri)?.use {
                ExifInterface(it).getAttributeInt(
                    ExifInterface.TAG_ORIENTATION,
                    ExifInterface.ORIENTATION_NORMAL,
                )
            }
        }.getOrNull() ?: ExifInterface.ORIENTATION_NORMAL
        val oriented = applyOrientation(decoded, orientation)
        if (oriented !== decoded) decoded.recycle()

        PreparedBitmap(
            bitmap = oriented,
            metadata = ScanImageMetadata(
                originalWidth = bounds.outWidth,
                originalHeight = bounds.outHeight,
                processedWidth = oriented.width,
                processedHeight = oriented.height,
                sampleSize = sampleSize,
            ),
        )
    }

    private fun applyOrientation(bitmap: Bitmap, orientation: Int): Bitmap {
        val matrix = Matrix()
        when (orientation) {
            ExifInterface.ORIENTATION_FLIP_HORIZONTAL -> matrix.setScale(-1f, 1f)
            ExifInterface.ORIENTATION_ROTATE_180 -> matrix.setRotate(180f)
            ExifInterface.ORIENTATION_FLIP_VERTICAL -> matrix.setScale(1f, -1f)
            ExifInterface.ORIENTATION_TRANSPOSE -> {
                matrix.setRotate(90f)
                matrix.postScale(-1f, 1f)
            }
            ExifInterface.ORIENTATION_ROTATE_90 -> matrix.setRotate(90f)
            ExifInterface.ORIENTATION_TRANSVERSE -> {
                matrix.setRotate(-90f)
                matrix.postScale(-1f, 1f)
            }
            ExifInterface.ORIENTATION_ROTATE_270 -> matrix.setRotate(-90f)
            else -> return bitmap
        }
        return Bitmap.createBitmap(bitmap, 0, 0, bitmap.width, bitmap.height, matrix, true)
    }
}

package com.lajur.app.scan

import android.content.Context
import android.net.Uri
import androidx.core.content.FileProvider
import java.io.File
import java.util.concurrent.ConcurrentHashMap

class TemporaryCaptureStore(context: Context) {
    private val applicationContext = context.applicationContext
    private val directory = File(applicationContext.cacheDir, "smart_scan").apply { mkdirs() }
    private val captures = ConcurrentHashMap<String, File>()

    init {
        directory.listFiles()?.forEach(File::delete)
    }

    fun createUri(): Uri {
        val file = File.createTempFile("capture-", ".jpg", directory)
        val uri = FileProvider.getUriForFile(
            applicationContext,
            "${applicationContext.packageName}.fileprovider",
            file,
        )
        captures[uri.toString()] = file
        return uri
    }

    fun release(uri: String) {
        captures.remove(uri)?.delete()
    }
}

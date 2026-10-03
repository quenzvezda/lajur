package com.lajur.app.data

import android.content.Context
import android.net.Uri
import android.provider.DocumentsContract
import android.provider.OpenableColumns
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.withContext
import java.io.File

data class SelectedWorkbook(val file: File, val displayName: String, val sizeBytes: Long, val ownerId: String)

/** Copies opaque workbook packages; never interprets or edits XLSM. */
class WorkbookDocumentStore(context: Context) {
    private val resolver = context.applicationContext.contentResolver
    private val directory = File(context.applicationContext.cacheDir, "workbook-transfers").apply { mkdirs() }

    fun temporaryFile(): File = File.createTempFile("transfer-", ".tmp", directory)

    suspend fun select(uriText: String, ownerId: String): SelectedWorkbook = withContext(Dispatchers.IO) {
        val uri = Uri.parse(uriText)
        require(uri.scheme == "content") { "Pilih workbook melalui pemilih file Android." }
        val name = resolver.query(uri, arrayOf(OpenableColumns.DISPLAY_NAME), null, null, null)?.use { cursor ->
            if (cursor.moveToFirst()) cursor.getString(0) else null
        } ?: error("Nama file tidak dapat dibaca.")
        require(name.endsWith(".xlsm", ignoreCase = true)) { "Pilih file Excel macro-enabled (.xlsm), bukan XLSX atau ZIP." }
        val file = temporaryFile()
        val context = currentCoroutineContext()
        try {
            val size = file.outputStream().use { output ->
                (resolver.openInputStream(uri) ?: error("File tidak dapat dibuka.")).use { input ->
                    WorkbookTransfers.copyLimited(input, output, WorkbookTransfers.MAX_IMPORT_BYTES) { context.ensureActive() }
                }
            }
            SelectedWorkbook(file, name.take(200), size, ownerId)
        } catch (error: Throwable) {
            file.delete()
            throw error
        }
    }

    suspend fun save(file: File, destination: String) = withContext(Dispatchers.IO) {
        val uri = Uri.parse(destination)
        require(uri.scheme == "content") { "Pilih tujuan melalui pemilih file Android." }
        val context = currentCoroutineContext()
        try {
            (resolver.openOutputStream(uri, "wt") ?: error("Tujuan tidak dapat dibuka.")).use { output ->
                file.inputStream().use { input ->
                    WorkbookTransfers.copyLimited(input, output, WorkbookTransfers.MAX_EXPORT_BYTES) { context.ensureActive() }
                }
            }
        } catch (error: Throwable) {
            // CreateDocument granted this new output, not an existing user workbook.
            runCatching { DocumentsContract.deleteDocument(resolver, uri) }
            throw error
        }
    }
}

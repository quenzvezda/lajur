package com.lajur.app.data

import androidx.room.withTransaction
import com.lajur.app.data.local.LajurDatabase
import com.lajur.app.data.remote.WorkbookImportDto
import com.lajur.app.data.remote.WorkbookInfoDto
import kotlinx.coroutines.NonCancellable
import kotlinx.coroutines.withContext

class WorkbookRepository(
    private val database: LajurDatabase,
    private val session: SessionRepository,
    private val gate: FinanceOperationGate,
    private val documents: WorkbookDocumentStore,
) {
    val outboxCount = database.dao().observeOutboxCount()

    suspend fun list(): List<WorkbookInfoDto> = gate.remoteChange {
        val items = session.authorized { api, token -> api.listWorkbooks(token) }
        session.reloadWorkbookState()
        items.sortedByDescending { it.year }
    }

    suspend fun select(uri: String): SelectedWorkbook = documents.select(uri, ownerId())

    suspend fun export(year: Int?, destination: String) {
        val file = documents.temporaryFile()
        try {
            gate.remoteChange {
                session.authorized { api, token -> api.exportWorkbook(year, file, token) }
            }
            documents.save(file, destination)
        } finally {
            file.delete()
        }
    }

    suspend fun import(selected: SelectedWorkbook, year: Int, replace: Boolean): WorkbookImportDto =
        gate.importWorkbook({ database.dao().getOutboxCount() != 0 }) {
            check(selected.ownerId == ownerId()) { "Akun berubah. Pilih workbook kembali." }
            require(year in 1900..9999) { "Tahun tidak valid." }
            check(selected.file.isFile && selected.file.length() == selected.sizeBytes &&
                selected.sizeBytes in 1..WorkbookTransfers.MAX_IMPORT_BYTES) { "File pilihan tidak lagi tersedia. Pilih ulang workbook." }
            // Invalidate BEFORE upload: process death/lost response must not leave stale
            // cached revisions editable after a possibly committed replacement.
            withContext(NonCancellable) {
                database.withTransaction {
                    database.dao().clearSyncedYear(year.toString())
                    database.dao().clearMasterYear(year)
                }
            }
            session.authorized { api, token -> api.importWorkbook(selected.file, year, replace, token) }
        }

    private fun ownerId(): String = (session.state.value as? SessionState.SignedIn)?.session?.userId
        ?: error("Silakan login kembali.")
}

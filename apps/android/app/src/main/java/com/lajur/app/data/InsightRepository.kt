package com.lajur.app.data

import com.lajur.app.data.remote.YearReportDto
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json
import java.io.File
import java.security.MessageDigest
import java.time.Instant

@Serializable data class CachedInsight(val report: YearReportDto, val fetchedAt: String)

/** Disposable private-file cache, scoped by server + immutable account ID + year. */
class InsightRepository(private val directory: File, private val json: Json) {
    private fun file(owner: String, year: Int): File {
        val hash = MessageDigest.getInstance("SHA-256").digest(owner.toByteArray())
            .joinToString("") { "%02x".format(it) }
        return File(directory, "$hash-$year.json")
    }

    suspend fun read(owner: String, year: Int): CachedInsight? = withContext(Dispatchers.IO) {
        runCatching { json.decodeFromString<CachedInsight>(file(owner, year).readText()) }
            .getOrNull()?.takeIf { it.report.year == year }
    }

    suspend fun save(owner: String, report: YearReportDto): CachedInsight = withContext(Dispatchers.IO) {
        directory.mkdirs()
        val cached = CachedInsight(report, Instant.now().toString())
        val destination = file(owner, report.year)
        val temp = File.createTempFile("insight-", ".tmp", directory)
        try {
            temp.writeText(json.encodeToString(cached))
            check(temp.renameTo(destination)) { "Cache laporan tidak dapat diperbarui." }
        } finally { temp.delete() }
        cached
    }
}

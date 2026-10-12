package com.lajur.app.ui

import com.lajur.app.data.remote.*
import com.lajur.app.data.InsightRepository
import kotlinx.coroutines.test.runTest
import kotlinx.serialization.json.Json
import org.junit.Assert.*
import org.junit.Test
import java.nio.file.Files

class InsightTest {
    @Test fun switchingOwnerImmediatelyHidesPreviousReportState() {
        val previous = InsightState(ownerKey = "local|alice", error = "Old report", offline = true)
        assertEquals(previous, previous.forOwner("local|alice"))
        assertEquals(InsightState(), previous.forOwner("local|bob"))
        assertEquals(InsightState(), previous.forOwner("production|alice"))
    }
    private val month = MonthReportDto(1, 1_000_000, 75_000, 925_000, 925_000, .925, 3,
        emptyList(), listOf(
            ReportRowDto("2026-01-01", "Pengeluaran", "", "Makan", 25_000),
            ReportRowDto("2026-01-02", "Pengeluaran", "Bank", "Makan", 50_000),
            ReportRowDto("2026-01-02", "Pemasukan", "Bank", "Gaji", 1_000_000),
        ))
    @Test fun paymentsExcludeIncomeAndDailyIncludesEmptyDays() {
        assertEquals(listOf("Bank" to 50_000L, "Belum diisi" to 25_000L), monthlyPayments(month))
        assertEquals(listOf(25_000L, 50_000L, 0L), dailyExpenses(month, 3))
    }
    @Test fun cacheIsIsolatedByServerAccountAndYearAndSurvivesRepositoryRestart() = runTest {
        val root = Files.createTempDirectory("insight-test").toFile()
        try {
            val json = Json { ignoreUnknownKeys = true }
            val report = YearReportDto(2026, "2026-01-12", "2026-01-12T00:00:00Z", listOf(month), 1_000_000, 75_000, 925_000, .925, 3)
            InsightRepository(root, json).save("local|alice", report)
            val cache = InsightRepository(root, json)
            assertEquals(report, cache.read("local|alice", 2026)?.report)
            assertNull(cache.read("local|bob", 2026))
            assertNull(cache.read("production|alice", 2026))
            assertNull(cache.read("local|alice", 2027))
            root.listFiles()!!.single().writeText("broken JSON")
            assertNull(cache.read("local|alice", 2026))
        } finally { root.deleteRecursively() }
    }
}

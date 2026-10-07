package com.lajur.app.ui

import android.graphics.Bitmap
import android.os.SystemClock
import androidx.activity.OnBackPressedDispatcher
import androidx.activity.compose.LocalOnBackPressedDispatcherOwner
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.hasTestTag
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollToNode
import androidx.test.platform.app.InstrumentationRegistry
import androidx.test.espresso.Espresso
import com.lajur.app.domain.LocalTransaction
import com.lajur.app.domain.MasterCategory
import com.lajur.app.domain.MasterData
import com.lajur.app.domain.SyncCounts
import com.lajur.app.domain.SyncState
import com.lajur.app.domain.TransactionDraft
import java.io.File
import java.time.YearMonth
import org.junit.Assert.assertEquals
import org.junit.Assume.assumeTrue
import org.junit.Before
import org.junit.Rule
import org.junit.Test

/** Explicit opt-in: dummy UI only, no account/backend/outbox writes or Activity finish. */
class AppNavigationUiTest {
    @get:Rule val compose = createComposeRule()

    @Before fun optIn() {
        assumeTrue(InstrumentationRegistry.getArguments().getString("lajur.runUiSmokeTests") == "true")
    }

    @Test fun systemBackFromNewFormReturnsHomeThenRequiresTwoPressesToExit() {
        var dispatcher: OnBackPressedDispatcher? = null
        var prompts = 0
        var exits = 0
        var now = 0L
        compose.setContent {
            MaterialTheme {
                dispatcher = LocalOnBackPressedDispatcherOwner.current!!.onBackPressedDispatcher
                var screen: AppScreen by remember { mutableStateOf(AppScreen.Editor(null)) }
                val back = rememberAppBackAction(screen, { screen = it }, { exits++ }, { prompts++ }, { now })
                if (screen is AppScreen.Editor) TransactionEditor(null, initialExtraction = null, master = master(), onBack = back, onSave = {})
                else Text("Beranda dummy")
            }
        }
        compose.onNodeWithText("Transaksi baru").assertIsDisplayed()
        compose.runOnIdle { dispatcher!!.onBackPressed() }
        compose.onNodeWithText("Beranda dummy").assertIsDisplayed()
        compose.runOnIdle {
            assertEquals(0, exits)
            dispatcher!!.onBackPressed()
            assertEquals(1, prompts)
            assertEquals(0, exits)
            now = 500
            dispatcher!!.onBackPressed()
            assertEquals(1, exits)
        }
    }

    @Test fun toolbarBackUsesSameDestinationAsSystemBack() {
        var target: AppScreen? = null
        compose.setContent {
            MaterialTheme {
                val back = rememberAppBackAction(AppScreen.Editor(null), { target = it }, {}, {})
                TransactionEditor(null, initialExtraction = null, master = master(), onBack = back, onSave = {})
            }
        }
        compose.onNodeWithText("Kembali").performClick()
        compose.runOnIdle { assertEquals(AppScreen.Main(), target) }
    }

    @Test fun systemBackDismissesCategorySheetBeforeLeavingForm() {
        var target: AppScreen? = null
        compose.setContent {
            MaterialTheme {
                val back = rememberAppBackAction(AppScreen.Editor(null), { target = it }, {}, {})
                TransactionEditor(null, initialExtraction = null, master = master(), onBack = back, onSave = {})
            }
        }
        compose.onNodeWithTag("selection-Kategori").performClick()
        compose.onNodeWithText("Cari kategori").assertIsDisplayed()
        Espresso.pressBack()
        compose.onNodeWithText("Cari kategori").assertDoesNotExist()
        compose.onNodeWithText("Transaksi baru").assertIsDisplayed()
        compose.runOnIdle { assertEquals(null, target) }
    }

    @Test fun darkThemeKeepsIncomeExpenseLabelsReadable() {
        compose.setContent { MaterialTheme(colorScheme = darkColorScheme()) { Dashboard(MainTab.HISTORY, {}) } }
        compose.onNodeWithTag("home-content").performScrollToNode(hasTestTag("transaction-dummy-income"))
        compose.onNodeWithText("Pemasukan").assertIsDisplayed()
        compose.onNodeWithText("Pengeluaran").assertIsDisplayed()
        capture("navigation-history-dark")
    }

    @Test fun bottomMenuMovesScanOffHomeAndShowsExplicitInsightPlaceholder() {
        compose.setContent {
            MaterialTheme {
                var tab by remember { mutableStateOf(MainTab.HOME) }
                Dashboard(tab, { tab = it })
            }
        }
        compose.onNodeWithText("Ringkasan bulan ini").assertIsDisplayed()
        compose.onNodeWithText("Pilih gambar").assertDoesNotExist()
        capture("navigation-home")
        compose.onNodeWithTag("nav-scan").performClick()
        compose.onNodeWithText("Pilih gambar").assertIsDisplayed()
        compose.onNodeWithText("Kamera").assertIsDisplayed()
        compose.onNodeWithText("Ringkasan bulan ini").assertDoesNotExist()
        capture("navigation-scan")
        compose.onNodeWithTag("nav-insights").performClick()
        compose.onNodeWithTag("insights-placeholder").assertIsDisplayed()
        compose.onNodeWithTag("add-transaction").assertDoesNotExist()
        capture("navigation-insight")
        compose.onNodeWithTag("nav-history").performClick()
        compose.onNodeWithTag("home-content").performScrollToNode(hasTestTag("transaction-dummy-income"))
        compose.onNodeWithText("Pemasukan").assertIsDisplayed()
        compose.onNodeWithText("Pengeluaran").assertIsDisplayed()
        capture("navigation-history")
    }

    @Test fun homeSummaryAndPreviewAreNotAffectedByHistorySearch() {
        compose.setContent { MaterialTheme { Dashboard(MainTab.HOME, {}, query = "no-match") } }
        compose.onNodeWithText("Ringkasan bulan ini").assertIsDisplayed()
        compose.onNodeWithText("Selisih bulan ini ${formatIdr(90_000)}").assertIsDisplayed()
        compose.onNodeWithTag("home-content").performScrollToNode(hasTestTag("transaction-dummy-income"))
        compose.onNodeWithTag("transaction-dummy-income").assertIsDisplayed()
    }

    @Composable private fun Dashboard(tab: MainTab, onTab: (MainTab) -> Unit, query: String = "") {
        val draft = TransactionDraft("2027-10-05", "Pemasukan", "Bank", "Gaji", 100_000)
        val income = LocalTransaction("dummy-income", 1, draft, SyncState.SYNCED, null, false)
        val expense = income.copy(transactionId = "dummy-expense",
            draft = draft.copy(type = "Pengeluaran", category = "Jajan", amountIdr = 10_000))
        HomeScreen(month = YearMonth.of(2027, 10), transactions = listOf(income, expense),
            incomeIdr = 100_000, expenseIdr = 10_000, query = query, syncCounts = SyncCounts(0, 0),
            offlineSession = false, hasWorkbook = true, hasPendingSharedImage = false,
            snackbar = remember { SnackbarHostState() }, onQueryChange = {}, onPreviousMonth = {},
            onNextMonth = {}, onRefresh = {}, onRetry = {}, onCreateWorkbook = {}, onAdd = {},
            onTransaction = {}, onPickImage = {}, onTakePhoto = {}, onOpenSharedImage = {},
            onDiscardSharedImage = {}, tab = tab, onTab = onTab)
    }

    private fun master() = MasterData(2027, "1", listOf("Pengeluaran", "Pemasukan"), listOf("Cash", "Bank"),
        listOf(MasterCategory("Jajan", "Pengeluaran"), MasterCategory("Gaji", "Pemasukan")))

    private fun capture(name: String) {
        compose.waitForIdle()
        SystemClock.sleep(500) // Let the navigation selection animation settle for visual review.
        val bitmap = InstrumentationRegistry.getInstrumentation().uiAutomation.takeScreenshot() ?: return
        try {
            val context = InstrumentationRegistry.getInstrumentation().targetContext
            val directory = File(context.externalCacheDir ?: context.noBackupFilesDir, "ui-smoke").apply { mkdirs() }
            File(directory, "$name.png").outputStream().use { bitmap.compress(Bitmap.CompressFormat.PNG, 100, it) }
        } finally { bitmap.recycle() }
    }
}

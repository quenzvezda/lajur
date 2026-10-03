package com.lajur.app.ui

import android.graphics.Bitmap
import android.os.SystemClock
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.SnackbarHostState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.assertIsEnabled
import androidx.compose.ui.test.assertIsNotEnabled
import androidx.compose.ui.test.assertTextContains
import androidx.compose.ui.test.hasTestTag
import androidx.compose.ui.test.getUnclippedBoundsInRoot
import androidx.compose.ui.test.isPopup
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performImeAction
import androidx.compose.ui.test.performScrollToNode
import androidx.compose.ui.test.performTextInput
import androidx.compose.ui.unit.dp
import androidx.test.platform.app.InstrumentationRegistry
import com.lajur.app.domain.MasterCategory
import com.lajur.app.domain.LocalTransaction
import com.lajur.app.domain.SyncCounts
import com.lajur.app.domain.SyncState
import com.lajur.app.domain.MasterData
import com.lajur.app.domain.TransactionDraft
import com.lajur.app.scan.OcrResult
import com.lajur.app.scan.ScanImageMetadata
import com.lajur.app.scan.ScanImageRequest
import com.lajur.app.scan.ScanImageSource
import com.lajur.app.scan.ScanReviewSnapshot
import com.lajur.app.scan.ScanSuggestionState
import com.lajur.app.scan.SmartScanOutput
import com.lajur.app.scan.SmartScanState
import com.lajur.app.scan.extraction.EvidenceTransactionExtractor
import com.lajur.app.scan.extraction.ExtractionRunResult
import com.lajur.app.scan.extraction.ExtractionTrace
import com.lajur.app.scan.extraction.TransactionExtraction
import com.lajur.app.scan.extraction.TransactionExtractionNormalizer
import com.lajur.app.scan.extraction.TransactionExtractionValidator
import java.io.File
import java.time.YearMonth
import org.junit.Assert.assertTrue
import org.junit.Assert.assertEquals
import org.junit.Assume.assumeTrue
import org.junit.Before
import org.junit.Rule
import org.junit.Test

/** Opt-in isolated Compose tests: no account, model, network or ledger writes. */
class TransactionEntryUiTest {
    @get:Rule val compose = createComposeRule()

    @Before fun optIn() {
        assumeTrue(InstrumentationRegistry.getArguments().getString("lajur.runUiSmokeTests") == "true")
    }

    @Test fun categoryPickerHasSearchInsteadOfAnOversizedPopup() {
        compose.setContent {
            MaterialTheme {
                Column(Modifier.fillMaxSize().padding(16.dp)) {
                    SelectionField("Kategori", "", listOf("Makanan-Jadi", "Transport", "Kebutuhan") +
                        (1..60).map { "Kategori dummy $it" }, searchable = true) {}
                }
            }
        }
        compose.onNodeWithTag("selection-Kategori").performClick()
        compose.onNodeWithText("Cari kategori").assertIsDisplayed()
        capture("category-picker")
    }

    @Test fun paymentMenuMatchesItsFieldWidth() {
        compose.setContent {
            MaterialTheme {
                Column(Modifier.fillMaxSize().padding(16.dp)) {
                    SelectionField("Pembayaran", "", listOf("Cash", "Bank")) {}
                }
            }
        }
        val field = compose.onNodeWithTag("selection-Pembayaran")
        val fieldBounds = field.getUnclippedBoundsInRoot()
        val width = (fieldBounds.right - fieldBounds.left).value
        field.performClick()
        val menuBounds = compose.onNode(isPopup()).getUnclippedBoundsInRoot()
        val menuWidth = (menuBounds.right - menuBounds.left).value
        assertTrue("Menu width $menuWidth should match field width $width", menuWidth >= width * 0.95f)
        capture("payment-menu")
    }

    @Test fun searchFiltersChoicesAndSelectionClosesTheSheet() {
        compose.setContent {
            MaterialTheme {
                var selected by remember { mutableStateOf("") }
                Column(Modifier.fillMaxSize().padding(16.dp)) {
                    SelectionField("Kategori", selected, listOf("Makanan-Jadi", "Transport", "Kuota"),
                        searchable = true) { selected = it }
                }
            }
        }
        compose.onNodeWithTag("selection-Kategori").performClick()
        compose.onNodeWithTag("selection-search").performTextInput("kuO")
        compose.onNodeWithText("Transport").assertDoesNotExist()
        compose.onNodeWithText("Kuota").performClick()
        compose.onNodeWithTag("selection-search").assertDoesNotExist()
        compose.onNodeWithTag("selection-Kategori").assertTextContains("Kuota")
    }

    @Test fun editorShowsCalendarAndLeavesMissingScanDateEmpty() {
        compose.setContent {
            MaterialTheme {
                TransactionEditor(null, TransactionExtractionValidator().validate(TransactionExtraction.empty(), master()),
                    master(), {}, {})
            }
        }
        compose.onNodeWithTag("editor-date").assertTextContains("Pilih tanggal transaksi").performClick()
        compose.onNodeWithText("Pilih tanggal").assertIsDisplayed()
        capture("date-picker")
        compose.onNodeWithText("Batal").performClick()
        compose.onNodeWithTag("editor-date").assertTextContains("Pilih tanggal transaksi")
    }

    @Test fun incompleteEditorCannotSaveAndAmountIsGrouped() {
        compose.setContent {
            MaterialTheme { TransactionEditor(null, null, master(), {}, { error("Invalid form must not save.") }) }
        }
        compose.onNodeWithTag("editor-amount").performTextInput("68820")
        compose.onNodeWithTag("editor-amount").assertTextContains("68.820")
        compose.onNodeWithTag("editor-amount").performImeAction()
        capture("transaction-editor")
        compose.onNodeWithTag("editor-content").performScrollToNode(hasTestTag("editor-save"))
        compose.onNodeWithTag("editor-save").assertIsNotEnabled()
    }

    @Test fun scanShowsDraftBeforeAiAndKeepsRawOcrCollapsed() {
        val text = "20/09/2026\nSUBTOTAL 65.000\nTOTAL RP 68.820\nKEMBALI 0\nQRIS"
        val ocr = OcrResult(text, emptyList(), 10)
        val output = SmartScanOutput(ScanImageRequest("content://dummy/receipt", ScanImageSource.GALLERY),
            ScanImageMetadata(493, 785, 493, 785, 1), ocr, emptyList())
        val rules = TransactionExtractionNormalizer().normalize(
            EvidenceTransactionExtractor().extract(text, master()), text, master())
        val review = TransactionExtractionValidator().validate(rules, master())
        val result = ExtractionRunResult.RulesOnly(review, ExtractionTrace(ocr, text, master(), rules))
        var editorOpened = false
        compose.setContent {
            MaterialTheme {
                SmartScanScreen(SmartScanState.Complete(output),
                    ScanReviewSnapshot(output, result, review, ScanSuggestionState.RUNNING), false,
                    null, {}, {}, {}, {}, {}, {}, { editorOpened = true })
            }
        }
        compose.onNodeWithTag("scan-raw-ocr").assertDoesNotExist()
        capture("scan-review")
        compose.onNodeWithTag("scan-content").performScrollToNode(hasTestTag("scan-edit"))
        compose.onNodeWithTag("scan-edit").assertIsEnabled().performClick()
        assertTrue(editorOpened)
        compose.onNodeWithTag("scan-content").performScrollToNode(hasTestTag("scan-details-toggle"))
        compose.onNodeWithTag("scan-details-toggle").performClick()
        compose.onNodeWithTag("scan-content").performScrollToNode(hasTestTag("scan-raw-ocr"))
        compose.onNodeWithTag("scan-raw-ocr").assertIsDisplayed()
    }

    @Test fun validEditorSubmitsMasterChoicesAndWholeIdr() {
        var saved: TransactionDraft? = null
        compose.setContent {
            MaterialTheme { TransactionEditor(null, null, master(), {}, { saved = it }) }
        }
        compose.onNodeWithTag("editor-amount").performTextInput("68820")
        compose.onNodeWithTag("editor-amount").performImeAction()
        compose.onNodeWithTag("selection-Pembayaran").performClick()
        compose.onNodeWithText("Bank").performClick()
        compose.onNodeWithTag("selection-Kategori").performClick()
        compose.onNodeWithText("Kebutuhan").performClick()
        compose.onNodeWithTag("editor-content").performScrollToNode(hasTestTag("editor-save"))
        compose.onNodeWithTag("editor-save").assertIsEnabled().performClick()
        compose.runOnIdle {
            assertEquals(68820L, saved?.amountIdr)
            assertEquals("Bank", saved?.paymentMethod)
            assertEquals("Kebutuhan", saved?.category)
        }
    }

    @Test fun editorCanSaveWithoutChoosingPayment() {
        var saved: TransactionDraft? = null
        compose.setContent {
            MaterialTheme { TransactionEditor(null, null, master(), {}, { saved = it }) }
        }
        compose.onNodeWithTag("editor-amount").performTextInput("25000")
        compose.onNodeWithTag("editor-amount").performImeAction()
        compose.onNodeWithTag("selection-Kategori").performClick()
        compose.onNodeWithText("Kebutuhan").performClick()
        compose.onNodeWithTag("editor-content").performScrollToNode(hasTestTag("editor-save"))
        compose.onNodeWithTag("editor-save").assertIsEnabled().performClick()
        compose.runOnIdle {
            assertEquals("", saved?.paymentMethod)
            assertEquals(25000L, saved?.amountIdr)
        }
    }

    @Test fun editingCanClearPaymentWithoutChangingRequiredFields() {
        var saved: TransactionDraft? = null
        val draft = TransactionDraft("2026-10-03", "Pengeluaran", "Bank", "Kebutuhan", 25_000)
        val transaction = LocalTransaction("dummy-edit", 1, draft, SyncState.SYNCED, null, false)
        compose.setContent {
            MaterialTheme { TransactionEditor(transaction, null, master(), {}, { saved = it }) }
        }
        compose.onNodeWithTag("editor-content").performScrollToNode(hasTestTag("selection-Pembayaran"))
        compose.onNodeWithTag("selection-Pembayaran").performClick()
        compose.onNodeWithText("Kosongkan pembayaran").performClick()
        compose.onNodeWithTag("selection-Pembayaran").assertTextContains("Pembayaran (opsional)")
        compose.onNodeWithTag("editor-content").performScrollToNode(hasTestTag("editor-save"))
        compose.onNodeWithTag("editor-save").assertIsEnabled().performClick()
        compose.runOnIdle { assertEquals(draft.copy(paymentMethod = ""), saved) }
    }

    @Test fun homeReminderSurvivesSearchAndFiltersForReview() {
        val draft = TransactionDraft("2026-10-03", "Pengeluaran", "Bank", "Kebutuhan", 25_000)
        val paid = LocalTransaction("dummy-paid", 1, draft, SyncState.SYNCED, null, false)
        val blank = paid.copy(transactionId = "dummy-blank", draft = draft.copy(paymentMethod = ""))
        compose.setContent {
            MaterialTheme {
                var query by remember { mutableStateOf("Bank") }
                HomeScreen(month = YearMonth.of(2026, 10), transactions = listOf(paid, blank),
                    incomeIdr = 0, expenseIdr = 50_000, query = query, syncCounts = SyncCounts(0, 0),
                    offlineSession = false, hasWorkbook = true, hasPendingSharedImage = false,
                    snackbar = remember { SnackbarHostState() }, onQueryChange = { query = it },
                    onPreviousMonth = {}, onNextMonth = {}, onRefresh = {}, onRetry = {}, onCreateWorkbook = {},
                    onAdd = {}, onTransaction = {}, onPickImage = {}, onTakePhoto = {}, onOpenSharedImage = {},
                    onDiscardSharedImage = {}, onSettings = {}, onSignOut = {})
            }
        }
        compose.onNodeWithTag("home-content").performScrollToNode(hasTestTag("transaction-review-toggle"))
        compose.onNodeWithText("1 transaksi pada data bulan ini yang sudah dimuat.", substring = true).assertIsDisplayed()
        capture("optional-payment-reminder")
        compose.onNodeWithTag("transaction-review-toggle").performClick()
        compose.onNodeWithTag("home-content").performScrollToNode(hasTestTag("transaction-dummy-blank"))
        compose.onNodeWithTag("transaction-dummy-blank").assertIsDisplayed()
        compose.onNodeWithTag("transaction-dummy-paid").assertDoesNotExist()
        compose.onNodeWithText("2026-10-03 · Pembayaran belum diisi").assertIsDisplayed()
        compose.onNodeWithTag("home-content").performScrollToNode(hasTestTag("transaction-review-toggle"))
        compose.onNodeWithTag("transaction-review-toggle").performClick()
        compose.onNodeWithTag("home-content").performScrollToNode(hasTestTag("transaction-dummy-paid"))
        compose.onNodeWithTag("transaction-dummy-paid").assertIsDisplayed()
    }

    private fun master() = MasterData(2026, "1", listOf("Pengeluaran", "Pemasukan"), listOf("Cash", "Bank"),
        listOf(MasterCategory("Kebutuhan", "Pengeluaran"), MasterCategory("Gaji", "Pemasukan")))

    private fun capture(name: String) {
        compose.waitForIdle()
        // Compose idleness does not include platform dialog/IME window animations.
        SystemClock.sleep(500)
        val bitmap = InstrumentationRegistry.getInstrumentation().uiAutomation.takeScreenshot() ?: return
        try {
            val context = InstrumentationRegistry.getInstrumentation().targetContext
            val directory = File(context.externalCacheDir ?: context.noBackupFilesDir,
                "ui-smoke").apply { mkdirs() }
            File(directory, "$name.png").outputStream().use { bitmap.compress(Bitmap.CompressFormat.PNG, 100, it) }
        } finally { bitmap.recycle() }
    }
}

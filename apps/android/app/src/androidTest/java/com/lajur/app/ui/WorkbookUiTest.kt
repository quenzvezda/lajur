package com.lajur.app.ui

import androidx.compose.material3.MaterialTheme
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.assertIsEnabled
import androidx.compose.ui.test.assertIsNotEnabled
import androidx.compose.ui.test.hasTestTag
import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollToNode
import androidx.compose.ui.test.performTextReplacement
import androidx.test.platform.app.InstrumentationRegistry
import com.lajur.app.data.SelectedWorkbook
import com.lajur.app.data.remote.WorkbookInfoDto
import java.io.File
import org.junit.Assert.*
import org.junit.Assume.assumeTrue
import org.junit.Before
import org.junit.Rule
import org.junit.Test

/** Synthetic host/callbacks only. Never imports into the user's account. */
class WorkbookUiTest {
    @get:Rule val compose = createComposeRule()
    @Before fun optIn() {
        assumeTrue(InstrumentationRegistry.getArguments().getString("lajur.runUiSmokeTests") == "true")
    }
    private val workbook = WorkbookInfoDto(2026, 120000, "2026-10-03T00:00:00Z")
    private val selected = SelectedWorkbook(File("synthetic-only.xlsm"), "Dummy.xlsm", 120000, "dummy-owner")

    @Test fun pendingIncludingFailedBlocksImportWithoutCallingUploader() {
        var called = false
        compose.setContent { MaterialTheme {
            WorkbookScreen(WorkbookMenuState(listOf(workbook), selected), 1, {}, {}, {}, {}, {}, {}, { _, _ -> called = true }, {})
        } }
        compose.onNodeWithTag("workbook-content").performScrollToNode(hasTestTag("workbook-pick"))
        compose.onNodeWithTag("workbook-pick").assertIsNotEnabled()
        compose.onNodeWithTag("workbook-content").performScrollToNode(hasTestTag("workbook-import"))
        compose.onNodeWithTag("workbook-import").assertIsNotEnabled()
        assertFalse(called)
    }

    @Test fun replacementNeedsOptInAndSeparateConfirmation() {
        var importedYear: Int? = null
        var replaced = false
        compose.setContent { MaterialTheme {
            WorkbookScreen(WorkbookMenuState(listOf(workbook), selected), 0, {}, {}, {}, {}, {}, {}, { y, r -> importedYear = y; replaced = r }, {})
        } }
        compose.onNodeWithTag("workbook-content").performScrollToNode(hasTestTag("workbook-year"))
        compose.onNodeWithTag("workbook-year").performTextReplacement("2026")
        compose.onNodeWithTag("workbook-content").performScrollToNode(hasTestTag("workbook-import"))
        compose.onNodeWithTag("workbook-import").assertIsNotEnabled()
        compose.onNode(androidx.compose.ui.test.isToggleable()).performClick()
        compose.onNodeWithTag("workbook-import").assertIsEnabled().performClick()
        assertNull(importedYear)
        compose.onNodeWithText("Impor workbook 2026?").assertIsDisplayed()
        compose.onNodeWithText("Impor", useUnmergedTree = true).performClick()
        assertEquals(2026, importedYear)
        assertTrue(replaced)
    }

    @Test fun exportWarnsThatPendingChangesAreExcluded() {
        var exported = false
        compose.setContent { MaterialTheme {
            WorkbookScreen(WorkbookMenuState(listOf(workbook)), 2, {}, {}, {}, {}, {}, {}, { _, _ -> }, { exported = true })
        } }
        compose.onNodeWithTag("workbook-content").performScrollToNode(hasText("Ekspor 2026 (.xlsm)"))
        compose.onNodeWithText("Ekspor 2026 (.xlsm)").performClick()
        assertFalse(exported)
        compose.onNodeWithText("Pending tidak ikut diekspor").assertIsDisplayed()
        compose.onNodeWithText("Ekspor data server").performClick()
        assertTrue(exported)
    }
}

package com.lajur.app.ui

import com.lajur.app.domain.LocalTransaction
import com.lajur.app.domain.SyncState
import com.lajur.app.domain.TransactionDraft
import org.junit.Assert.*
import org.junit.Test

class AppNavigationTest {
    private val transaction = LocalTransaction("dummy", 1,
        TransactionDraft("2027-10-05", "Pengeluaran", "", "Jajan", 7_000), SyncState.SYNCED, null, false)

    @Test fun newTransactionBackReturnsHome() {
        assertEquals(AppScreen.Main(), AppScreen.Editor(null).backTarget())
    }

    @Test fun editingReturnsToDetailAndRespectsHistoryOrigin() {
        val detail = AppScreen.Detail(transaction, MainTab.HISTORY)
        assertEquals(detail, AppScreen.Editor(transaction, origin = MainTab.HISTORY).backTarget())
        assertEquals(AppScreen.Main(MainTab.HISTORY), detail.backTarget())
    }

    @Test fun secondaryDestinationsReturnWithoutExiting() {
        MainTab.entries.filterNot { it == MainTab.HOME }.forEach {
            assertEquals(AppScreen.Main(), AppScreen.Main(it).backTarget())
        }
        assertEquals(AppScreen.Main(MainTab.SETTINGS), AppScreen.Workbooks.backTarget())
        assertEquals(AppScreen.Main(MainTab.SCAN), AppScreen.SmartScan.backTarget())
        assertNull(AppScreen.Main().backTarget())
    }

    @Test fun exitNeedsTwoPressesWithinTwoSeconds() {
        val gate = ExitBackPressGate()
        assertFalse(gate.press(0))
        assertTrue(gate.press(2_000))
        assertFalse(gate.press(2_001))
        assertFalse(gate.press(4_002))
        assertTrue(gate.press(4_003))
    }

    @Test fun navigationOrBackgroundResetCannotAccidentallyExit() {
        val gate = ExitBackPressGate()
        assertFalse(gate.press(1_000))
        gate.reset()
        assertFalse(gate.press(1_001))
        assertTrue(gate.press(1_002))
    }
}

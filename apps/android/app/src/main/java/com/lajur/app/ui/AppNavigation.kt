package com.lajur.app.ui

import com.lajur.app.domain.LocalTransaction
import com.lajur.app.scan.extraction.ValidatedTransactionExtraction

internal enum class MainTab(val label: String) {
    HOME("Beranda"), HISTORY("Riwayat"), SCAN("Scan"), INSIGHTS("Insight"), SETTINGS("Setelan")
}

internal sealed interface AppScreen {
    data class Main(val tab: MainTab = MainTab.HOME) : AppScreen
    data object Workbooks : AppScreen
    data object SmartScan : AppScreen
    data class Editor(
        val transaction: LocalTransaction?,
        val extraction: ValidatedTransactionExtraction? = null,
        val origin: MainTab = MainTab.HOME,
    ) : AppScreen
    data class Detail(val transaction: LocalTransaction, val origin: MainTab = MainTab.HOME) : AppScreen
}

internal fun AppScreen.backTarget(): AppScreen? = when (this) {
    is AppScreen.Main -> if (tab == MainTab.HOME) null else AppScreen.Main()
    AppScreen.Workbooks -> AppScreen.Main(MainTab.SETTINGS)
    AppScreen.SmartScan -> AppScreen.Main(MainTab.SCAN)
    is AppScreen.Editor -> if (transaction == null) AppScreen.Main(origin) else AppScreen.Detail(transaction, origin)
    is AppScreen.Detail -> AppScreen.Main(origin)
}

/** Monotonic timestamps; exiting never logs the account out. */
internal class ExitBackPressGate(private val windowMillis: Long = 2_000) {
    private var previousPress: Long? = null

    fun press(nowMillis: Long): Boolean {
        val previous = previousPress
        if (previous != null && nowMillis - previous in 0..windowMillis) {
            reset()
            return true
        }
        previousPress = nowMillis
        return false
    }

    fun reset() { previousPress = null }
}

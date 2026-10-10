package com.lajur.app.ui

import android.graphics.Bitmap
import android.os.SystemClock
import android.view.accessibility.AccessibilityNodeInfo
import androidx.test.core.app.ActivityScenario
import androidx.test.platform.app.InstrumentationRegistry
import java.io.File
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Assume.assumeTrue
import org.junit.Before
import org.junit.Test

/** Opt-in synthetic UI only. Uses platform accessibility to support API 37. */
class AppUpdateUiTest {
    @Before fun optIn() {
        assumeTrue(InstrumentationRegistry.getArguments().getString("lajur.runUpdateUiSmokeTests") == "true")
    }

    @Test fun availableDownloadCancelReadyPermissionAndErrorStates() {
        ActivityScenario.launch(UpdateSmokeActivity::class.java).use { scenario ->
            idle()
            screenshot("update-harness-initial.png")
            click("Unduh APK")
            scenario.onActivity { assertEquals(1, it.downloads) }
            screenshot("update-available-dark.png")
            scenario.onActivity { it.updateState.value = it.updateState.value.copy(progress = 35) }
            idle()
            click("Batalkan unduhan")
            scenario.onActivity { assertEquals(1, it.cancellations) }
            screenshot("update-downloading-dark.png")
            scenario.onActivity {
                it.dark.value = false
                it.updateState.value = it.updateState.value.copy(progress = null, ready = true)
            }
            idle()
            click("Izinkan pemasangan")
            scenario.onActivity { assertEquals(1, it.installations) }
            screenshot("update-ready-light.png")
            scenario.onActivity { it.updateState.value = it.updateState.value.copy(ready = false, error = "Unduhan gagal. Coba lagi.") }
            idle()
            awaitNode("Unduhan gagal. Coba lagi.")
            assertTrue(nodes("Unduh APK").isNotEmpty())
            scenario.onActivity { it.updateState.value = it.updateState.value.copy(error = null, incompatibility = "Build pengembangan tidak kompatibel.") }
            idle()
            awaitNode("Build pengembangan tidak kompatibel.")
            assertTrue(nodes("Unduh APK").isEmpty())
            assertTrue(nodes("Catatan rilis").isNotEmpty())
        }
    }

    private fun idle() {
        InstrumentationRegistry.getInstrumentation().waitForIdleSync()
        InstrumentationRegistry.getInstrumentation().uiAutomation.waitForIdle(500, 10_000)
    }

    private fun nodes(text: String): List<AccessibilityNodeInfo> {
        val result = mutableListOf<AccessibilityNodeInfo>()
        fun visit(node: AccessibilityNodeInfo) {
            if (node.text?.toString() == text || node.contentDescription?.toString() == text) result.add(node)
            for (index in 0 until node.childCount) node.getChild(index)?.let(::visit)
        }
        InstrumentationRegistry.getInstrumentation().uiAutomation.rootInActiveWindow?.let(::visit)
        return result
    }

    private fun click(text: String) {
        awaitNode(text)
        var node = checkNotNull(nodes(text).firstOrNull()) { "Missing control: $text" }
        while (!node.isClickable) node = checkNotNull(node.parent) { "Control is not clickable: $text" }
        assertTrue(node.performAction(AccessibilityNodeInfo.ACTION_CLICK))
        idle()
    }

    private fun awaitNode(text: String) {
        val deadline = SystemClock.uptimeMillis() + 10_000
        while (nodes(text).isEmpty() && SystemClock.uptimeMillis() < deadline) SystemClock.sleep(100)
        assertTrue("Missing control: $text", nodes(text).isNotEmpty())
    }

    private fun screenshot(name: String) {
        idle()
        val instrument = InstrumentationRegistry.getInstrumentation()
        val bitmap = checkNotNull(instrument.uiAutomation.takeScreenshot())
        File(instrument.targetContext.cacheDir, name).outputStream().use { bitmap.compress(Bitmap.CompressFormat.PNG, 100, it) }
    }
}

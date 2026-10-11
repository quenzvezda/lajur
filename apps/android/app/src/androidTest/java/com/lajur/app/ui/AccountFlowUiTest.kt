package com.lajur.app.ui

import android.content.Intent
import android.os.SystemClock
import android.view.WindowManager
import android.view.accessibility.AccessibilityNodeInfo
import androidx.test.core.app.ActivityScenario
import androidx.test.platform.app.InstrumentationRegistry
import android.view.KeyEvent
import com.lajur.app.data.remote.*
import kotlinx.coroutines.runBlocking
import kotlinx.serialization.json.Json
import org.junit.Assert.*
import org.junit.Assume.assumeTrue
import org.junit.Before
import org.junit.Test

/** Opt-in local synthetic backend. Never accepts production credentials/URL. */
class AccountFlowUiTest {
    private val instrument get() = InstrumentationRegistry.getInstrumentation()
    private val server = "http://127.0.0.1:5088/api/v1/" // emulator-only adb reverse
    private val password = "synthetic-ui-password-123"

    @Before fun optIn() {
        assumeTrue(InstrumentationRegistry.getArguments().getString("lajur.runAccountUiSmokeTests") == "true")
    }

    @Test fun registerSaveCodesAndRecoverOnRealAndroidScreen() = runBlocking {
        val api = LajurApiClient(server, Json { ignoreUnknownKeys = true })
        val owner = api.login(LoginRequest("ui-owner", password, deviceLabel = "Synthetic instrumentation"))
        val invitation = api.invitation(password, owner.accessToken)
        val username = "android-smoke-${SystemClock.uptimeMillis()}"
        var recoveryCode = ""
        ActivityScenario.launch<AccountSmokeActivity>(Intent(instrument.targetContext, AccountSmokeActivity::class.java)).use { scenario ->
            idle()
            scenario.onActivity { assertTrue(it.window.attributes.flags and WindowManager.LayoutParams.FLAG_SECURE != 0) }
            edit("Alamat server", server)
            edit("Username", username)
            edit("Kode undangan", invitation.code)
            edit("Password · minimal 12 karakter", password)
            edit("Ulangi password", password)
            click("Daftar")
            awaitText("Simpan kode pemulihan")
            val allText = allNodes().mapNotNull { it.text?.toString() }.joinToString("\n")
            val codes = Regex("[A-F0-9]{4}(?:-[A-F0-9]{4}){7}").findAll(allText).map { it.value }.distinct().toList()
            assertEquals(8, codes.size)
            recoveryCode = codes.first()
            assertTrue(allNodes().any { it.text?.toString() == "Salin kode" })
            click("Sudah disimpan, selesai")
        }
        ActivityScenario.launch<AccountSmokeActivity>(Intent(instrument.targetContext, AccountSmokeActivity::class.java).putExtra("mode", "recover")).use { scenario ->
            idle()
            edit("Alamat server", server)
            edit("Username", username)
            edit("Kode pemulihan", recoveryCode)
            edit("Password baru · minimal 12 karakter", password + "-new")
            edit("Ulangi password", password + "-new")
            click("Ganti password")
            val deadline = SystemClock.uptimeMillis() + 20_000
            var done = false
            while (!done && SystemClock.uptimeMillis() < deadline) {
                scenario.onActivity {
                    assertNull("Recovery UI error", it.viewModel.account.value.error)
                    done = it.viewModel.account.value.message != null
                }
                if (!done) SystemClock.sleep(100)
            }
            assertTrue("Recovery must complete", done)
            awaitText("Kembali masuk")
        }
        assertTrue(api.login(LoginRequest(username, password + "-new", deviceLabel = "Synthetic verification")).accessToken.isNotEmpty())
    }

    private fun allNodes(): List<AccessibilityNodeInfo> {
        val result = mutableListOf<AccessibilityNodeInfo>()
        fun visit(node: AccessibilityNodeInfo) {
            result.add(node)
            for (index in 0 until node.childCount) node.getChild(index)?.let(::visit)
        }
        instrument.uiAutomation.rootInActiveWindow?.let(::visit)
        return result
    }

    private fun idle() { instrument.waitForIdleSync(); instrument.uiAutomation.waitForIdle(300, 10_000) }
    private fun find(text: String) = allNodes().firstOrNull { it.text?.toString() == text || it.hintText?.toString() == text || it.contentDescription?.toString() == text }
    private fun awaitText(text: String) {
        val deadline = SystemClock.uptimeMillis() + 20_000
        while (find(text) == null && SystemClock.uptimeMillis() < deadline) SystemClock.sleep(100)
        assertNotNull("Expected account UI control/result missing", find(text))
    }
    private fun edit(label: String, value: String) {
        if (label == "Alamat server") {
            awaitText(label)
            var node = checkNotNull(find(label))
            while (!node.isEditable) node = checkNotNull(node.parent)
            assertTrue(node.performAction(AccessibilityNodeInfo.ACTION_CLICK))
        } else {
            instrument.sendKeyDownUpSync(KeyEvent.KEYCODE_TAB)
        }
        idle()
        // The API 37 IME restarts when changing to a password keyboard. Give that
        // restart time to complete before injecting the first key.
        SystemClock.sleep(600)
        instrument.sendKeySync(KeyEvent(0, 0, KeyEvent.ACTION_DOWN, KeyEvent.KEYCODE_A, 0, KeyEvent.META_CTRL_ON))
        instrument.sendKeySync(KeyEvent(0, 0, KeyEvent.ACTION_UP, KeyEvent.KEYCODE_A, 0, KeyEvent.META_CTRL_ON))
        instrument.sendKeyDownUpSync(KeyEvent.KEYCODE_DEL)
        instrument.waitForIdleSync()
        SystemClock.sleep(100)
        value.forEach {
            instrument.sendStringSync(it.toString())
            instrument.waitForIdleSync()
            SystemClock.sleep(30)
        }
        idle()
        val deadline = SystemClock.uptimeMillis() + 3_000
        while (allNodes().firstOrNull { it.isEditable && it.isFocused }?.text?.length != value.length &&
            SystemClock.uptimeMillis() < deadline) SystemClock.sleep(50)
        assertEquals("Focused field must retain all input: $label", value.length,
            allNodes().firstOrNull { it.isEditable && it.isFocused }?.text?.length)
    }
    private fun click(label: String) {
        repeat(5) {
            if (find(label) == null) {
                allNodes().firstOrNull { it.isScrollable }?.performAction(AccessibilityNodeInfo.ACTION_SCROLL_FORWARD)
                idle()
            }
        }
        awaitText(label)
        var node = checkNotNull(find(label))
        while (!node.isClickable) node = checkNotNull(node.parent)
        node.performAction(AccessibilityNodeInfo.AccessibilityAction.ACTION_SHOW_ON_SCREEN.id)
        idle()
        node = checkNotNull(find(label))
        while (!node.isClickable) node = checkNotNull(node.parent)
        assertTrue("Control must be enabled: $label; editable lengths=" +
            allNodes().filter { it.isEditable }.joinToString(",") { (it.text?.length ?: 0).toString() }, node.isEnabled)
        assertTrue("Control must accept click: $label", node.performAction(AccessibilityNodeInfo.ACTION_CLICK))
        idle()
    }
}

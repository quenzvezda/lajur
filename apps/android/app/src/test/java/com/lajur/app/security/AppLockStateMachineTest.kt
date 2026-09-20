package com.lajur.app.security

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class AppLockStateMachineTest {
    @Test
    fun `cold restore locks an enabled app`() {
        val machine = AppLockStateMachine()

        machine.restore(AppLockSettings(enabled = true))

        assertEquals(AppLockStatus.LOCKED, machine.status)
    }

    @Test
    fun `cold restore opens an app with lock disabled`() {
        val machine = AppLockStateMachine()

        machine.restore(AppLockSettings(enabled = false))

        assertEquals(AppLockStatus.UNLOCKED, machine.status)
    }

    @Test
    fun `returning inside grace period stays unlocked`() {
        val machine = unlockedMachine(timeoutMillis = 300_000)

        machine.onBackgrounded(1_000)
        machine.onForegrounded(300_999)

        assertEquals(AppLockStatus.UNLOCKED, machine.status)
    }

    @Test
    fun `returning at timeout locks app`() {
        val machine = unlockedMachine(timeoutMillis = 300_000)

        machine.onBackgrounded(1_000)
        machine.onForegrounded(301_000)

        assertEquals(AppLockStatus.LOCKED, machine.status)
    }

    @Test
    fun `successful authentication unlocks and clears old timeout`() {
        val machine = unlockedMachine(timeoutMillis = 60_000)
        machine.lockNow()

        machine.authenticationSucceeded()
        machine.onForegrounded(999_999)

        assertEquals(AppLockStatus.UNLOCKED, machine.status)
    }

    @Test
    fun `disabling lock always exposes app`() {
        val machine = unlockedMachine()
        machine.lockNow()

        machine.updateSettings(machine.settings.copy(enabled = false))

        assertEquals(AppLockStatus.UNLOCKED, machine.status)
    }

    @Test
    fun `shared image waits while locked and becomes ready after unlock`() {
        val machine = unlockedMachine()
        machine.lockNow()

        assertTrue(machine.gateSharedImage("content://receipt") is ShareIntentGate.AwaitingUnlock)

        machine.authenticationSucceeded()
        assertEquals(ShareIntentGate.Ready("content://receipt"), machine.gateSharedImage("content://receipt"))
    }

    @Test
    fun `immediate timeout locks on next foreground`() {
        val machine = unlockedMachine(timeoutMillis = 0)

        machine.onBackgrounded(5_000)
        machine.onForegrounded(5_000)

        assertEquals(AppLockStatus.LOCKED, machine.status)
    }

    @Test
    fun `screen protection preference changes without changing unlock state`() {
        val machine = unlockedMachine()

        machine.updateSettings(machine.settings.copy(screenProtectionEnabled = false))

        assertEquals(AppLockStatus.UNLOCKED, machine.status)
        assertEquals(false, machine.settings.screenProtectionEnabled)
    }

    private fun unlockedMachine(timeoutMillis: Long = DEFAULT_APP_LOCK_TIMEOUT_MILLIS) =
        AppLockStateMachine().apply {
            restore(AppLockSettings(enabled = true, timeoutMillis = timeoutMillis))
            authenticationSucceeded()
        }
}

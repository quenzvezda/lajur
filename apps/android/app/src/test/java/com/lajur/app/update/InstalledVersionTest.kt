package com.lajur.app.update

import org.junit.Assert.*
import org.junit.Test

class InstalledVersionTest {
    private val release = AppUpdateManifest(1, 12, "1.0.12", "com.lajur.app", 26,
        "", "", 100, "", "", "")
    @Test fun installedUpdateClearsReadyStateWithoutNetworkCheck() {
        val stale = AppUpdateState(installedVersionCode = 11, installedVersionName = "1.0.11", release = release, ready = true)
        val current = stale.reconcileInstalled(12, "1.0.12")
        assertEquals("1.0.12", current.installedVersionName)
        assertNull(current.release); assertFalse(current.ready); assertTrue(current.checked)
        assertEquals(current, current.reconcileInstalled(12, "1.0.12"))
    }
    @Test fun newerUpdateAndPreferencesArePreserved() {
        val stale = AppUpdateState(release = release, ready = true, autoCheck = false)
        val current = stale.reconcileInstalled(11, "1.0.11")
        assertEquals(release, current.release);assertTrue(current.ready);assertFalse(current.autoCheck)
    }
}

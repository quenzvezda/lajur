package com.lajur.app.ui

import org.junit.Assert.*
import org.junit.Test

class AccountUiStateTest {
    @Test fun passwordRequiresLengthAndMatchingConfirmation() {
        assertFalse(validNewAccountPassword("short", "short"))
        assertFalse(validNewAccountPassword("a sufficiently long password", "different"))
        assertTrue(validNewAccountPassword("a sufficiently long password", "a sufficiently long password"))
        assertFalse(validNewAccountPassword("a".repeat(257), "a".repeat(257)))
    }
}

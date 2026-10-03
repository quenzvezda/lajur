package com.lajur.app.data

import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import org.junit.Assert.*
import org.junit.Test

class WorkbookTransfersTest {
    @Test fun `year is explicit bounded ASCII and filenames are controlled`() {
        assertEquals(2026, WorkbookTransfers.year("2026"))
        listOf("", "26", "1899", "10000", "2026/../", "２０２６", " 2026").forEach { assertNull(WorkbookTransfers.year(it)) }
        assertEquals("Keuangan_Pribadi_2026.xlsm", WorkbookTransfers.fileName(2026))
        assertEquals("Lajur_Workbooks.zip", WorkbookTransfers.fileName(null))
    }

    @Test fun `stream copy preserves binary bytes and exact limit`() {
        val bytes = ByteArray(70_000) { (it % 256).toByte() }
        val output = ByteArrayOutputStream()
        assertEquals(bytes.size.toLong(), WorkbookTransfers.copyLimited(ByteArrayInputStream(bytes), output, bytes.size.toLong()))
        assertArrayEquals(bytes, output.toByteArray())
    }

    @Test fun `unknown-size streams cannot bypass limit or supply empty file`() {
        assertThrows(IllegalStateException::class.java) {
            WorkbookTransfers.copyLimited(ByteArrayInputStream(ByteArray(10)), ByteArrayOutputStream(), 9)
        }
        assertThrows(IllegalStateException::class.java) {
            WorkbookTransfers.copyLimited(ByteArrayInputStream(byteArrayOf()), ByteArrayOutputStream(), 10)
        }
    }
}

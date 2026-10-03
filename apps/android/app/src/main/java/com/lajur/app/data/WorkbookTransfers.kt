package com.lajur.app.data

import java.io.InputStream
import java.io.OutputStream

object WorkbookTransfers {
    const val XLSM_TYPE = "application/vnd.ms-excel.sheet.macroEnabled.12"
    const val ZIP_TYPE = "application/zip"
    const val MAX_IMPORT_BYTES = 20L * 1024 * 1024
    const val MAX_EXPORT_BYTES = 256L * 1024 * 1024

    fun year(value: String): Int? = value.takeIf { it.length == 4 && it.all { c -> c in '0'..'9' } }
        ?.toIntOrNull()?.takeIf { it in 1900..9999 }

    fun fileName(year: Int?): String = if (year == null) "Lajur_Workbooks.zip" else "Keuangan_Pribadi_$year.xlsm"

    fun copyLimited(input: InputStream, output: OutputStream, limit: Long, checkActive: () -> Unit = {}): Long {
        val buffer = ByteArray(32 * 1024)
        var total = 0L
        while (true) {
            checkActive()
            val count = input.read(buffer)
            if (count < 0) break
            check(total + count <= limit) { "Ukuran file melebihi batas yang diizinkan." }
            output.write(buffer, 0, count)
            total += count
        }
        check(total > 0) { "File kosong tidak dapat diproses." }
        return total
    }
}

package com.lajur.app.scan.extraction

import com.lajur.app.scan.OcrResult
import com.lajur.app.scan.OcrTextBlock
import kotlin.math.abs
import kotlin.math.min

/** Joins horizontally separated OCR lines only when their vertical positions agree. */
object OcrReadingOrder {
    fun text(ocr: OcrResult): String {
        if (ocr.lines.isEmpty() || ocr.lines.any { !it.hasPosition() }) return ocr.text
        val rows = mutableListOf<MutableList<OcrTextBlock>>()
        ocr.lines.sortedWith(compareBy<OcrTextBlock> { it.centerY() }.thenBy { it.left }).forEach { line ->
            val row = rows.lastOrNull()
            val anchor = row?.firstOrNull()
            if (anchor != null && sameRow(anchor, line)) row.add(line)
            else rows.add(mutableListOf(line))
        }
        return rows.joinToString("\n") { row ->
            row.sortedBy { it.left }.joinToString(" ") { it.text.trim() }
        }
    }

    private fun OcrTextBlock.hasPosition() = left != null && top != null && right != null &&
        bottom != null && right > left && bottom > top

    private fun OcrTextBlock.centerY() = (requireNotNull(top).toDouble() + requireNotNull(bottom)) / 2

    private fun sameRow(first: OcrTextBlock, second: OcrTextBlock): Boolean {
        val height = min(requireNotNull(first.bottom) - requireNotNull(first.top),
            requireNotNull(second.bottom) - requireNotNull(second.top))
        return abs(first.centerY() - second.centerY()) <= height * 0.45
    }
}

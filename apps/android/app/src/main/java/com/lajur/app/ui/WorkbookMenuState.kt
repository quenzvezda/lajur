package com.lajur.app.ui

import com.lajur.app.data.SelectedWorkbook
import com.lajur.app.data.remote.WorkbookInfoDto

data class WorkbookMenuState(
    val workbooks: List<WorkbookInfoDto> = emptyList(),
    val selected: SelectedWorkbook? = null,
    val busy: Boolean = false,
    val error: String? = null,
    val status: String? = null,
)

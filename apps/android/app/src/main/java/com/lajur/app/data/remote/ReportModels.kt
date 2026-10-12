package com.lajur.app.data.remote

import kotlinx.serialization.Serializable

@Serializable data class YearReportDto(
    val year: Int, val today: String, val lastModifiedAtUtc: String,
    val months: List<MonthReportDto>, val incomeIdr: Long, val expenseIdr: Long,
    val netIdr: Long, val savingRate: Double? = null, val transactionCount: Int,
)
@Serializable data class MonthReportDto(
    val month: Int, val incomeIdr: Long, val expenseIdr: Long, val netIdr: Long,
    val cumulativeNetIdr: Long, val savingRate: Double? = null, val transactionCount: Int,
    val expenseByCategory: List<CategoryAmountDto>, val rows: List<ReportRowDto>,
    val openingBalanceIdr: Long? = null, val estimatedClosingIdr: Long? = null,
    val actualBalanceIdr: Long? = null, val balanceDifferenceIdr: Long? = null,
    val pace: SpendingPaceDto? = null, val projection: CashProjectionDto? = null,
)
@Serializable data class CategoryAmountDto(
    val category: String, val amountIdr: Long, val transactionCount: Int, val monthlyBudgetIdr: Long? = null,
)
@Serializable data class ReportRowDto(
    val date: String, val type: String, val paymentMethod: String, val category: String,
    val amountIdr: Long, val description: String? = null, val transactionId: String? = null,
    val revision: Int? = null,
)
@Serializable data class SpendingPaceDto(
    val elapsedDays: Int, val daysInMonth: Int, val spentIdr: Long,
    val averagePerDayIdr: Long, val projectedMonthEndIdr: Long,
    val historicalMonthlyAverageIdr: Long, val totalMasterBudgetIdr: Long, val status: String,
)
@Serializable data class CashProjectionDto(
    val sources: List<IncomeSourceDto>, val estimateTotalIdr: Long? = null,
    val actualTotalIdr: Long? = null, val remainingTotalIdr: Long? = null,
    val recordedIncomeIdr: Long, val projectedIncomeIdr: Long? = null,
    val projectedExpenseIdr: Long, val projectedNetIdr: Long? = null,
    val projectedClosingIdr: Long? = null, val complete: Boolean,
)
@Serializable data class IncomeSourceDto(
    val label: String, val payoutDay: Int? = null, val estimateIdr: Long? = null,
    val incomeCategory: String? = null, val actualIdr: Long? = null,
    val remainingIdr: Long? = null, val status: String, val mapped: Boolean,
)

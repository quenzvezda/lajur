using Lajur.Application.Finance;
using Lajur.Infrastructure.Workbooks;
using Microsoft.Extensions.Logging;

namespace Lajur.Infrastructure.Finance;

public sealed class WorkbookFinanceService(
    OpenXmlWorkbookStore workbookStore,
    ILogger<WorkbookFinanceService> logger) : IFinanceService
{
    public async Task<FinanceMasterData> GetMasterDataAsync(
        Guid userId,
        int year,
        CancellationToken cancellationToken)
    {
        var master = await workbookStore.GetMasterDataAsync(
            new WorkbookKey(userId, year),
            cancellationToken);
        return new FinanceMasterData(
            master.WorkbookYear,
            "lajur-workbook-v1",
            master.TransactionTypes,
            master.PaymentMethods,
            master.Categories.Select(category => new FinanceCategory(
                category.Name,
                category.Kind,
                category.MonthlyBudgetIdr)).ToArray());
    }

    public async Task<IReadOnlyList<FinanceTransaction>> ListTransactionsAsync(
        Guid userId,
        int year,
        int month,
        string? query,
        CancellationToken cancellationToken)
    {
        var transactions = await workbookStore.ListTransactionsAsync(
            new WorkbookKey(userId, year),
            month,
            cancellationToken);
        var filtered = string.IsNullOrWhiteSpace(query)
            ? transactions
            : transactions.Where(transaction => MatchesQuery(transaction, query.Trim())).ToArray();
        return filtered
            .OrderByDescending(transaction => transaction.Date)
            .ThenBy(transaction => transaction.TransactionId)
            .Select(Map)
            .ToArray();
    }

    public async Task<FinanceSummary> GetSummaryAsync(
        Guid userId,
        int year,
        int month,
        CancellationToken cancellationToken)
    {
        var rows = await workbookStore.ListCanonicalRowsAsync(
            new WorkbookKey(userId, year),
            month,
            cancellationToken);
        return FinanceSummaryCalculator.Calculate(
            year,
            month,
            rows.Select(row => new FinanceAmount(row.Type, row.AmountIdr)));
    }

    public async Task<FinanceTransaction> CreateTransactionAsync(
        Guid userId,
        Guid deviceId,
        string correlationId,
        Guid transactionId,
        FinanceTransactionDraft transaction,
        CancellationToken cancellationToken)
    {
        var result = await workbookStore.CreateTransactionAsync(
            new WorkbookKey(userId, transaction.Date.Year),
            new CreateWorkbookTransactionCommand(
                transactionId,
                Map(transaction),
                Audit(deviceId, correlationId)),
            cancellationToken);
        LogMutation("create", userId, transaction.Date.Year, transactionId, result.Revision);
        return Map(result);
    }

    public async Task<FinanceTransaction> UpdateTransactionAsync(
        Guid userId,
        Guid deviceId,
        string correlationId,
        int workbookYear,
        Guid transactionId,
        int expectedRevision,
        FinanceTransactionDraft transaction,
        CancellationToken cancellationToken)
    {
        var result = await workbookStore.UpdateTransactionAsync(
            new WorkbookKey(userId, workbookYear),
            new UpdateWorkbookTransactionCommand(
                transactionId,
                expectedRevision,
                Map(transaction),
                Audit(deviceId, correlationId)),
            cancellationToken);
        LogMutation("update", userId, workbookYear, transactionId, result.Revision);
        return Map(result);
    }

    public async Task DeleteTransactionAsync(
        Guid userId,
        Guid deviceId,
        string correlationId,
        int workbookYear,
        Guid transactionId,
        int expectedRevision,
        CancellationToken cancellationToken)
    {
        await workbookStore.DeleteTransactionAsync(
            new WorkbookKey(userId, workbookYear),
            new DeleteWorkbookTransactionCommand(
                transactionId,
                expectedRevision,
                Audit(deviceId, correlationId)),
            cancellationToken);
        LogMutation("delete", userId, workbookYear, transactionId, expectedRevision + 1);
    }

    private void LogMutation(string operation, Guid userId, int year, Guid transactionId, int revision) =>
        logger.LogInformation(
            "Workbook transaction {Operation} succeeded for user {UserId}, year {Year}, transaction {TransactionId}, revision {Revision}",
            operation,
            userId,
            year,
            transactionId,
            revision);

    private static bool MatchesQuery(WorkbookTransaction transaction, string query) =>
        transaction.Type.Contains(query, StringComparison.OrdinalIgnoreCase)
        || transaction.PaymentMethod.Contains(query, StringComparison.OrdinalIgnoreCase)
        || transaction.Category.Contains(query, StringComparison.OrdinalIgnoreCase)
        || (transaction.Description?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);

    private static WorkbookAuditContext Audit(Guid deviceId, string correlationId) =>
        new(WorkbookChangeSource.Android, deviceId.ToString("D"), correlationId);

    private static WorkbookTransactionDraft Map(FinanceTransactionDraft transaction) => new(
        transaction.Date,
        transaction.Type.Trim(),
        transaction.PaymentMethod.Trim(),
        transaction.Category.Trim(),
        transaction.AmountIdr,
        string.IsNullOrWhiteSpace(transaction.Description) ? null : transaction.Description.Trim());

    private static FinanceTransaction Map(WorkbookTransaction transaction) => new(
        transaction.TransactionId,
        transaction.Revision,
        transaction.Date,
        transaction.Type,
        transaction.PaymentMethod,
        transaction.Category,
        transaction.AmountIdr,
        transaction.Description);
}

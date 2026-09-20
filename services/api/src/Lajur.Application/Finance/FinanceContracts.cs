namespace Lajur.Application.Finance;

public sealed record FinanceTransaction(
    Guid TransactionId,
    int Revision,
    DateOnly Date,
    string Type,
    string PaymentMethod,
    string Category,
    long AmountIdr,
    string? Description);

public sealed record FinanceCategory(string Name, string Kind, long? MonthlyBudgetIdr);

public sealed record FinanceMasterData(
    int WorkbookYear,
    string SchemaVersion,
    IReadOnlyList<string> TransactionTypes,
    IReadOnlyList<string> PaymentMethods,
    IReadOnlyList<FinanceCategory> Categories);

public sealed record FinanceSummary(
    int Year,
    int Month,
    long IncomeIdr,
    long ExpenseIdr,
    int TransactionCount);

public sealed record FinanceTransactionDraft(
    DateOnly Date,
    string Type,
    string PaymentMethod,
    string Category,
    long AmountIdr,
    string? Description);

public interface IFinanceService
{
    Task<FinanceMasterData> GetMasterDataAsync(Guid userId, int year, CancellationToken cancellationToken);
    Task<IReadOnlyList<FinanceTransaction>> ListTransactionsAsync(
        Guid userId,
        int year,
        int month,
        string? query,
        CancellationToken cancellationToken);
    Task<FinanceSummary> GetSummaryAsync(
        Guid userId,
        int year,
        int month,
        CancellationToken cancellationToken);
    Task<FinanceTransaction> CreateTransactionAsync(
        Guid userId,
        Guid deviceId,
        string correlationId,
        Guid transactionId,
        FinanceTransactionDraft transaction,
        CancellationToken cancellationToken);
    Task<FinanceTransaction> UpdateTransactionAsync(
        Guid userId,
        Guid deviceId,
        string correlationId,
        int workbookYear,
        Guid transactionId,
        int expectedRevision,
        FinanceTransactionDraft transaction,
        CancellationToken cancellationToken);
    Task DeleteTransactionAsync(
        Guid userId,
        Guid deviceId,
        string correlationId,
        int workbookYear,
        Guid transactionId,
        int expectedRevision,
        CancellationToken cancellationToken);
}

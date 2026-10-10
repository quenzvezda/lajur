namespace Lajur.Infrastructure.Workbooks;

public sealed record WorkbookTransactionRow(
    DateOnly Date,
    string Type,
    string PaymentMethod,
    string Category,
    long AmountIdr,
    string? Description,
    Guid? TransactionId = null,
    int? Revision = null);

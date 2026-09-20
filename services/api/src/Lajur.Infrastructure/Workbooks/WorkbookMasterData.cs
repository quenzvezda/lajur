namespace Lajur.Infrastructure.Workbooks;

public sealed record WorkbookCategory(string Name, string Kind, long? MonthlyBudgetIdr);

public sealed record WorkbookMasterData(
    int WorkbookYear,
    IReadOnlyList<string> TransactionTypes,
    IReadOnlyList<string> PaymentMethods,
    IReadOnlyList<WorkbookCategory> Categories,
    IReadOnlyDictionary<string, string> DefinedNames);

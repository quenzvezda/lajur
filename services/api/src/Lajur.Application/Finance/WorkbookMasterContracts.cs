namespace Lajur.Application.Finance;

public sealed record MasterEntry(string Name, string? Kind, string? BudgetIdr, string? Icon,
    int TransactionCount, int HistoryCount, int DependencyCount, bool CanRename, bool CanDelete, string? Reason);
public sealed record MasterManagement(string Version, IReadOnlyList<MasterEntry> Categories, IReadOnlyList<MasterEntry> Payments,
    int CategoryCapacity, int PaymentCapacity, bool CanAddCategory, bool CanAddPayment, string? CategoryReason, string? PaymentReason);
public sealed record ChangeWorkbookMaster(Guid OperationId, string ExpectedVersion, string Entity, string Action,
    string? OriginalName, string? Name, string? Kind, string? BudgetIdr, string? Icon);

public interface IWorkbookMasterService
{
    Task<MasterManagement> GetAsync(Guid userId, int year, CancellationToken cancellationToken = default);
    Task<MasterManagement> ChangeAsync(Guid userId, int year, ChangeWorkbookMaster command, string correlationId, CancellationToken cancellationToken = default);
}

public static class MasterInput
{
    public static readonly string[] Icons = ["cash", "bank", "wallet", "qr", "shopping", "card", "other"];
    public static string Name(string? value)
    {
        var name = value?.Trim() ?? "";
        if (name.Length is < 1 or > 100 || name.Any(char.IsControl) || name.IndexOfAny(['*', '?', '~']) >= 0)
            throw new ArgumentException("Nama wajib diisi, maksimal 100 karakter, tanpa karakter kontrol atau wildcard Excel (* ? ~).");
        return name;
    }
    public static string DefaultIcon(string name)
    {
        var text = name.ToLowerInvariant();
        return text.Contains("cash") || text.Contains("tunai") ? "cash"
            : text.Contains("qris") || text.Contains("qr") ? "qr"
            : text.Contains("shopee") || text.Contains("belanja") ? "shopping"
            : text.Contains("bank") || text.Contains("transfer") ? "bank"
            : text.Contains("wallet") || text.Contains("dompet") || text.Contains("gopay") || text.Contains("dana") || text.Contains("ovo") ? "wallet"
            : text.Contains("kartu") || text.Contains("card") ? "card" : "other";
    }
}

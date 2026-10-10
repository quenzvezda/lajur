using System.Globalization;

namespace Lajur.Application.Finance;

public sealed record BudgetSetting(string Category, string? AmountIdr, bool Editable, string? Reason);
public sealed record BudgetSettings(string Version, IReadOnlyList<BudgetSetting> Categories);
public sealed record IncomeProjectionSetting(string Id, string Label, int? PayoutDay, string? EstimateIdr,
    bool IsMealAllowance, int? WorkDays, string? DailyRateIdr, bool Editable, string? Reason);
public sealed record ProjectionSettings(int Month, string Version, IReadOnlyList<IncomeProjectionSetting> Sources);
public sealed record BudgetChange(string Category, string? AmountIdr);
public sealed record ProjectionChange(string Id, int? PayoutDay, string? EstimateIdr, int? WorkDays, string? DailyRateIdr);
public sealed record SettingsOperation(Guid OperationId, string Scope);
public sealed record SaveBudgetSettings(Guid OperationId, string ExpectedVersion, IReadOnlyList<BudgetChange> Changes);
public sealed record SaveProjectionSettings(Guid OperationId, string ExpectedVersion, IReadOnlyList<ProjectionChange> Changes);

public interface IWorkbookSettingsService
{
    Task<BudgetSettings> GetBudgetAsync(Guid userId, int year, CancellationToken cancellationToken = default);
    Task<ProjectionSettings> GetProjectionAsync(Guid userId, int year, int month, CancellationToken cancellationToken = default);
    Task<SettingsOperation?> GetOperationAsync(Guid userId, int year, Guid operationId, CancellationToken cancellationToken = default);
    Task<BudgetSettings> SaveBudgetAsync(Guid userId, int year, SaveBudgetSettings command, string correlationId, CancellationToken cancellationToken = default);
    Task<ProjectionSettings> SaveProjectionAsync(Guid userId, int year, int month, SaveProjectionSettings command, string correlationId, CancellationToken cancellationToken = default);
}

public static class SettingsInput
{
    public static long? Amount(string? value)
    {
        if (value is null) return null;
        if (value.Length is < 1 or > 19 || value.Any(c => c is < '0' or > '9')
            || !long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var amount))
            throw new ArgumentException("Nominal harus rupiah bulat nonnegatif dalam batas yang didukung; kosongkan untuk belum diatur.");
        return amount;
    }

    public static void Validate(Guid operationId, string version, int count)
    {
        if (operationId == Guid.Empty || string.IsNullOrWhiteSpace(version) || version.Length != 64)
            throw new ArgumentException("Identitas operasi dan versi pengaturan wajib diisi.");
        if (count is < 1 or > 100) throw new ArgumentException("Simpan antara 1 dan 100 perubahan dalam satu operasi.");
    }
}

using System.Text.Json;
using Lajur.Api.Web;
using Lajur.Application.Reports;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Lajur.Api.Pages;

/// <summary>
/// Read-only workbook viewer for one user/year. Every number comes from one consistent workbook read;
/// opening this page never writes, imports, migrates or recalculates the workbook.
/// </summary>
public sealed class BukuModel(
    IFinanceReportService reports,
    TimeProvider timeProvider,
    ILogger<BukuModel> logger) : PageModel
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public int? Year { get; private set; }
    public IReadOnlyList<int> Years { get; private set; } = [];
    public string Username { get; private set; } = string.Empty;
    public YearReport? Report { get; private set; }
    public string? ErrorMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync(int? year, CancellationToken cancellationToken)
    {
        if (!User.TryGetWebUserId(out var userId))
        {
            return Challenge(WebViewerSetup.Scheme);
        }

        Username = User.Identity?.Name ?? string.Empty;
        Years = await reports.ListYearsAsync(userId, cancellationToken);
        if (year is null)
        {
            if (Years.Count == 0)
            {
                return Page();
            }

            var currentYear = timeProvider.GetLocalNow().Year;
            return RedirectToPage(new { year = Years.Contains(currentYear) ? currentYear : Years[^1] });
        }

        Year = year;
        if (!Years.Contains(year.Value))
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            ErrorMessage = $"Belum ada buku untuk tahun {year}.";
            return Page();
        }

        try
        {
            Report = await reports.GetYearReportAsync(userId, year.Value, cancellationToken);
        }
        catch (InvalidDataException exception)
        {
            logger.LogWarning(exception, "Web viewer could not read workbook year {Year} for user {UserId}", year, userId);
            ErrorMessage = "Buku tahun ini tidak dapat dibaca. Data tidak diubah; periksa workbook dari aplikasi atau Excel.";
            Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            return Page();
        }

        if (Report is null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            ErrorMessage = $"Belum ada buku untuk tahun {year}.";
        }

        return Page();
    }

    /// <summary>
    /// JSON for the client renderer. The default encoder escapes &lt;, &gt; and &amp;, so the payload
    /// is safe inside a non-executable script element.
    /// </summary>
    public string ReportJson() => JsonSerializer.Serialize(Report, JsonOptions);
}

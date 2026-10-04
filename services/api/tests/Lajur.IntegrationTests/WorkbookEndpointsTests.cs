using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Lajur.Application.Auth;
using Lajur.Application.Finance;
using Lajur.Application.Workbooks;
using Lajur.Infrastructure.Workbooks;
using Microsoft.Extensions.DependencyInjection;

namespace Lajur.IntegrationTests;

public sealed class WorkbookEndpointsTests
{
    private const string Password = "correct horse battery staple";
    private static string FixturePath => Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "LajurWorkbookFixture.xlsm");

    [Fact]
    public async Task Create_new_workbook_completes_onboarding_and_exports_target_year()
    {
        using var factory = new LajurApiFactory();
        _ = await CreateAccountAsync(factory, "workbook-create", Password);
        SeedTemplate(factory);
        using var client = await CreateAuthenticatedClientAsync(factory, "workbook-create", Password);

        var before = await client.GetFromJsonAsync<MeResponse>("/api/v1/me");
        Assert.NotNull(before);
        Assert.True(before.NeedsWorkbookOnboarding);

        using var create = await client.PostAsync("/api/v1/workbooks/2027/create", content: null);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var result = await create.Content.ReadFromJsonAsync<WorkbookCreateResult>();
        Assert.NotNull(result);
        Assert.Equal(2027, result.Year);

        var workbooks = await client.GetFromJsonAsync<WorkbookInfo[]>("/api/v1/workbooks");
        var workbook = Assert.Single(Assert.IsType<WorkbookInfo[]>(workbooks));
        Assert.Equal(2027, workbook.Year);

        var after = await client.GetFromJsonAsync<MeResponse>("/api/v1/me");
        Assert.NotNull(after);
        Assert.False(after.NeedsWorkbookOnboarding);
        Assert.Equal([2027], after.WorkbookYears);

        using var export = await client.GetAsync("/api/v1/workbooks/2027/export");
        export.EnsureSuccessStatusCode();
        Assert.Equal("application/vnd.ms-excel.sheet.macroEnabled.12", export.Content.Headers.ContentType?.MediaType);
        Assert.True((await export.Content.ReadAsByteArrayAsync()).Length > 0);

        using var duplicate = await client.PostAsync("/api/v1/workbooks/2027/create", content: null);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("workbook_already_exists", await ReadProblemCodeAsync(duplicate));
    }

    [Fact]
    public async Task Import_migrates_legacy_rows_and_export_all_contains_real_workbook()
    {
        using var factory = new LajurApiFactory();
        _ = await CreateAccountAsync(factory, "workbook-import", Password);
        using var client = await CreateAuthenticatedClientAsync(factory, "workbook-import", Password);

        using var import = await ImportAsync(
            client,
            FixturePath,
            confirmedYear: null,
            replaceExisting: false,
            confirmNoPendingChanges: true);
        import.EnsureSuccessStatusCode();
        var result = await import.Content.ReadFromJsonAsync<WorkbookImportResult>();
        Assert.NotNull(result);
        Assert.Equal(2026, result.Year);
        Assert.True(result.IndexedTransactionCount > 0);
        Assert.False(result.ReplacedExisting);

        var january = await client.GetFromJsonAsync<FinanceTransaction[]>(
            "/api/v1/transactions?year=2026&month=1");
        Assert.NotEmpty(Assert.IsType<FinanceTransaction[]>(january));

        using var exportAll = await client.GetAsync("/api/v1/workbooks/export-all");
        exportAll.EnsureSuccessStatusCode();
        Assert.Equal("application/zip", exportAll.Content.Headers.ContentType?.MediaType);
        await using var zipBytes = new MemoryStream(await exportAll.Content.ReadAsByteArrayAsync());
        using var archive = new ZipArchive(zipBytes, ZipArchiveMode.Read);
        var entry = Assert.Single(archive.Entries);
        Assert.Equal("Keuangan_Pribadi_2026.xlsm", entry.FullName);
        Assert.True(entry.Length > 0);
    }

    [Fact]
    public async Task Relative_date_helpers_work_through_create_import_export_and_invalid_replacement_guard()
    {
        using var factory = new LajurApiFactory();
        _ = await CreateAccountAsync(factory, "workbook-relative", Password);
        SeedTemplate(factory);
        var templatePath = Path.Combine(factory.StorageRoot, "templates", "finance-template.xlsm");
        WriteRelativeDateHelpers(templatePath);
        var templateBefore = SHA256.HashData(await File.ReadAllBytesAsync(templatePath));
        using var client = await CreateAuthenticatedClientAsync(factory, "workbook-relative", Password);

        using var create = await client.PostAsync("/api/v1/workbooks/2028/create", content: null);
        create.EnsureSuccessStatusCode();
        using var exportGenerated = await client.GetAsync("/api/v1/workbooks/2028/export");
        exportGenerated.EnsureSuccessStatusCode();
        var generatedPath = Path.Combine(factory.StorageRoot, "generated-export.xlsm");
        await File.WriteAllBytesAsync(generatedPath, await exportGenerated.Content.ReadAsByteArrayAsync());
        Assert.Equal(2028, new OpenXmlWorkbookReader().DetectWorkbookYear(generatedPath));
        Assert.Equal(templateBefore, SHA256.HashData(await File.ReadAllBytesAsync(templatePath)));
        Assert.Equal(ReadVbaHash(templatePath), ReadVbaHash(generatedPath));

        using var import = await ImportAsync(client, templatePath, confirmedYear: null,
            replaceExisting: false, confirmNoPendingChanges: true);
        import.EnsureSuccessStatusCode();
        var result = await import.Content.ReadFromJsonAsync<WorkbookImportResult>();
        Assert.NotNull(result);
        Assert.Equal(2026, result.Year);
        Assert.Equal(8, result.IndexedTransactionCount);
        using var exportImported = await client.GetAsync("/api/v1/workbooks/2026/export");
        exportImported.EnsureSuccessStatusCode();
        var canonicalBefore = await exportImported.Content.ReadAsByteArrayAsync();
        var importedPath = Path.Combine(factory.StorageRoot, "imported-export.xlsm");
        await File.WriteAllBytesAsync(importedPath, canonicalBefore);
        Assert.Equal(2026, new OpenXmlWorkbookReader().DetectWorkbookYear(importedPath));
        Assert.Equal(ReadVbaHash(templatePath), ReadVbaHash(importedPath));

        using (var document = SpreadsheetDocument.Open(templatePath, true))
        {
            var workbook = document.WorkbookPart!;
            var sheet = workbook.Workbook!.Sheets!.Elements<Sheet>().Single(sheet => sheet.Name?.Value == "Okt");
            var worksheet = ((WorksheetPart)workbook.GetPartById(sheet.Id!.Value!)).Worksheet!;
            worksheet.Descendants<Cell>().Single(cell => cell.CellReference?.Value == "AG32")
                .CellFormula!.Text = "$AD$1+AF31-1";
            worksheet.Save();
        }
        using var invalid = await ImportAsync(client, templatePath, 2026,
            replaceExisting: true, confirmNoPendingChanges: true);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);
        Assert.Equal("workbook_validation_failed", await ReadProblemCodeAsync(invalid));
        using var exportAfter = await client.GetAsync("/api/v1/workbooks/2026/export");
        exportAfter.EnsureSuccessStatusCode();
        Assert.Equal(canonicalBefore, await exportAfter.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Import_requires_pending_change_and_replacement_confirmation_and_preserves_active_on_invalid_upload()
    {
        using var factory = new LajurApiFactory();
        var userId = await CreateAccountAsync(factory, "workbook-guard", Password);
        var activePath = SeedWorkbook(factory, userId, 2026);
        var activeBefore = await File.ReadAllBytesAsync(activePath);
        using var client = await CreateAuthenticatedClientAsync(factory, "workbook-guard", Password);

        using var pendingNotConfirmed = await ImportAsync(
            client,
            FixturePath,
            2026,
            replaceExisting: true,
            confirmNoPendingChanges: false);
        Assert.Equal(HttpStatusCode.Conflict, pendingNotConfirmed.StatusCode);
        Assert.Equal("pending_changes_confirmation_required", await ReadProblemCodeAsync(pendingNotConfirmed));

        using var replacementNotConfirmed = await ImportAsync(
            client,
            FixturePath,
            2026,
            replaceExisting: false,
            confirmNoPendingChanges: true);
        Assert.Equal(HttpStatusCode.Conflict, replacementNotConfirmed.StatusCode);
        Assert.Equal("workbook_replace_not_confirmed", await ReadProblemCodeAsync(replacementNotConfirmed));

        var invalidPath = Path.Combine(factory.StorageRoot, "invalid.xlsm");
        await File.WriteAllTextAsync(invalidPath, "not an Excel package");
        using var invalid = await ImportAsync(
            client,
            invalidPath,
            2026,
            replaceExisting: true,
            confirmNoPendingChanges: true);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);
        Assert.Equal("workbook_import_invalid", await ReadProblemCodeAsync(invalid));
        Assert.Equal(activeBefore, await File.ReadAllBytesAsync(activePath));

        using var validReplacement = await ImportAsync(
            client,
            FixturePath,
            2026,
            replaceExisting: true,
            confirmNoPendingChanges: true);
        validReplacement.EnsureSuccessStatusCode();
        var replacement = await validReplacement.Content.ReadFromJsonAsync<WorkbookImportResult>();
        Assert.NotNull(replacement);
        Assert.True(replacement.ReplacedExisting);
        Assert.NotNull(replacement.BackupFileName);
        var backupPath = Path.Combine(
            factory.StorageRoot,
            "backups",
            userId.ToString("N"),
            "2026",
            replacement.BackupFileName);
        Assert.True(File.Exists(backupPath));
        Assert.Equal(activeBefore, await File.ReadAllBytesAsync(backupPath));
    }

    private static async Task<HttpResponseMessage> ImportAsync(
        HttpClient client,
        string path,
        int? confirmedYear,
        bool replaceExisting,
        bool confirmNoPendingChanges)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(await File.ReadAllBytesAsync(path));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.ms-excel.sheet.macroEnabled.12");
        form.Add(file, "file", "upload.xlsm");
        if (confirmedYear is not null)
        {
            form.Add(new StringContent(confirmedYear.Value.ToString()), "year");
        }

        form.Add(new StringContent(replaceExisting.ToString()), "replaceExisting");
        form.Add(new StringContent(confirmNoPendingChanges.ToString()), "confirmNoPendingChanges");
        return await client.PostAsync("/api/v1/workbooks/import", form);
    }

    private static async Task<Guid> CreateAccountAsync(
        LajurApiFactory factory,
        string username,
        string password)
    {
        using var scope = factory.Services.CreateScope();
        var administration = scope.ServiceProvider.GetRequiredService<IAccountAdministration>();
        var result = await administration.CreateAsync(username, password, CancellationToken.None);
        Assert.True(result.Succeeded, result.Failure?.ToString());
        return Assert.IsType<Guid>(result.Value);
    }

    private static async Task<HttpClient> CreateAuthenticatedClientAsync(
        LajurApiFactory factory,
        string username,
        string password)
    {
        var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            username,
            password,
            deviceLabel = "Workbook integration device"
        });
        response.EnsureSuccessStatusCode();
        var tokens = await response.Content.ReadFromJsonAsync<AuthTokens>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
        return client;
    }

    private static void SeedTemplate(LajurApiFactory factory)
    {
        var path = Path.Combine(factory.StorageRoot, "templates", "finance-template.xlsm");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.Copy(FixturePath, path);
    }

    private static void WriteRelativeDateHelpers(string path)
    {
        using var document = SpreadsheetDocument.Open(path, true);
        var workbook = document.WorkbookPart!;
        foreach (var sheetName in new[] { "Okt", "Nov", "Des" })
        {
            var sheet = workbook.Workbook!.Sheets!.Elements<Sheet>().Single(sheet => sheet.Name?.Value == sheetName);
            var worksheet = ((WorksheetPart)workbook.GetPartById(sheet.Id!.Value!)).Worksheet!;
            for (var row = 2; row <= 32; row++)
            {
                worksheet.Descendants<Cell>().Single(cell => cell.CellReference?.Value == $"AG{row}")
                    .CellFormula = new CellFormula($"$AD$1+AF{row}-1");
            }
            worksheet.Save();
        }
    }

    private static byte[] ReadVbaHash(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        using var vba = archive.GetEntry("xl/vbaProject.bin")!.Open();
        return SHA256.HashData(vba);
    }

    private static string SeedWorkbook(LajurApiFactory factory, Guid userId, int year)
    {
        using var scope = factory.Services.CreateScope();
        var paths = scope.ServiceProvider.GetRequiredService<IUserStoragePathResolver>();
        var path = Path.Combine(paths.GetUserRoot(userId), "workbooks", $"Keuangan_Pribadi_{year}.xlsm");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.Copy(FixturePath, path);
        return path;
    }

    private static async Task<string> ReadProblemCodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("code").GetString()!;
    }

    private sealed record MeResponse(
        Guid UserId,
        string Username,
        Guid DeviceId,
        string DeviceLabel,
        Guid SessionFamilyId,
        bool NeedsWorkbookOnboarding,
        IReadOnlyList<int> WorkbookYears);
}

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
using Microsoft.Extensions.DependencyInjection;

namespace Lajur.IntegrationTests;

public sealed class FinanceEndpointsTests
{
    private const string Password = "correct horse battery staple";
    private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "LajurWorkbookFixture.xlsm");

    [Fact]
    public async Task Bearer_report_reads_only_own_workbook_and_preserves_package()
    {
        using var factory = new LajurApiFactory();
        var owner = await CreateAccountAsync(factory, "report-owner", Password);
        await CreateAccountAsync(factory, "report-empty", Password);
        var path = SeedWorkbook(factory, owner, 2026);
        var before = SHA256.HashData(await File.ReadAllBytesAsync(path));
        using var anonymous = factory.CreateClient();
        using var denied = await anonymous.GetAsync("/api/v1/reports?year=2026");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using var empty = await CreateAuthenticatedClientAsync(factory, "report-empty", Password);
        using var missing = await empty.GetAsync("/api/v1/reports?year=2026");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var client = await CreateAuthenticatedClientAsync(factory, "report-owner", Password);
        using var response = await client.GetAsync("/api/v1/reports?year=2026");
        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.CacheControl?.NoStore);
        var report = await response.Content.ReadFromJsonAsync<Lajur.Application.Reports.YearReport>();
        Assert.NotNull(report);
        Assert.Equal(12, report.Months.Count);
        var summary = await client.GetFromJsonAsync<FinanceSummary>("/api/v1/summary?year=2026&month=1");
        Assert.Equal(summary!.ExpenseIdr, report.Months[0].ExpenseIdr);
        Assert.Equal(before, SHA256.HashData(await File.ReadAllBytesAsync(path)));
        using var invalid = await client.GetAsync("/api/v1/reports?year=0");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task Master_summary_and_transaction_query_read_the_authenticated_users_workbook()
    {
        using var factory = new LajurApiFactory();
        var userId = await CreateAccountAsync(factory, "finance-read", Password);
        var workbookPath = SeedWorkbook(factory, userId, 2026);
        using var client = await CreateAuthenticatedClientAsync(factory, "finance-read", Password);

        var master = await client.GetFromJsonAsync<FinanceMasterData>("/api/v1/master?year=2026");
        Assert.NotNull(master);
        Assert.Equal(2026, master.WorkbookYear);
        Assert.Equal("lajur-workbook-v1", master.SchemaVersion);
        Assert.Contains("Pengeluaran", master.TransactionTypes);

        var before = await client.GetFromJsonAsync<FinanceSummary>("/api/v1/summary?year=2026&month=1");
        Assert.NotNull(before);
        Assert.True(before.TransactionCount > 0);

        var created = await CreateTransactionAsync(
            client,
            Guid.NewGuid(),
            description: "Kopi integrasi unik");
        Assert.Equal(1, created.Revision);

        var listed = await client.GetFromJsonAsync<FinanceTransaction[]>(
            "/api/v1/transactions?year=2026&month=1&query=integrasi");
        var transaction = Assert.Single(Assert.IsType<FinanceTransaction[]>(listed));
        Assert.Equal(created.TransactionId, transaction.TransactionId);

        var after = await client.GetFromJsonAsync<FinanceSummary>("/api/v1/summary?year=2026&month=1");
        Assert.NotNull(after);
        Assert.Equal(before.TransactionCount + 1, after.TransactionCount);
        Assert.Equal(before.ExpenseIdr + created.AmountIdr, after.ExpenseIdr);
        Assert.True(File.Exists(workbookPath));
    }

    [Fact]
    public async Task Ten_identical_creates_produce_one_row_and_conflicting_payload_returns_409()
    {
        using var factory = new LajurApiFactory();
        var userId = await CreateAccountAsync(factory, "idempotent", Password);
        var workbookPath = SeedWorkbook(factory, userId, 2026);
        var vbaBefore = ReadZipEntryHash(workbookPath, "xl/vbaProject.bin");
        using var client = await CreateAuthenticatedClientAsync(factory, "idempotent", Password);
        var transactionId = Guid.NewGuid();
        var payload = TransactionPayload(transactionId, "Idempotent create");

        var requests = Enumerable.Range(0, 10)
            .Select(_ => client.PostAsJsonAsync("/api/v1/transactions", payload))
            .ToArray();
        var responses = await Task.WhenAll(requests);
        try
        {
            foreach (var response in responses)
            {
                Assert.True(response.StatusCode == HttpStatusCode.Created,
                    $"Expected Created, got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            }
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        var listed = await client.GetFromJsonAsync<FinanceTransaction[]>(
            "/api/v1/transactions?year=2026&month=1");
        Assert.Single(Assert.IsType<FinanceTransaction[]>(listed), item => item.TransactionId == transactionId);

        var conflicting = TransactionPayload(transactionId, "Different content") with { AmountIdr = 999_000 };
        using var conflictResponse = await client.PostAsJsonAsync("/api/v1/transactions", conflicting);
        Assert.Equal(HttpStatusCode.Conflict, conflictResponse.StatusCode);
        Assert.Equal("transaction_id_conflict", await ReadProblemCodeAsync(conflictResponse));
        Assert.Equal(vbaBefore, ReadZipEntryHash(workbookPath, "xl/vbaProject.bin"));
        var auditText = ReadSheetText(workbookPath, "__AuditLog");
        Assert.Contains("CREATE", auditText);
        Assert.Contains(userId.ToString("D"), auditText);
        Assert.Equal(1, CountOccurrences(auditText, transactionId.ToString("D")));
    }

    [Fact]
    public async Task Update_and_delete_enforce_expected_revision()
    {
        using var factory = new LajurApiFactory();
        var userId = await CreateAccountAsync(factory, "revisions", Password);
        SeedWorkbook(factory, userId, 2026);
        using var client = await CreateAuthenticatedClientAsync(factory, "revisions", Password);
        var transactionId = Guid.NewGuid();
        _ = await CreateTransactionAsync(client, transactionId, "Revision one");

        var updatePayload = new
        {
            expectedRevision = 1,
            date = "2026-02-12",
            type = "Pengeluaran",
            paymentMethod = "Bank Uji",
            category = "Kategori Uji 08",
            amountIdr = 222_000,
            description = "Revision two"
        };
        using var updateResponse = await client.PutAsJsonAsync(
            $"/api/v1/transactions/{transactionId:D}?year=2026",
            updatePayload);
        updateResponse.EnsureSuccessStatusCode();
        var updated = await updateResponse.Content.ReadFromJsonAsync<FinanceTransaction>();
        Assert.NotNull(updated);
        Assert.Equal(2, updated.Revision);
        Assert.Equal(2, updated.Date.Month);

        using var staleUpdate = await client.PutAsJsonAsync(
            $"/api/v1/transactions/{transactionId:D}?year=2026",
            updatePayload);
        Assert.Equal(HttpStatusCode.Conflict, staleUpdate.StatusCode);
        Assert.Equal("transaction_revision_conflict", await ReadProblemCodeAsync(staleUpdate));

        using var staleDelete = await client.DeleteAsync(
            $"/api/v1/transactions/{transactionId:D}?year=2026&expectedRevision=1");
        Assert.Equal(HttpStatusCode.Conflict, staleDelete.StatusCode);

        using var delete = await client.DeleteAsync(
            $"/api/v1/transactions/{transactionId:D}?year=2026&expectedRevision=2");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var listed = await client.GetFromJsonAsync<FinanceTransaction[]>(
            "/api/v1/transactions?year=2026&month=2");
        Assert.DoesNotContain(Assert.IsType<FinanceTransaction[]>(listed), item => item.TransactionId == transactionId);
    }

    [Fact]
    public async Task Authenticated_account_cannot_read_or_mutate_another_accounts_workbook()
    {
        using var factory = new LajurApiFactory();
        var aliceId = await CreateAccountAsync(factory, "finance-alice", Password);
        var bobId = await CreateAccountAsync(factory, "finance-bob", Password);
        SeedWorkbook(factory, aliceId, 2026);
        SeedWorkbook(factory, bobId, 2026);
        using var alice = await CreateAuthenticatedClientAsync(factory, "finance-alice", Password);
        using var bob = await CreateAuthenticatedClientAsync(factory, "finance-bob", Password);
        var transactionId = Guid.NewGuid();
        _ = await CreateTransactionAsync(alice, transactionId, "Only Alice can see this");

        var aliceRows = await alice.GetFromJsonAsync<FinanceTransaction[]>(
            "/api/v1/transactions?year=2026&month=1");
        var bobRows = await bob.GetFromJsonAsync<FinanceTransaction[]>(
            "/api/v1/transactions?year=2026&month=1");
        Assert.Contains(Assert.IsType<FinanceTransaction[]>(aliceRows), item => item.TransactionId == transactionId);
        Assert.DoesNotContain(Assert.IsType<FinanceTransaction[]>(bobRows), item => item.TransactionId == transactionId);

        using var bobUpdate = await bob.PutAsJsonAsync(
            $"/api/v1/transactions/{transactionId:D}?year=2026",
            new
            {
                expectedRevision = 1,
                date = "2026-01-12",
                type = "Pengeluaran",
                paymentMethod = "Tunai Uji",
                category = "Kategori Uji 10",
                amountIdr = 100_000,
                description = "Attempted cross-tenant update"
            });
        Assert.Equal(HttpStatusCode.NotFound, bobUpdate.StatusCode);
        Assert.Equal("transaction_not_found", await ReadProblemCodeAsync(bobUpdate));
    }

    [Fact]
    public async Task Missing_workbook_and_invalid_master_value_return_stable_problem_codes()
    {
        using var factory = new LajurApiFactory();
        var userId = await CreateAccountAsync(factory, "problems", Password);
        using var client = await CreateAuthenticatedClientAsync(factory, "problems", Password);

        using var missing = await client.GetAsync("/api/v1/master?year=2026");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("workbook_not_found", await ReadProblemCodeAsync(missing));

        SeedWorkbook(factory, userId, 2026);
        var invalid = TransactionPayload(Guid.NewGuid(), "Invalid category") with
        {
            Category = "Kategori yang tidak ada"
        };
        using var invalidResponse = await client.PostAsJsonAsync("/api/v1/transactions", invalid);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalidResponse.StatusCode);
        Assert.Equal("transaction_validation_failed", await ReadProblemCodeAsync(invalidResponse));

        var malformed = TransactionPayload(Guid.NewGuid(), "Invalid amount") with { AmountIdr = 0 };
        using var malformedResponse = await client.PostAsJsonAsync("/api/v1/transactions", malformed);
        Assert.Equal(HttpStatusCode.BadRequest, malformedResponse.StatusCode);
        Assert.Equal("request_validation_failed", await ReadProblemCodeAsync(malformedResponse));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Optional_payment_is_canonical_and_filling_or_clearing_does_not_duplicate_expense(string? payment)
    {
        using var factory = new LajurApiFactory();
        var userId = await CreateAccountAsync(factory, "optional-payment", Password);
        SeedWorkbook(factory, userId, 2026);
        using var client = await CreateAuthenticatedClientAsync(factory, "optional-payment", Password);
        var before = await client.GetFromJsonAsync<FinanceSummary>("/api/v1/summary?year=2026&month=1");
        var id = Guid.NewGuid();
        var payload = TransactionPayload(id, "Payment can be filled later") with { PaymentMethod = payment };
        using var create = await client.PostAsJsonAsync("/api/v1/transactions", payload);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<FinanceTransaction>();
        Assert.Equal("", created!.PaymentMethod);
        using var retry = await client.PostAsJsonAsync("/api/v1/transactions", payload with { PaymentMethod = "" });
        retry.EnsureSuccessStatusCode();
        Assert.Equal(1, (await retry.Content.ReadFromJsonAsync<FinanceTransaction>())!.Revision);
        var revision = 1;
        foreach (var method in new string?[] { "Bank Uji", null })
        {
            using var update = await client.PutAsJsonAsync($"/api/v1/transactions/{id:D}?year=2026", new
            {
                expectedRevision = revision, payload.Date, payload.Type,
                paymentMethod = method, payload.Category, payload.AmountIdr, payload.Description
            });
            update.EnsureSuccessStatusCode();
            var updated = await update.Content.ReadFromJsonAsync<FinanceTransaction>();
            Assert.Equal(id, updated!.TransactionId);
            Assert.Equal(++revision, updated.Revision);
            Assert.Equal(method ?? "", updated.PaymentMethod);
            var summary = await client.GetFromJsonAsync<FinanceSummary>("/api/v1/summary?year=2026&month=1");
            Assert.Equal(before!.ExpenseIdr + payload.AmountIdr, summary!.ExpenseIdr);
            Assert.Equal(before.TransactionCount + 1, summary.TransactionCount);
        }
        using var unknown = await client.PostAsJsonAsync("/api/v1/transactions",
            payload with { TransactionId = Guid.NewGuid(), PaymentMethod = "Not in Master" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, unknown.StatusCode);
    }

    [Fact]
    public async Task Omitted_payment_is_accepted_but_type_and_category_remain_required()
    {
        using var factory = new LajurApiFactory();
        var userId = await CreateAccountAsync(factory, "omitted-payment", Password);
        SeedWorkbook(factory, userId, 2026);
        using var client = await CreateAuthenticatedClientAsync(factory, "omitted-payment", Password);
        using var create = await client.PostAsJsonAsync("/api/v1/transactions", new
        {
            transactionId = Guid.NewGuid(), date = "2026-01-12", type = "Pengeluaran",
            category = "Kategori Uji 10", amountIdr = 123_000
        });
        create.EnsureSuccessStatusCode();
        Assert.Equal("", (await create.Content.ReadFromJsonAsync<FinanceTransaction>())!.PaymentMethod);
        foreach (var invalid in new[]
        {
            TransactionPayload(Guid.NewGuid(), "Missing type") with { Type = "", PaymentMethod = "" },
            TransactionPayload(Guid.NewGuid(), "Missing category") with { Category = "", PaymentMethod = "" }
        })
        {
            using var rejected = await client.PostAsJsonAsync("/api/v1/transactions", invalid);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }
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

    private static string SeedWorkbook(LajurApiFactory factory, Guid userId, int year)
    {
        using var scope = factory.Services.CreateScope();
        var paths = scope.ServiceProvider.GetRequiredService<IUserStoragePathResolver>();
        var workbookPath = Path.Combine(
            paths.GetUserRoot(userId),
            "workbooks",
            $"Keuangan_Pribadi_{year}.xlsm");
        Directory.CreateDirectory(Path.GetDirectoryName(workbookPath)!);
        File.Copy(FixturePath, workbookPath, overwrite: false);
        return workbookPath;
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
            deviceLabel = "Finance integration device"
        });
        response.EnsureSuccessStatusCode();
        var tokens = await response.Content.ReadFromJsonAsync<AuthTokens>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
        return client;
    }

    private static async Task<FinanceTransaction> CreateTransactionAsync(
        HttpClient client,
        Guid transactionId,
        string description)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/v1/transactions",
            TransactionPayload(transactionId, description));
        response.EnsureSuccessStatusCode();
        return Assert.IsType<FinanceTransaction>(await response.Content.ReadFromJsonAsync<FinanceTransaction>());
    }

    private static TransactionRequest TransactionPayload(Guid transactionId, string description) => new(
        transactionId,
        "2026-01-12",
        "Pengeluaran",
        "Tunai Uji",
        "Kategori Uji 10",
        123_000,
        description);

    private static async Task<string> ReadProblemCodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("code").GetString()!;
    }

    private static string ReadZipEntryHash(string workbookPath, string entryName)
    {
        using var stream = File.OpenRead(workbookPath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        using var entryStream = archive.GetEntry(entryName)!.Open();
        return Convert.ToHexString(SHA256.HashData(entryStream));
    }

    private static string ReadSheetText(string workbookPath, string sheetName)
    {
        using var document = SpreadsheetDocument.Open(workbookPath, false);
        var workbookPart = document.WorkbookPart!;
        var sheet = workbookPart.Workbook!.Sheets!.Elements<Sheet>()
            .Single(candidate => candidate.Name?.Value == sheetName);
        var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheet.Id!.Value!);
        return worksheetPart.Worksheet!.InnerText;
    }

    private static int CountOccurrences(string value, string search)
    {
        var count = 0;
        var offset = 0;
        while ((offset = value.IndexOf(search, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += search.Length;
        }

        return count;
    }

    private sealed record TransactionRequest(
        Guid TransactionId,
        string Date,
        string Type,
        string? PaymentMethod,
        string Category,
        long AmountIdr,
        string Description);
}

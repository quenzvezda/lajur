using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Lajur.Api.Web;
using Lajur.Application.Finance;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Lajur.IntegrationTests;

public sealed partial class WebViewerTests
{
    private static BrowserTransactionInput WebInput(string amount = "15000") => new(
        new DateOnly(2026, 10, 8), "Pengeluaran", "", "Kategori Uji 06", amount, "Input web sintetis");

    private static async Task EnableBrowserWrites(HttpClient browser)
    {
        var html = await browser.GetStringAsync("/buku/2026");
        browser.DefaultRequestHeaders.Add("X-Lajur-CSRF", ExtractAntiforgeryToken(html));
    }

    [Fact]
    public async Task Browser_CRUD_is_idempotent_revisioned_audited_and_refreshes_reports()
    {
        using var factory = new LajurApiFactory();
        var userId = await CreateAccountAsync(factory, "web-crud");
        var path = SeedWorkbook(factory, userId, 2026);
        var vba = PackagePart(path, "xl/vbaProject.bin");
        var master = SheetXml(path, "Master");
        var rightSide = RightSide(path, "Okt");
        using var browser = CreateBrowser(factory);
        await BrowserLoginAsync(browser, "web-crud");
        await EnableBrowserWrites(browser);
        var id = Guid.NewGuid();
        var payload = new BrowserCreateTransaction(id, WebInput());
        // Warm the year cache before writing, then retry the same command concurrently.
        using var oldReport = await browser.GetAsync("/web/books/2026/report");
        oldReport.EnsureSuccessStatusCode();
        var replies = await Task.WhenAll(Enumerable.Range(0, 3)
            .Select(_ => browser.PostAsJsonAsync("/web/books/2026/transactions", payload)));
        foreach (var reply in replies) { reply.EnsureSuccessStatusCode(); reply.Dispose(); }
        var stored = await browser.GetFromJsonAsync<BrowserTransaction>($"/web/books/2026/transactions/{id}");
        Assert.Equal(1, stored!.Revision);
        Assert.Equal("", stored.PaymentMethod);
        using (var report = JsonDocument.Parse(await browser.GetStringAsync("/web/books/2026/report")))
        {
            var row = Assert.Single(report.RootElement.GetProperty("months")[9].GetProperty("rows").EnumerateArray());
            Assert.Equal(id, row.GetProperty("transactionId").GetGuid());
            Assert.Equal("15000", row.GetProperty("amountIdrText").GetString());
            Assert.Equal(15000, report.RootElement.GetProperty("months")[9].GetProperty("expenseIdr").GetInt64());
        }
        var changed = new BrowserUpdateTransaction(1, WebInput("16000") with { Date = new DateOnly(2026, 11, 9), PaymentMethod = "Bank Uji" });
        using var update = await browser.PutAsJsonAsync($"/web/books/2026/transactions/{id}", changed);
        update.EnsureSuccessStatusCode();
        Assert.Equal(2, (await update.Content.ReadFromJsonAsync<BrowserTransaction>())!.Revision);
        using var staleUpdate = await browser.PutAsJsonAsync($"/web/books/2026/transactions/{id}", changed);
        Assert.Equal(HttpStatusCode.Conflict, staleUpdate.StatusCode);
        using var staleDelete = await browser.DeleteAsync($"/web/books/2026/transactions/{id}?expectedRevision=1");
        Assert.Equal(HttpStatusCode.Conflict, staleDelete.StatusCode);
        using var deleted = await browser.DeleteAsync($"/web/books/2026/transactions/{id}?expectedRevision=2");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var absent = await browser.GetAsync($"/web/books/2026/transactions/{id}");
        Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
        using var after = JsonDocument.Parse(await browser.GetStringAsync("/web/books/2026/report"));
        Assert.Empty(after.RootElement.GetProperty("months")[9].GetProperty("rows").EnumerateArray());
        Assert.Empty(after.RootElement.GetProperty("months")[10].GetProperty("rows").EnumerateArray());
        using var document = SpreadsheetDocument.Open(path, false);
        var audit = Worksheet(document, "__AuditLog").GetFirstChild<SheetData>()!.Elements<Row>().Skip(1).ToArray();
        Assert.Equal(3, audit.Length);
        Assert.All(audit, row =>
        {
            Assert.Equal("WEB", row.Elements<Cell>().Single(cell => cell.CellReference!.Value!.StartsWith('D')).InnerText);
            Assert.Equal(userId.ToString("D"), row.Elements<Cell>().Single(cell => cell.CellReference!.Value!.StartsWith('J')).InnerText);
            Assert.True(string.IsNullOrEmpty(row.Elements<Cell>().SingleOrDefault(cell => cell.CellReference!.Value!.StartsWith('K'))?.InnerText));
        });
        Assert.Equal(vba, PackagePart(path, "xl/vbaProject.bin"));
        Assert.Equal(master, SheetXml(path, "Master"));
        Assert.Equal(rightSide, RightSide(path, "Okt"));
    }

    [Fact]
    public async Task Browser_mutations_require_antiforgery_and_browser_authentication()
    {
        using var factory = new LajurApiFactory();
        var userId = await CreateAccountAsync(factory, "csrf-transactions");
        var path = SeedWorkbook(factory, userId, 2026);
        var before = SHA256.HashData(await File.ReadAllBytesAsync(path));
        using var browser = CreateBrowser(factory);
        await BrowserLoginAsync(browser, "csrf-transactions");
        var id = Guid.NewGuid();
        using var create = await browser.PostAsJsonAsync("/web/books/2026/transactions", new BrowserCreateTransaction(id, WebInput()));
        using var update = await browser.PutAsJsonAsync($"/web/books/2026/transactions/{id}", new BrowserUpdateTransaction(1, WebInput()));
        using var delete = await browser.DeleteAsync($"/web/books/2026/transactions/{id}?expectedRevision=1");
        Assert.All(new[] { create, update, delete }, response => Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode));
        using var phone = factory.CreateClient();
        var tokens = await AndroidLoginAsync(phone, "csrf-transactions");
        phone.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        using var bearer = await phone.PostAsJsonAsync("/web/books/2026/transactions", new BrowserCreateTransaction(id, WebInput()));
        Assert.Equal(HttpStatusCode.Unauthorized, bearer.StatusCode);
        using var anonymous = CreateBrowser(factory);
        using var unauthenticated = await anonymous.GetAsync("/web/books/2026/report");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        Assert.Equal(before, SHA256.HashData(await File.ReadAllBytesAsync(path)));
    }

    [Fact]
    public async Task Browser_and_Android_writes_conflict_without_revoking_the_phone()
    {
        using var factory = new LajurApiFactory();
        var userId = await CreateAccountAsync(factory, "concurrent-web");
        SeedWorkbook(factory, userId, 2026);
        using var phone = factory.CreateClient();
        var tokens = await AndroidLoginAsync(phone, "concurrent-web");
        phone.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        using var browser = CreateBrowser(factory);
        await BrowserLoginAsync(browser, "concurrent-web");
        await EnableBrowserWrites(browser);
        var id = Guid.NewGuid();
        using var create = await browser.PostAsJsonAsync("/web/books/2026/transactions", new BrowserCreateTransaction(id, WebInput()));
        create.EnsureSuccessStatusCode();
        using var phoneUpdate = await phone.PutAsJsonAsync($"/api/v1/transactions/{id}?year=2026", new
        {
            expectedRevision = 1, date = "2026-10-08", type = "Pengeluaran", paymentMethod = "Bank Uji",
            category = "Kategori Uji 06", amountIdr = 20000, description = "Perubahan Android"
        });
        phoneUpdate.EnsureSuccessStatusCode();
        using var stale = await browser.PutAsJsonAsync($"/web/books/2026/transactions/{id}", new BrowserUpdateTransaction(1, WebInput()));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var latest = await browser.GetFromJsonAsync<BrowserTransaction>($"/web/books/2026/transactions/{id}");
        Assert.Equal("20000", latest!.AmountIdr);
        using var webUpdate = await browser.PutAsJsonAsync($"/web/books/2026/transactions/{id}", new BrowserUpdateTransaction(2, WebInput("21000")));
        webUpdate.EnsureSuccessStatusCode();
        using var stalePhoneDelete = await phone.DeleteAsync($"/api/v1/transactions/{id}?year=2026&expectedRevision=2");
        Assert.Equal(HttpStatusCode.Conflict, stalePhoneDelete.StatusCode);
        using var summary = await phone.GetAsync("/api/v1/summary?year=2026&month=10");
        Assert.Equal(HttpStatusCode.OK, summary.StatusCode);
    }

    [Fact]
    public async Task Browser_cannot_read_update_or_delete_another_users_transaction()
    {
        using var factory = new LajurApiFactory();
        var ownerId = await CreateAccountAsync(factory, "web-owner");
        var ownerPath = SeedWorkbook(factory, ownerId, 2026);
        var otherId = await CreateAccountAsync(factory, "web-other");
        SeedWorkbook(factory, otherId, 2026);
        using var owner = CreateBrowser(factory); await BrowserLoginAsync(owner, "web-owner"); await EnableBrowserWrites(owner);
        var id = Guid.NewGuid();
        using var create = await owner.PostAsJsonAsync("/web/books/2026/transactions", new BrowserCreateTransaction(id, WebInput()));
        create.EnsureSuccessStatusCode();
        var before = SHA256.HashData(await File.ReadAllBytesAsync(ownerPath));
        using var other = CreateBrowser(factory); await BrowserLoginAsync(other, "web-other"); await EnableBrowserWrites(other);
        using var read = await other.GetAsync($"/web/books/2026/transactions/{id}");
        using var update = await other.PutAsJsonAsync($"/web/books/2026/transactions/{id}", new BrowserUpdateTransaction(1, WebInput()));
        using var delete = await other.DeleteAsync($"/web/books/2026/transactions/{id}?expectedRevision=1");
        Assert.All(new[] { read, update, delete }, response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));
        Assert.Equal(before, SHA256.HashData(await File.ReadAllBytesAsync(ownerPath)));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("1e5")]
    [InlineData("9223372036854775808")]
    public async Task Invalid_browser_amounts_leave_workbook_unchanged(string amount)
    {
        using var factory = new LajurApiFactory();
        var userId = await CreateAccountAsync(factory, "invalid-web");
        var path = SeedWorkbook(factory, userId, 2026);
        using var browser = CreateBrowser(factory); await BrowserLoginAsync(browser, "invalid-web"); await EnableBrowserWrites(browser);
        var before = SHA256.HashData(await File.ReadAllBytesAsync(path));
        using var response = await browser.PostAsJsonAsync("/web/books/2026/transactions", new BrowserCreateTransaction(Guid.NewGuid(), WebInput(amount)));
        Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity);
        Assert.Equal(before, SHA256.HashData(await File.ReadAllBytesAsync(path)));
    }

    [Fact]
    public async Task Browser_amount_text_preserves_integer_precision_and_year_scope_is_enforced()
    {
        using var factory = new LajurApiFactory();
        var userId = await CreateAccountAsync(factory, "exact-web");
        var path = SeedWorkbook(factory, userId, 2026);
        using var browser = CreateBrowser(factory); await BrowserLoginAsync(browser, "exact-web"); await EnableBrowserWrites(browser);
        var id = Guid.NewGuid();
        using var create = await browser.PostAsJsonAsync("/web/books/2026/transactions", new BrowserCreateTransaction(id, WebInput("9007199254740993")));
        create.EnsureSuccessStatusCode();
        var row = await create.Content.ReadFromJsonAsync<BrowserTransaction>();
        Assert.Equal("9007199254740993", row!.AmountIdr);
        using var report = JsonDocument.Parse(await browser.GetStringAsync("/web/books/2026/report"));
        Assert.Equal("9007199254740993", report.RootElement.GetProperty("months")[9].GetProperty("rows")[0].GetProperty("amountIdrText").GetString());
        var before = SHA256.HashData(await File.ReadAllBytesAsync(path));
        using var invalidYear = await browser.PutAsJsonAsync($"/web/books/2026/transactions/{id}",
            new BrowserUpdateTransaction(1, WebInput() with { Date = new DateOnly(2027, 1, 1) }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalidYear.StatusCode);
        using var invalidCategory = await browser.PutAsJsonAsync($"/web/books/2026/transactions/{id}",
            new BrowserUpdateTransaction(1, WebInput() with { Category = "Kategori tidak terdaftar" }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalidCategory.StatusCode);
        Assert.Equal(before, SHA256.HashData(await File.ReadAllBytesAsync(path)));
    }

    private static byte[] PackagePart(string path, string name)
    {
        using var zip = ZipFile.OpenRead(path); using var stream = zip.GetEntry(name)!.Open();
        using var copy = new MemoryStream(); stream.CopyTo(copy); return copy.ToArray();
    }
    private static Worksheet Worksheet(SpreadsheetDocument document, string sheet)
    {
        var workbook = document.WorkbookPart!;
        var definition = workbook.Workbook!.Sheets!.Elements<Sheet>().Single(s => s.Name == sheet);
        return ((WorksheetPart)workbook.GetPartById(definition.Id!.Value!)).Worksheet
            ?? throw new InvalidDataException("Missing worksheet.");
    }
    private static string SheetXml(string path, string sheet)
    {
        using var document = SpreadsheetDocument.Open(path, false); return Worksheet(document, sheet).OuterXml;
    }
    private static string RightSide(string path, string sheet)
    {
        using var document = SpreadsheetDocument.Open(path, false);
        return string.Join("\n", Worksheet(document, sheet).Descendants<Cell>()
            .Where(c => { var column = new string(c.CellReference!.Value!.TakeWhile(char.IsLetter).ToArray());
                return column.Length > 1 || string.CompareOrdinal(column, "F") > 0; })
            .Select(c => c.OuterXml));
    }
}

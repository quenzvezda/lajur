using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Lajur.Application.Finance;

namespace Lajur.IntegrationTests;

public sealed partial class WebViewerTests
{
    [Fact]
    public async Task Web_master_values_refresh_browser_and_Android_and_validate_new_transactions()
    {
        using var factory = new LajurApiFactory();
        var userId = await CreateAccountAsync(factory, "master-web"); SeedWorkbook(factory, userId, 2026);
        using var browser = CreateBrowser(factory); await BrowserLoginAsync(browser, "master-web"); await EnableBrowserWrites(browser);
        var master = (await browser.GetFromJsonAsync<MasterManagement>("/web/books/2026/master"))!;
        var payload = new ChangeWorkbookMaster(Guid.NewGuid(), master.Version, "category", "create", null, "Baru API", "Pengeluaran", "50000", null);
        using var created = await browser.PutAsJsonAsync("/web/books/2026/master", payload); created.EnsureSuccessStatusCode();
        master = (await created.Content.ReadFromJsonAsync<MasterManagement>())!;
        Assert.Equal("50000", master.Categories.Last().BudgetIdr);
        using var payment = await browser.PutAsJsonAsync("/web/books/2026/master", new ChangeWorkbookMaster(Guid.NewGuid(), master.Version, "payment", "create", null, "Bayar API", null, null, "wallet")); payment.EnsureSuccessStatusCode();
        var withPayment = (await payment.Content.ReadFromJsonAsync<MasterManagement>())!;
        using var phone = factory.CreateClient(); var tokens = await AndroidLoginAsync(phone, "master-web");
        phone.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        using var androidMaster = JsonDocument.Parse(await phone.GetStringAsync("/api/v1/master?year=2026"));
        Assert.Contains(androidMaster.RootElement.GetProperty("categories").EnumerateArray(), row => row.GetProperty("name").GetString() == "Baru API");
        Assert.Contains(androidMaster.RootElement.GetProperty("paymentMethods").EnumerateArray(), value => value.GetString() == "Bayar API");
        var id = Guid.NewGuid();
        using var addTransaction = await phone.PostAsJsonAsync("/api/v1/transactions", new { transactionId = id, date = "2026-10-10", type = "Pengeluaran", category = "Baru API", paymentMethod = "Bayar API", amountIdr = 15000, description = "Synthetic" }); addTransaction.EnsureSuccessStatusCode();
        using var report = JsonDocument.Parse(await browser.GetStringAsync("/web/books/2026/report"));
        Assert.Equal("wallet", report.RootElement.GetProperty("paymentIcons").GetProperty("Bayar API").GetString());
        using var unsafeDelete = await browser.PutAsJsonAsync("/web/books/2026/master", new ChangeWorkbookMaster(Guid.NewGuid(), withPayment.Version, "payment", "delete", "Bayar API", null, null, null, null));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, unsafeDelete.StatusCode);
        using var receipt = await browser.GetAsync($"/web/books/2026/settings/operations/{payload.OperationId}"); receipt.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Removed_unused_master_rejects_stale_Android_values_without_creating_ledger_rows()
    {
        using var factory = new LajurApiFactory();
        var userId = await CreateAccountAsync(factory, "master-stale"); var path = SeedWorkbook(factory, userId, 2026);
        using var browser = CreateBrowser(factory); await BrowserLoginAsync(browser, "master-stale"); await EnableBrowserWrites(browser);
        var master = (await browser.GetFromJsonAsync<MasterManagement>("/web/books/2026/master"))!;
        using var create = await browser.PutAsJsonAsync("/web/books/2026/master", new ChangeWorkbookMaster(Guid.NewGuid(), master.Version, "category", "create", null, "Kategori sementara", "Pengeluaran", null, null)); create.EnsureSuccessStatusCode();
        master = (await create.Content.ReadFromJsonAsync<MasterManagement>())!;
        using var delete = await browser.PutAsJsonAsync("/web/books/2026/master", new ChangeWorkbookMaster(Guid.NewGuid(), master.Version, "category", "delete", "Kategori sementara", null, null, null, null)); delete.EnsureSuccessStatusCode();
        var hash = SHA256.HashData(File.ReadAllBytes(path));
        using var phone = factory.CreateClient(); var tokens = await AndroidLoginAsync(phone, "master-stale"); phone.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        using var stale = await phone.PostAsJsonAsync("/api/v1/transactions", new { transactionId = Guid.NewGuid(), date = "2026-10-10", type = "Pengeluaran", category = "Kategori sementara", paymentMethod = "", amountIdr = 15000, description = "Synthetic pending intent" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, stale.StatusCode);
        Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(path)));
    }

    [Fact]
    public async Task Master_csrf_auth_and_tenant_isolation_prevent_cross_account_mutations()
    {
        using var factory = new LajurApiFactory();
        var ownerId = await CreateAccountAsync(factory, "master-owner"); var path = SeedWorkbook(factory, ownerId, 2026);
        var otherId = await CreateAccountAsync(factory, "master-other"); SeedWorkbook(factory, otherId, 2026);
        using var owner = CreateBrowser(factory); await BrowserLoginAsync(owner, "master-owner");
        var master = (await owner.GetFromJsonAsync<MasterManagement>("/web/books/2026/master"))!;
        var payload = new ChangeWorkbookMaster(Guid.NewGuid(), master.Version, "payment", "create", null, "Terisolasi", null, null, "cash");
        var before = SHA256.HashData(File.ReadAllBytes(path));
        using var csrf = await owner.PutAsJsonAsync("/web/books/2026/master", payload); Assert.Equal(HttpStatusCode.BadRequest, csrf.StatusCode);
        using var anonymous = CreateBrowser(factory); using var read = await anonymous.GetAsync("/web/books/2026/master"); Assert.Equal(HttpStatusCode.Unauthorized, read.StatusCode);
        await EnableBrowserWrites(owner); using var saved = await owner.PutAsJsonAsync("/web/books/2026/master", payload); saved.EnsureSuccessStatusCode();
        using var other = CreateBrowser(factory); await BrowserLoginAsync(other, "master-other"); await EnableBrowserWrites(other);
        using var receipt = await other.GetAsync($"/web/books/2026/settings/operations/{payload.OperationId}"); Assert.Equal(HttpStatusCode.NotFound, receipt.StatusCode);
        var otherMaster = (await other.GetFromJsonAsync<MasterManagement>("/web/books/2026/master"))!;
        Assert.DoesNotContain(otherMaster.Payments, entry => entry.Name == "Terisolasi");
        using var bearer = factory.CreateClient(); var tokens = await AndroidLoginAsync(bearer, "master-owner"); bearer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        using var rejected = await bearer.PutAsJsonAsync("/web/books/2026/master", payload); Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        Assert.NotEqual(before, SHA256.HashData(File.ReadAllBytes(path)));
    }
}

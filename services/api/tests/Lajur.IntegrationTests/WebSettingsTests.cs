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
    public async Task Browser_budget_and_projection_refresh_reports_keep_actuals_and_support_replay_receipts()
    {
        using var factory = new LajurApiFactory();
        var userId = await CreateAccountAsync(factory, "web-settings");
        SeedWorkbook(factory, userId, 2026);
        using var browser = CreateBrowser(factory);
        await BrowserLoginAsync(browser, "web-settings"); await EnableBrowserWrites(browser);
        var budget = (await browser.GetFromJsonAsync<BudgetSettings>("/web/books/2026/settings/budget"))!;
        using var oldReport = JsonDocument.Parse(await browser.GetStringAsync("/web/books/2026/report"));
        var payload = new SaveBudgetSettings(Guid.NewGuid(), budget.Version, [new(budget.Categories[0].Category, "150000")]);
        using var save = await browser.PutAsJsonAsync("/web/books/2026/settings/budget", payload);
        save.EnsureSuccessStatusCode();
        var updated = (await save.Content.ReadFromJsonAsync<BudgetSettings>())!;
        Assert.Equal("150000", updated.Categories[0].AmountIdr);
        using var replay = await browser.PutAsJsonAsync("/web/books/2026/settings/budget", payload); replay.EnsureSuccessStatusCode();
        var receipt = await browser.GetFromJsonAsync<SettingsOperation>($"/web/books/2026/settings/operations/{payload.OperationId}");
        Assert.Equal("BUDGET", receipt!.Scope);
        using var stale = await browser.PutAsJsonAsync("/web/books/2026/settings/budget", payload with { OperationId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var projection = (await browser.GetFromJsonAsync<ProjectionSettings>("/web/books/2026/settings/projection/1"))!;
        var meal = projection.Sources.Single(s => s.IsMealAllowance);
        using var projected = await browser.PutAsJsonAsync("/web/books/2026/settings/projection/1",
            new SaveProjectionSettings(Guid.NewGuid(), projection.Version, [new(meal.Id, 12, null, 22, "35000")]));
        projected.EnsureSuccessStatusCode();
        using var report = JsonDocument.Parse(await browser.GetStringAsync("/web/books/2026/report"));
        Assert.Equal(150000, report.RootElement.GetProperty("categories")[0].GetProperty("monthlyBudgetIdr").GetInt64());
        var jan = report.RootElement.GetProperty("months")[0];
        Assert.Equal(770000, jan.GetProperty("projection").GetProperty("sources")[0].GetProperty("estimateIdr").GetInt64());
        Assert.Equal(oldReport.RootElement.GetProperty("months")[0].GetProperty("incomeIdr").GetInt64(), jan.GetProperty("incomeIdr").GetInt64());
        Assert.Equal(oldReport.RootElement.GetProperty("months")[0].GetProperty("transactionCount").GetInt32(), jan.GetProperty("transactionCount").GetInt32());
    }

    [Fact]
    public async Task Settings_are_cookie_only_csrf_protected_and_receipts_are_tenant_isolated()
    {
        using var factory = new LajurApiFactory();
        var ownerId = await CreateAccountAsync(factory, "settings-owner");
        var path = SeedWorkbook(factory, ownerId, 2026);
        var otherId = await CreateAccountAsync(factory, "settings-other"); SeedWorkbook(factory, otherId, 2026);
        using var owner = CreateBrowser(factory); await BrowserLoginAsync(owner, "settings-owner");
        var budget = (await owner.GetFromJsonAsync<BudgetSettings>("/web/books/2026/settings/budget"))!;
        var payload = new SaveBudgetSettings(Guid.NewGuid(), budget.Version, [new(budget.Categories[0].Category, "125000")]);
        var hash = SHA256.HashData(File.ReadAllBytes(path));
        using var noCsrf = await owner.PutAsJsonAsync("/web/books/2026/settings/budget", payload);
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode); Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(path)));
        using var phone = factory.CreateClient(); var tokens = await AndroidLoginAsync(phone, "settings-owner");
        phone.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        using var noCookie = await phone.GetAsync("/web/books/2026/settings/budget"); Assert.Equal(HttpStatusCode.Unauthorized, noCookie.StatusCode);
        await EnableBrowserWrites(owner);
        using var saved = await owner.PutAsJsonAsync("/web/books/2026/settings/budget", payload); saved.EnsureSuccessStatusCode();
        using var other = CreateBrowser(factory); await BrowserLoginAsync(other, "settings-other"); await EnableBrowserWrites(other);
        using var receipt = await other.GetAsync($"/web/books/2026/settings/operations/{payload.OperationId}"); Assert.Equal(HttpStatusCode.NotFound, receipt.StatusCode);
        var otherBudget = (await other.GetFromJsonAsync<BudgetSettings>("/web/books/2026/settings/budget"))!;
        Assert.Equal(budget.Categories[0].AmountIdr, otherBudget.Categories[0].AmountIdr);
        Assert.Contains("no-store", (await owner.GetAsync("/web/books/2026/settings/budget")).Headers.CacheControl!.ToString());
        using var phoneMaster = await phone.GetAsync("/api/v1/master?year=2026"); phoneMaster.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Invalid_settings_inputs_and_scope_never_mutate_workbook()
    {
        using var factory = new LajurApiFactory();
        var userId = await CreateAccountAsync(factory, "invalid-settings"); var path = SeedWorkbook(factory, userId, 2026);
        using var browser = CreateBrowser(factory); await BrowserLoginAsync(browser, "invalid-settings"); await EnableBrowserWrites(browser);
        var budget = (await browser.GetFromJsonAsync<BudgetSettings>("/web/books/2026/settings/budget"))!;
        var hash = SHA256.HashData(File.ReadAllBytes(path));
        using var invalid = await browser.PutAsJsonAsync("/web/books/2026/settings/budget",
            new SaveBudgetSettings(Guid.NewGuid(), budget.Version, [new(budget.Categories[0].Category, "1.5")]));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);
        using var unknown = await browser.PutAsJsonAsync("/web/books/2026/settings/budget",
            new SaveBudgetSettings(Guid.NewGuid(), budget.Version, [new("C999", "100")]));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, unknown.StatusCode);
        using var invalidMonth = await browser.GetAsync("/web/books/2026/settings/projection/13"); Assert.Equal(HttpStatusCode.BadRequest, invalidMonth.StatusCode);
        using var absentYear = await browser.GetAsync("/web/books/2027/settings/budget"); Assert.Equal(HttpStatusCode.NotFound, absentYear.StatusCode);
        Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(path)));
    }
}

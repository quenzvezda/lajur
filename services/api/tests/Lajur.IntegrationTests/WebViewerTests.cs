using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lajur.Application.Auth;
using Lajur.Application.Finance;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Lajur.IntegrationTests;

public sealed partial class WebViewerTests
{
    private const string Password = "correct horse battery staple";
    private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "LajurWorkbookFixture.xlsm");

    [Fact]
    public async Task Anonymous_page_redirects_to_login_but_api_keeps_json_401()
    {
        using var factory = new LajurApiFactory();
        using var browser = CreateBrowser(factory);

        using var page = await browser.GetAsync("/buku/2026");
        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
        Assert.StartsWith("/masuk", page.Headers.Location!.PathAndQuery, StringComparison.Ordinal);

        using var api = await browser.GetAsync("/api/v1/summary?year=2026&month=1");
        Assert.Equal(HttpStatusCode.Unauthorized, api.StatusCode);
        Assert.Contains("authentication_required", await api.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Browser_login_and_logout_do_not_revoke_the_android_session()
    {
        using var factory = new LajurApiFactory();
        var userId = await CreateAccountAsync(factory, "web-and-phone");
        SeedWorkbook(factory, userId, 2026);
        using var phone = factory.CreateClient();
        var tokens = await AndroidLoginAsync(phone, "web-and-phone");

        using var browser = CreateBrowser(factory);
        await BrowserLoginAsync(browser, "web-and-phone");
        using (var page = await browser.GetAsync("/buku/2026"))
        {
            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        }

        await BrowserLogoutAsync(browser);
        using (var afterLogout = await browser.GetAsync("/buku/2026"))
        {
            Assert.Equal(HttpStatusCode.Redirect, afterLogout.StatusCode);
        }

        phone.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        using var summary = await phone.GetAsync("/api/v1/summary?year=2026&month=1");
        Assert.Equal(HttpStatusCode.OK, summary.StatusCode);
        using var refresh = await phone.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = tokens.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
    }

    [Fact]
    public async Task Browser_cookie_is_not_accepted_by_the_android_api()
    {
        using var factory = new LajurApiFactory();
        var userId = await CreateAccountAsync(factory, "cookie-api");
        SeedWorkbook(factory, userId, 2026);
        using var browser = CreateBrowser(factory);
        await BrowserLoginAsync(browser, "cookie-api");

        using var api = await browser.GetAsync("/api/v1/summary?year=2026&month=1");

        Assert.Equal(HttpStatusCode.Unauthorized, api.StatusCode);
    }

    [Fact]
    public async Task Viewer_totals_match_the_api_summary_and_viewing_leaves_the_workbook_unchanged()
    {
        using var factory = new LajurApiFactory();
        var userId = await CreateAccountAsync(factory, "viewer-totals");
        var workbookPath = SeedWorkbook(factory, userId, 2026);
        var hashBefore = SHA256.HashData(await File.ReadAllBytesAsync(workbookPath));

        using var browser = CreateBrowser(factory);
        await BrowserLoginAsync(browser, "viewer-totals");
        var html = await browser.GetStringAsync("/buku/2026");
        using var report = JsonDocument.Parse(ExtractReportJson(html));

        using var phone = factory.CreateClient();
        var tokens = await AndroidLoginAsync(phone, "viewer-totals");
        phone.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var summary = await phone.GetFromJsonAsync<FinanceSummary>("/api/v1/summary?year=2026&month=1");

        var january = report.RootElement.GetProperty("months")[0];
        Assert.Equal(2026, report.RootElement.GetProperty("year").GetInt32());
        Assert.Equal(summary!.IncomeIdr, january.GetProperty("incomeIdr").GetInt64());
        Assert.Equal(summary.ExpenseIdr, january.GetProperty("expenseIdr").GetInt64());
        Assert.Equal(summary.TransactionCount, january.GetProperty("transactionCount").GetInt32());
        Assert.Equal(summary.TransactionCount, january.GetProperty("rows").GetArrayLength());
        Assert.Equal(summary.ExpenseIdr, january.GetProperty("pace").GetProperty("spentIdr").GetInt64());
        var projection = january.GetProperty("projection");
        Assert.Equal(3, projection.GetProperty("sources").GetArrayLength());
        Assert.Equal(summary.IncomeIdr, projection.GetProperty("recordedIncomeIdr").GetInt64());
        Assert.True(projection.GetProperty("complete").GetBoolean());
        Assert.Equal(JsonValueKind.String, report.RootElement.GetProperty("today").ValueKind);
        Assert.Equal(hashBefore, SHA256.HashData(await File.ReadAllBytesAsync(workbookPath)));
    }

    [Fact]
    public async Task A_user_only_sees_their_own_workbook()
    {
        using var factory = new LajurApiFactory();
        var ownerId = await CreateAccountAsync(factory, "owner-a");
        SeedWorkbook(factory, ownerId, 2026);
        await CreateAccountAsync(factory, "other-b");

        using var browser = CreateBrowser(factory);
        await BrowserLoginAsync(browser, "other-b");

        using var tampered = await browser.GetAsync("/buku/2026");
        Assert.Equal(HttpStatusCode.NotFound, tampered.StatusCode);
        Assert.DoesNotContain("report-data", await tampered.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        using var landing = await browser.GetAsync("/buku");
        Assert.Equal(HttpStatusCode.OK, landing.StatusCode);
        Assert.Contains("Belum ada buku", await landing.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Wrong_password_is_rejected_without_a_cookie()
    {
        using var factory = new LajurApiFactory();
        await CreateAccountAsync(factory, "wrong-pass");
        using var browser = CreateBrowser(factory);

        using var response = await PostLoginAsync(browser, "wrong-pass", "not the password");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Nama pengguna atau kata sandi salah", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        using var page = await browser.GetAsync("/buku");
        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
    }

    [Fact]
    public async Task Password_reset_invalidates_existing_browser_sessions()
    {
        using var factory = new LajurApiFactory();
        var userId = await CreateAccountAsync(factory, "reset-web");
        SeedWorkbook(factory, userId, 2026);
        using var browser = CreateBrowser(factory);
        await BrowserLoginAsync(browser, "reset-web");

        using (var scope = factory.Services.CreateScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<IAccountAdministration>()
                .ResetPasswordAsync("reset-web", "a completely new passphrase", CancellationToken.None);
            Assert.True(result.Succeeded);
        }

        using var page = await browser.GetAsync("/buku/2026");
        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
    }

    [Fact]
    public async Task Login_without_antiforgery_token_is_rejected_and_return_url_must_be_local()
    {
        using var factory = new LajurApiFactory();
        var userId = await CreateAccountAsync(factory, "csrf-web");
        SeedWorkbook(factory, userId, 2026);
        using var browser = CreateBrowser(factory);

        using var forged = await browser.PostAsync("/masuk", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = "csrf-web",
            ["Password"] = Password
        }));
        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);

        using var login = await PostLoginAsync(browser, "csrf-web", Password, returnUrl: "https://evil.example/");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal("/buku", login.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Viewer_pages_send_security_headers()
    {
        using var factory = new LajurApiFactory();
        var userId = await CreateAccountAsync(factory, "headers-web");
        SeedWorkbook(factory, userId, 2026);
        using var browser = CreateBrowser(factory);
        await BrowserLoginAsync(browser, "headers-web");

        using var page = await browser.GetAsync("/buku/2026");

        Assert.Contains("frame-ancestors 'none'", page.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
        Assert.Equal("no-store", page.Headers.CacheControl?.ToString());
        Assert.Equal("nosniff", page.Headers.GetValues("X-Content-Type-Options").Single());
    }

    private static HttpClient CreateBrowser(LajurApiFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true
        });

    private static async Task BrowserLoginAsync(HttpClient browser, string username)
    {
        using var response = await PostLoginAsync(browser, username, Password);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> PostLoginAsync(
        HttpClient browser,
        string username,
        string password,
        string? returnUrl = null)
    {
        var loginPage = await browser.GetStringAsync("/masuk");
        var fields = new Dictionary<string, string>
        {
            ["Username"] = username,
            ["Password"] = password,
            ["__RequestVerificationToken"] = ExtractAntiforgeryToken(loginPage)
        };
        if (returnUrl is not null)
        {
            fields["kembali"] = returnUrl;
        }

        return await browser.PostAsync("/masuk", new FormUrlEncodedContent(fields));
    }

    private static async Task BrowserLogoutAsync(HttpClient browser)
    {
        var page = await browser.GetStringAsync("/buku/2026");
        using var response = await browser.PostAsync("/keluar", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractAntiforgeryToken(page)
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private static string ExtractAntiforgeryToken(string html) =>
        AntiforgeryRegex().Match(html) is { Success: true } match
            ? WebUtility.HtmlDecode(match.Groups[1].Value)
            : throw new InvalidOperationException("Antiforgery token not found.");

    private static string ExtractReportJson(string html) =>
        ReportJsonRegex().Match(html) is { Success: true } match
            ? match.Groups[1].Value
            : throw new InvalidOperationException("Report payload not found.");

    private static async Task<AuthTokens> AndroidLoginAsync(HttpClient client, string username)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            username,
            password = Password,
            deviceLabel = "Web viewer test phone"
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthTokens>())!;
    }

    private static async Task<Guid> CreateAccountAsync(LajurApiFactory factory, string username)
    {
        using var scope = factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IAccountAdministration>()
            .CreateAsync(username, Password, CancellationToken.None);
        Assert.True(result.Succeeded, result.Failure?.ToString());
        return result.Value;
    }

    private static string SeedWorkbook(LajurApiFactory factory, Guid userId, int year)
    {
        using var scope = factory.Services.CreateScope();
        var paths = scope.ServiceProvider.GetRequiredService<IUserStoragePathResolver>();
        var workbookPath = Path.Combine(paths.GetUserRoot(userId), "workbooks", $"Keuangan_Pribadi_{year}.xlsm");
        Directory.CreateDirectory(Path.GetDirectoryName(workbookPath)!);
        File.Copy(FixturePath, workbookPath, overwrite: false);
        return workbookPath;
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryRegex();

    [GeneratedRegex("<script type=\"application/json\" id=\"report-data\">(.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex ReportJsonRegex();
}

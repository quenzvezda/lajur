using System.Net;
using System.Text.RegularExpressions;
using Lajur.Application.Auth;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Lajur.IntegrationTests;

public sealed class AccountBrowserTests
{
    private const string Password = "a long web test password";
    private static async Task<HttpResponseMessage> Form(HttpClient browser, string page, Dictionary<string, string> fields)
    {
        var html = await browser.GetStringAsync(page);
        fields["__RequestVerificationToken"] = WebUtility.HtmlDecode(
            Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value);
        Assert.NotEmpty(fields["__RequestVerificationToken"]);
        return await browser.PostAsync(page, new FormUrlEncodedContent(fields));
    }

    [Fact]
    public async Task Browser_register_recover_and_authenticated_actions_enforce_csrf_and_session_revocation()
    {
        using var factory = new LajurApiFactory();
        using var browser = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true });
        using var initial = await browser.GetAsync("/masuk");
        string invitation;
        using (var scope = factory.Services.CreateScope())
        {
            var id = (await scope.ServiceProvider.GetRequiredService<IAccountAdministration>().CreateAsync("inviter", Password, default)).Value;
            invitation = (await scope.ServiceProvider.GetRequiredService<IAccountSelfService>().InviteAsync(id, Password, default)).Value!.Code;
        }
        using var forged = await browser.PostAsync("/daftar", new FormUrlEncodedContent(new Dictionary<string, string> {
            ["Username"] = "browser-user", ["Password"] = Password, ["ConfirmPassword"] = Password, ["InvitationCode"] = invitation }));
        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
        using var signup = await Form(browser, "/daftar", new() { ["Username"] = "browser-user", ["Password"] = Password,
            ["ConfirmPassword"] = Password, ["InvitationCode"] = invitation });
        Assert.Equal(HttpStatusCode.OK, signup.StatusCode);
        var html = await signup.Content.ReadAsStringAsync();
        Assert.Contains("Akun berhasil dibuat", html);
        Assert.DoesNotContain(Password, html);
        var codes = Regex.Matches(html, "[A-F0-9]{4}(?:-[A-F0-9]{4}){7}").Select(match => match.Value).ToArray();
        Assert.Equal(8, codes.Length);
        Assert.True(signup.Headers.CacheControl?.NoStore);
        using var login = await Form(browser, "/masuk", new() { ["Username"] = "browser-user", ["Password"] = Password });
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        using var account = await browser.GetAsync("/akun");
        Assert.Equal(HttpStatusCode.OK, account.StatusCode);
        Assert.DoesNotContain(codes[0], await account.Content.ReadAsStringAsync());
        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true });
        using var recover = await Form(anonymous, "/pulihkan", new() { ["Username"] = "browser-user", ["Code"] = codes[0],
            ["NewPassword"] = Password + " changed", ["ConfirmPassword"] = Password + " changed" });
        Assert.Contains("Password berhasil diganti", await recover.Content.ReadAsStringAsync());
        using var expired = await browser.GetAsync("/akun");
        Assert.Equal(HttpStatusCode.Redirect, expired.StatusCode);
        using var relogin = await Form(browser, "/masuk", new() { ["Username"] = "browser-user", ["Password"] = Password + " changed" });
        Assert.Equal(HttpStatusCode.Redirect, relogin.StatusCode);
        using var regenerated = await Form(browser, "/akun", new() { ["action"] = "codes", ["Password"] = Password + " changed" });
        Assert.Equal(8, Regex.Matches(await regenerated.Content.ReadAsStringAsync(), "[A-F0-9]{4}(?:-[A-F0-9]{4}){7}").Count);
        using var invite = await Form(browser, "/akun", new() { ["action"] = "invite", ["Password"] = Password + " changed" });
        Assert.Contains("Undangan siap dibagikan", await invite.Content.ReadAsStringAsync());
        using var change = await Form(browser, "/akun", new() { ["action"] = "password", ["Password"] = Password + " changed",
            ["NewPassword"] = Password + " final", ["ConfirmPassword"] = Password + " final" });
        Assert.Equal(HttpStatusCode.Redirect, change.StatusCode);
        using var ended = await browser.GetAsync("/akun");
        Assert.Equal(HttpStatusCode.Redirect, ended.StatusCode);
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Lajur.Application.Auth;
using Lajur.Infrastructure.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace Lajur.IntegrationTests;

public sealed class AuthEndpointsTests
{
    private const string Password = "correct horse battery staple";

    [Fact]
    public async Task Refresh_rotates_token_and_replay_revokes_session_family()
    {
        using var factory = new LajurApiFactory();
        await CreateAccountAsync(factory, "rama", Password);
        using var client = factory.CreateClient();

        var login = await LoginAsync(client, "rama", Password, "POCO F6");
        Assert.Equal(HttpStatusCode.OK, await GetMeStatusAsync(client, login.AccessToken));

        using var refreshResponse = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh",
            new { refreshToken = login.RefreshToken });
        refreshResponse.EnsureSuccessStatusCode();
        var rotated = await refreshResponse.Content.ReadFromJsonAsync<AuthTokens>();
        Assert.NotNull(rotated);
        Assert.NotEqual(login.RefreshToken, rotated.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, await GetMeStatusAsync(client, rotated.AccessToken));

        using var replayResponse = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh",
            new { refreshToken = login.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, replayResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await GetMeStatusAsync(client, rotated.AccessToken));
    }

    [Fact]
    public async Task Concurrent_refresh_allows_only_one_rotation()
    {
        using var factory = new LajurApiFactory();
        await CreateAccountAsync(factory, "concurrent", Password);
        using var client = factory.CreateClient();
        var login = await LoginAsync(client, "concurrent", Password, "Phone");

        var requests = Enumerable.Range(0, 2)
            .Select(_ => client.PostAsJsonAsync(
                "/api/v1/auth/refresh",
                new { refreshToken = login.RefreshToken }))
            .ToArray();
        var responses = await Task.WhenAll(requests);
        try
        {
            Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
            Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Unauthorized));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task Login_on_new_device_revokes_old_refresh_and_access_session()
    {
        using var factory = new LajurApiFactory();
        await CreateAccountAsync(factory, "sari", Password);
        using var client = factory.CreateClient();

        var oldSession = await LoginAsync(client, "sari", Password, "Old phone");
        var newSession = await LoginAsync(client, "sari", Password, "New phone");

        Assert.NotEqual(oldSession.Device.Id, newSession.Device.Id);
        Assert.Equal(HttpStatusCode.Unauthorized, await GetMeStatusAsync(client, oldSession.AccessToken));
        Assert.Equal(HttpStatusCode.OK, await GetMeStatusAsync(client, newSession.AccessToken));

        using var oldRefresh = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh",
            new { refreshToken = oldSession.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, oldRefresh.StatusCode);
    }

    [Fact]
    public async Task Logout_and_password_reset_revoke_access_sessions()
    {
        using var factory = new LajurApiFactory();
        await CreateAccountAsync(factory, "bima", Password);
        using var client = factory.CreateClient();

        var first = await LoginAsync(client, "bima", Password, "Phone");
        using var logout = CreateAuthorizedRequest(HttpMethod.Post, "/api/v1/auth/logout", first.AccessToken);
        using var logoutResponse = await client.SendAsync(logout);
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await GetMeStatusAsync(client, first.AccessToken));

        var second = await LoginAsync(client, "bima", Password, "Phone", first.Device.Id);
        await ResetPasswordAsync(factory, "bima", "a completely new password");
        Assert.Equal(HttpStatusCode.Unauthorized, await GetMeStatusAsync(client, second.AccessToken));

        using var oldPasswordLogin = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            username = "bima",
            password = Password,
            deviceLabel = "Phone"
        });
        Assert.Equal(HttpStatusCode.Unauthorized, oldPasswordLogin.StatusCode);
        await LoginAsync(client, "bima", "a completely new password", "Phone");
    }

    [Fact]
    public async Task Account_cannot_revoke_other_accounts_device_or_share_storage_path()
    {
        using var factory = new LajurApiFactory();
        var aliceId = await CreateAccountAsync(factory, "alice", Password);
        var bobId = await CreateAccountAsync(factory, "bob", Password);
        using var client = factory.CreateClient();
        var alice = await LoginAsync(client, "alice", Password, "Alice phone");
        var bob = await LoginAsync(client, "bob", Password, "Bob phone");

        using var revokeBob = CreateAuthorizedRequest(
            HttpMethod.Delete,
            $"/api/v1/devices/{bob.Device.Id}",
            alice.AccessToken);
        using var revokeResponse = await client.SendAsync(revokeBob);
        Assert.Equal(HttpStatusCode.NotFound, revokeResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, await GetMeStatusAsync(client, bob.AccessToken));

        using var scope = factory.Services.CreateScope();
        var paths = scope.ServiceProvider.GetRequiredService<IUserStoragePathResolver>();
        var alicePath = paths.GetUserRoot(aliceId);
        var bobPath = paths.GetUserRoot(bobId);
        Assert.NotEqual(alicePath, bobPath);
        Assert.Contains(aliceId.ToString("N"), alicePath);
        Assert.Contains(bobId.ToString("N"), bobPath);
        Assert.DoesNotContain("alice", alicePath, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bob", bobPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_is_rate_limited_and_failure_is_generic()
    {
        using var factory = new LajurApiFactory();
        using var client = factory.CreateClient();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var response = await client.PostAsJsonAsync("/api/v1/auth/login", new
            {
                username = "unknown",
                password = "not the right password",
                deviceLabel = "Phone"
            });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("unknown", body, StringComparison.OrdinalIgnoreCase);
        }

        using var limited = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            username = "unknown",
            password = "not the right password",
            deviceLabel = "Phone"
        });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [Fact]
    public async Task Database_stores_only_refresh_token_hash()
    {
        using var factory = new LajurApiFactory();
        await CreateAccountAsync(factory, "hashcheck", Password);
        using var client = factory.CreateClient();
        var login = await LoginAsync(client, "hashcheck", Password, "Phone");

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var stored = Assert.Single(dbContext.RefreshSessions);
        Assert.NotEqual(login.RefreshToken, stored.RefreshTokenHash);
        Assert.Equal(64, stored.RefreshTokenHash.Length);
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

    private static async Task ResetPasswordAsync(
        LajurApiFactory factory,
        string username,
        string password)
    {
        using var scope = factory.Services.CreateScope();
        var administration = scope.ServiceProvider.GetRequiredService<IAccountAdministration>();
        var result = await administration.ResetPasswordAsync(username, password, CancellationToken.None);
        Assert.True(result.Succeeded, result.Failure?.ToString());
    }

    private static async Task<AuthTokens> LoginAsync(
        HttpClient client,
        string username,
        string password,
        string deviceLabel,
        Guid? deviceId = null)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            username,
            password,
            deviceId,
            deviceLabel
        });
        response.EnsureSuccessStatusCode();
        return Assert.IsType<AuthTokens>(await response.Content.ReadFromJsonAsync<AuthTokens>());
    }

    private static async Task<HttpStatusCode> GetMeStatusAsync(HttpClient client, string accessToken)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Get, "/api/v1/me", accessToken);
        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    private static HttpRequestMessage CreateAuthorizedRequest(HttpMethod method, string path, string token)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Lajur.Application.Auth;
using Lajur.Infrastructure.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Lajur.IntegrationTests;

public sealed class AccountRegistrationTests
{
    private const string Password = "a long private test password";
    private const string NewPassword = "another long test password";

    private static async Task<Guid> Owner(LajurApiFactory factory)
    {
        // Starting the host initializes old/new schema before scoped services are used.
        using var client = factory.CreateClient();
        await client.GetAsync("/health/ready");
        using var scope = factory.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<IAccountAdministration>()
            .CreateAsync("owner", Password, default)).Value;
    }

    private static async Task<InvitationReceipt> Invite(LajurApiFactory factory, Guid id)
    {
        using var scope = factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IAccountSelfService>().InviteAsync(id, Password, default);
        Assert.True(result.Succeeded);
        return result.Value!;
    }

    private static async Task<AuthResult<RegistrationReceipt>> Register(LajurApiFactory factory, string name, string invitation)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IAccountSelfService>()
            .RegisterAsync(new(name, Password, invitation), default);
    }

    [Fact]
    public async Task Invitation_is_single_use_and_new_account_stays_isolated()
    {
        using var factory = new LajurApiFactory();
        var owner = await Owner(factory);
        var invitation = await Invite(factory, owner);
        Assert.InRange(invitation.ExpiresAtUtc, DateTimeOffset.UtcNow.AddDays(6), DateTimeOffset.UtcNow.AddDays(8));
        var result = await Register(factory, "new-account", invitation.Code);
        Assert.True(result.Succeeded);
        Assert.Equal(8, result.Value!.Codes.Distinct().Count());
        Assert.Equal(AuthFailure.InvalidInvitation, (await Register(factory, "other-account", invitation.Code)).Failure);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var user = await db.Users.SingleAsync(user => user.Username == "new-account");
        Assert.NotEqual(owner, user.Id);
        Assert.True(await db.SelfRegisteredAccounts.AnyAsync(account => account.UserId == user.Id));
        var hashes = await db.RecoveryCodes.Where(code => code.UserId == user.Id).Select(code => code.Hash).ToListAsync();
        Assert.DoesNotContain(result.Value.Codes[0].Replace("-", ""), hashes);
        Assert.False(Directory.Exists(Path.Combine(factory.StorageRoot, "data", user.Id.ToString("N"))));
    }

    [Fact]
    public async Task Failed_registration_keeps_invitation_available_and_duplicate_username_cannot_overwrite()
    {
        using var factory = new LajurApiFactory();
        var owner = await Owner(factory);
        var invitation = await Invite(factory, owner);
        Assert.Equal(AuthFailure.UsernameAlreadyExists, (await Register(factory, "owner", invitation.Code)).Failure);
        Assert.True((await Register(factory, "different", invitation.Code)).Succeeded);
    }

    [Fact]
    public async Task Concurrent_invitation_consumption_has_one_winner()
    {
        using var factory = new LajurApiFactory();
        var owner = await Owner(factory);
        var invitation = await Invite(factory, owner);
        var results = await Task.WhenAll(Register(factory, "first-user", invitation.Code), Register(factory, "second-user", invitation.Code));
        Assert.Single(results, result => result.Succeeded);
        Assert.Single(results, result => result.Failure == AuthFailure.InvalidInvitation);
    }

    [Fact]
    public async Task Recovery_is_single_use_revokes_android_and_invalidates_other_codes()
    {
        using var factory = new LajurApiFactory();
        var owner = await Owner(factory);
        using var scope = factory.Services.CreateScope();
        var self = scope.ServiceProvider.GetRequiredService<IAccountSelfService>();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
        var codes = (await self.RegenerateCodesAsync(owner, Password, default)).Value!.Codes;
        var tokens = (await auth.LoginAsync(new("owner", Password, null, "Test"), default)).Value!;
        using var client = factory.CreateClient();
        var failed = await client.PostAsJsonAsync("/api/v1/auth/recover", new { username = "owner", code = "WRONG", newPassword = NewPassword });
        Assert.True(failed.StatusCode == HttpStatusCode.BadRequest, await failed.Content.ReadAsStringAsync());
        var success = await client.PostAsJsonAsync("/api/v1/auth/recover", new { username = "owner", code = codes[0], newPassword = NewPassword });
        Assert.Equal(HttpStatusCode.NoContent, success.StatusCode);
        Assert.True(success.Headers.CacheControl?.NoStore);
        Assert.False(await self.RecoverAsync("owner", codes[0], Password, default));
        Assert.False(await self.RecoverAsync("owner", codes[1], Password, default));
        // Fresh scope avoids the previous tracked password snapshot.
        using var fresh = factory.Services.CreateScope();
        var refreshedAuth = fresh.ServiceProvider.GetRequiredService<IAuthService>();
        Assert.False((await refreshedAuth.RefreshAsync(tokens.RefreshToken, default)).Succeeded);
        Assert.False((await refreshedAuth.LoginAsync(new("owner", Password, null, "Test"), default)).Succeeded);
        Assert.True((await refreshedAuth.LoginAsync(new("owner", NewPassword, null, "Test"), default)).Succeeded);
    }

    [Fact]
    public async Task Reauthentication_and_invitation_limits_are_enforced()
    {
        using var factory = new LajurApiFactory();
        var owner = await Owner(factory);
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAccountSelfService>();
        Assert.False((await service.RegenerateCodesAsync(owner, "wrong password", default)).Succeeded);
        Assert.False(await service.ChangePasswordAsync(owner, "wrong password", NewPassword, default));
        for (var index = 0; index < 5; index++) Assert.True((await service.InviteAsync(owner, Password, default)).Succeeded);
        Assert.Equal(AuthFailure.InvitationLimit, (await service.InviteAsync(owner, Password, default)).Failure);
    }

    [Fact]
    public async Task Expired_invitation_and_disabled_issuer_are_rejected()
    {
        using var factory = new LajurApiFactory();
        var owner = await Owner(factory);
        var invitation = await Invite(factory, owner);
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AuthDbContext>().Invitations.ExecuteUpdateAsync(update => update.SetProperty(code => code.ExpiresAtUnix, 0));
        Assert.False((await Register(factory, "expired", invitation.Code)).Succeeded);
        invitation = await Invite(factory, owner);
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AuthDbContext>().Users.Where(user => user.Id == owner)
                .ExecuteUpdateAsync(update => update.SetProperty(user => user.DisabledAtUtc, DateTimeOffset.UtcNow));
        Assert.False((await Register(factory, "disabled", invitation.Code)).Succeeded);
    }

    [Fact]
    public async Task Additive_schema_upgrade_preserves_old_account_id_and_password()
    {
        using var factory = new LajurApiFactory();
        var owner = await Owner(factory);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            await db.Database.ExecuteSqlRawAsync("DROP TABLE RecoveryCodes; DROP TABLE AccountInvitations; DROP TABLE SelfRegisteredAccounts; DROP TABLE AuthSchemaVersions;");
            await scope.ServiceProvider.GetRequiredService<AuthDatabaseInitializer>().InitializeAsync();
            await scope.ServiceProvider.GetRequiredService<AuthDatabaseInitializer>().InitializeAsync();
            Assert.Equal(owner, (await db.Users.SingleAsync()).Id);
            Assert.Empty(await db.RecoveryCodes.ToArrayAsync());
        }
        using var fresh = factory.Services.CreateScope();
        Assert.True((await fresh.ServiceProvider.GetRequiredService<IAuthService>()
            .LoginAsync(new("owner", Password, null, "Test"), default)).Succeeded);
    }

    [Fact]
    public async Task New_registration_cannot_fall_back_to_owner_template()
    {
        using var factory = new LajurApiFactory();
        var owner = await Owner(factory);
        await Register(factory, "no-private-template", (await Invite(factory, owner)).Code);
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { username = "no-private-template", password = Password });
        var tokens = await response.Content.ReadFromJsonAsync<AuthTokens>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
        Directory.CreateDirectory(Path.Combine(factory.StorageRoot, "templates"));
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "LajurWorkbookFixture.xlsm"), Path.Combine(factory.StorageRoot, "templates", "finance-template.xlsm"));
        using var create = await client.PostAsync("/api/v1/workbooks/2026/create", null);
        Assert.False(create.IsSuccessStatusCode);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "LajurWorkbookFixture.xlsm"), Path.Combine(factory.StorageRoot, "templates", "registration-template.xlsm"));
        using var correct = await client.PostAsync("/api/v1/workbooks/2026/create", null);
        Assert.True(correct.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Closed_mode_and_host_account_limit_are_enforced()
    {
        using var factory = new LajurApiFactory();
        await Owner(factory);
        using var scope = factory.Services.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<IOptions<RegistrationOptions>>().Value;
        settings.Mode = "closed";
        Assert.Equal(AuthFailure.RegistrationUnavailable, (await Register(factory, "closed-user", "")).Failure);
        settings.Mode = "open";
        settings.MaxAccounts = 1;
        Assert.Equal(AuthFailure.RegistrationUnavailable, (await Register(factory, "over-cap", "")).Failure);
        settings.MaxAccounts = 2;
        Assert.True((await Register(factory, "open-user", "")).Succeeded);
    }

    [Fact]
    public async Task Recovery_endpoint_limits_repeated_invalid_attempts()
    {
        using var factory = new LajurApiFactory();
        using var client = factory.CreateClient();
        for (var attempt = 0; attempt < 10; attempt++)
        {
            using var failed = await client.PostAsJsonAsync("/api/v1/auth/recover", new { username = "unknown", code = "invalid", newPassword = NewPassword });
            Assert.Equal(HttpStatusCode.BadRequest, failed.StatusCode);
        }
        using var limited = await client.PostAsJsonAsync("/api/v1/auth/recover", new { username = "unknown", code = "invalid", newPassword = NewPassword });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }
}

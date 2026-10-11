using System.Security.Cryptography;
using System.Text;
using Lajur.Application.Auth;
using Lajur.Domain.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Lajur.Infrastructure.Auth;

public sealed partial class SqliteAuthService
{
    public RegistrationPolicy Policy => new(registrationOptions.Value.Mode);
    private static string NewCode() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
    private static string CodeHash(string purpose, string code) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes($"lajur:{purpose}:{code.Replace("-", "").Replace(" ", "").ToUpperInvariant()}")));

    public async Task<AuthResult<RegistrationReceipt>> RegisterAsync(RegisterAccount command, CancellationToken cancellationToken)
    {
        var settings = registrationOptions.Value;
        if (settings.Mode == "closed") return AuthResult<RegistrationReceipt>.Fail(AuthFailure.RegistrationUnavailable);
        if (NormalizeUsername(command.Username) is null) return AuthResult<RegistrationReceipt>.Fail(AuthFailure.InvalidUsername);
        if (!IsValidPassword(command.Password)) return AuthResult<RegistrationReceipt>.Fail(AuthFailure.InvalidPassword);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (await dbContext.Users.CountAsync(cancellationToken) >= settings.MaxAccounts)
            return AuthResult<RegistrationReceipt>.Fail(AuthFailure.RegistrationUnavailable);
        var now = timeProvider.GetUtcNow().ToUnixTimeSeconds();
        if (settings.Mode == "invite")
        {
            var hash = CodeHash("invite", command.InvitationCode ?? "");
            var invitation = await dbContext.Invitations.SingleOrDefaultAsync(code => code.Hash == hash
                && code.UsedAtUnix == null && code.ExpiresAtUnix > now, cancellationToken);
            if (invitation is null || !await dbContext.Users.AnyAsync(user => user.Id == invitation.CreatedBy
                && user.DisabledAtUtc == null, cancellationToken))
                return AuthResult<RegistrationReceipt>.Fail(AuthFailure.InvalidInvitation);
            invitation.UsedAtUnix = now;
        }
        var created = await CreateAsync(command.Username, command.Password, cancellationToken);
        if (!created.Succeeded) return AuthResult<RegistrationReceipt>.Fail(created.Failure!.Value);
        dbContext.SelfRegisteredAccounts.Add(new SelfRegisteredAccount { UserId = created.Value });
        var codes = await ReplaceCodesAsync(created.Value, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return AuthResult<RegistrationReceipt>.Success(new(command.Username.Trim(), codes));
    }

    public async Task<bool> RecoverAsync(string username, string code, string newPassword, CancellationToken cancellationToken)
    {
        if (!IsValidPassword(newPassword)) return false;
        var normalized = NormalizeUsername(username);
        var hash = CodeHash("recovery", code);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var user = await dbContext.Users.SingleOrDefaultAsync(user => user.NormalizedUsername == normalized
            && user.DisabledAtUtc == null, cancellationToken);
        if (user is null) return false;
        var consumed = await dbContext.RecoveryCodes.Where(code => code.UserId == user.Id && code.Hash == hash
                && code.UsedAtUnix == null)
            .ExecuteUpdateAsync(update => update.SetProperty(code => code.UsedAtUnix,
                timeProvider.GetUtcNow().ToUnixTimeSeconds()), cancellationToken);
        if (consumed != 1) return false;
        user.PasswordHash = passwordHasher.HashPassword(user, newPassword);
        await RevokeUserSessionsAsync(user.Id, timeProvider.GetUtcNow(), cancellationToken);
        // Recovery invalidates the whole old set. Generate a new set after login.
        await dbContext.RecoveryCodes.Where(code => code.UserId == user.Id).ExecuteDeleteAsync(cancellationToken);
        await dbContext.Invitations.Where(code => code.CreatedBy == user.Id && code.UsedAtUnix == null).ExecuteDeleteAsync(cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task<UserAccount?> ReauthenticateAsync(Guid userId, string password, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.SingleOrDefaultAsync(user => user.Id == userId && user.DisabledAtUtc == null, cancellationToken);
        return user is not null && passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password)
            != PasswordVerificationResult.Failed ? user : null;
    }

    public async Task<bool> ChangePasswordAsync(Guid userId, string password, string newPassword, CancellationToken cancellationToken)
    {
        if (!IsValidPassword(newPassword)) return false;
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var user = await ReauthenticateAsync(userId, password, cancellationToken);
        if (user is null) return false;
        user.PasswordHash = passwordHasher.HashPassword(user, newPassword);
        await RevokeUserSessionsAsync(user.Id, timeProvider.GetUtcNow(), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<AuthResult<RecoveryCodes>> RegenerateCodesAsync(Guid userId, string password, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (await ReauthenticateAsync(userId, password, cancellationToken) is null)
            return AuthResult<RecoveryCodes>.Fail(AuthFailure.InvalidCredentials);
        var codes = await ReplaceCodesAsync(userId, timeProvider.GetUtcNow().ToUnixTimeSeconds(), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return AuthResult<RecoveryCodes>.Success(new(codes));
    }

    private async Task<IReadOnlyList<string>> ReplaceCodesAsync(Guid userId, long now, CancellationToken cancellationToken)
    {
        await dbContext.RecoveryCodes.Where(code => code.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        var codes = Enumerable.Range(0, 8).Select(_ => NewCode()).ToArray();
        dbContext.RecoveryCodes.AddRange(codes.Select(code => new AccountRecoveryCode
            { Hash = CodeHash("recovery", code), UserId = userId, CreatedAtUnix = now }));
        await dbContext.SaveChangesAsync(cancellationToken);
        return codes.Select(code => string.Join("-", Enumerable.Range(0, 8).Select(index => code.Substring(index * 4, 4)))).ToArray();
    }

    public async Task<AuthResult<InvitationReceipt>> InviteAsync(Guid userId, string password, CancellationToken cancellationToken)
    {
        if (Policy.Mode != "invite") return AuthResult<InvitationReceipt>.Fail(AuthFailure.RegistrationUnavailable);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (await ReauthenticateAsync(userId, password, cancellationToken) is null)
            return AuthResult<InvitationReceipt>.Fail(AuthFailure.InvalidCredentials);
        var now = timeProvider.GetUtcNow();
        if (await dbContext.Invitations.CountAsync(code => code.CreatedBy == userId && code.UsedAtUnix == null
                && code.ExpiresAtUnix > now.ToUnixTimeSeconds(), cancellationToken) >= 5)
            return AuthResult<InvitationReceipt>.Fail(AuthFailure.InvitationLimit);
        var plaintext = NewCode();
        var expiry = now.AddDays(7);
        dbContext.Invitations.Add(new AccountInvitation { Hash = CodeHash("invite", plaintext), CreatedBy = userId,
            ExpiresAtUnix = expiry.ToUnixTimeSeconds() });
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return AuthResult<InvitationReceipt>.Success(new(plaintext, expiry));
    }
}

using System.Security.Cryptography;
using System.Text;
using Lajur.Application.Auth;
using Lajur.Domain.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Lajur.Infrastructure.Auth;

public sealed partial class SqliteAuthService : IBrowserAuthService
{
    private static readonly Lazy<string> TimingEqualizerHash = new(() =>
        new PasswordHasher<UserAccount>().HashPassword(
            new UserAccount { Username = "-", NormalizedUsername = "-", PasswordHash = string.Empty },
            Guid.NewGuid().ToString("N")));

    public async Task<BrowserIdentity?> VerifyCredentialsAsync(
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        var normalized = NormalizeUsername(username);
        var user = normalized is null
            ? null
            : await dbContext.Users.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.NormalizedUsername == normalized, cancellationToken);

        if (user is null || user.DisabledAtUtc is not null)
        {
            // Spend comparable hashing time so response timing does not reveal which usernames exist.
            _ = passwordHasher.VerifyHashedPassword(
                new UserAccount { Username = "-", NormalizedUsername = "-", PasswordHash = string.Empty },
                TimingEqualizerHash.Value,
                password ?? string.Empty);
            return null;
        }

        // Deliberately no rehash/write here: browser sign-in must not change auth state used by Android.
        var verification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password ?? string.Empty);
        return verification == PasswordVerificationResult.Failed ? null : ToBrowserIdentity(user);
    }

    public async Task<BrowserIdentity?> GetActiveIdentityAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);
        return user is null || user.DisabledAtUtc is not null ? null : ToBrowserIdentity(user);
    }

    private static BrowserIdentity ToBrowserIdentity(UserAccount user)
    {
        var stamp = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes($"lajur-web-v1:{user.Id:N}:{user.PasswordHash}")));
        return new BrowserIdentity(user.Id, user.Username, stamp);
    }
}

using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Lajur.Application.Auth;
using Lajur.Domain.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Lajur.Infrastructure.Auth;

public sealed partial class SqliteAuthService(
    AuthDbContext dbContext,
    IPasswordHasher<UserAccount> passwordHasher,
    IAccessTokenIssuer accessTokenIssuer,
    IOptions<AuthOptions> options,
    TimeProvider timeProvider,
    IOptions<RegistrationOptions> registrationOptions) : IAuthService, IAccountAdministration, IAccountSelfService
{
    private readonly AuthOptions _options = options.Value;

    public async Task<AuthResult<Guid>> CreateAsync(
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        var normalized = NormalizeUsername(username);
        if (normalized is null)
        {
            return AuthResult<Guid>.Fail(AuthFailure.InvalidUsername);
        }

        if (!IsValidPassword(password))
        {
            return AuthResult<Guid>.Fail(AuthFailure.InvalidPassword);
        }

        if (await dbContext.Users.AnyAsync(
                user => user.NormalizedUsername == normalized,
                cancellationToken))
        {
            return AuthResult<Guid>.Fail(AuthFailure.UsernameAlreadyExists);
        }

        var user = new UserAccount
        {
            Id = Guid.NewGuid(),
            Username = username.Trim(),
            NormalizedUsername = normalized,
            PasswordHash = string.Empty,
            CreatedAtUtc = timeProvider.GetUtcNow()
        };
        user.PasswordHash = passwordHasher.HashPassword(user, password);
        dbContext.Users.Add(user);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return AuthResult<Guid>.Fail(AuthFailure.UsernameAlreadyExists);
        }

        return AuthResult<Guid>.Success(user.Id);
    }

    public async Task<AuthResult<Guid>> ResetPasswordAsync(
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        var normalized = NormalizeUsername(username);
        if (normalized is null)
        {
            return AuthResult<Guid>.Fail(AuthFailure.UserNotFound);
        }

        if (!IsValidPassword(password))
        {
            return AuthResult<Guid>.Fail(AuthFailure.InvalidPassword);
        }

        var user = await dbContext.Users.SingleOrDefaultAsync(
            candidate => candidate.NormalizedUsername == normalized,
            cancellationToken);
        if (user is null)
        {
            return AuthResult<Guid>.Fail(AuthFailure.UserNotFound);
        }

        user.PasswordHash = passwordHasher.HashPassword(user, password);
        await RevokeUserSessionsAsync(user.Id, timeProvider.GetUtcNow(), cancellationToken);
        await dbContext.RecoveryCodes.Where(code => code.UserId == user.Id).ExecuteDeleteAsync(cancellationToken);
        await dbContext.Invitations.Where(code => code.CreatedBy == user.Id && code.UsedAtUnix == null).ExecuteDeleteAsync(cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return AuthResult<Guid>.Success(user.Id);
    }

    public async Task<AuthResult<AuthTokens>> LoginAsync(
        LoginCommand command,
        CancellationToken cancellationToken)
    {
        var normalized = NormalizeUsername(command.Username);
        if (normalized is null)
        {
            return AuthResult<AuthTokens>.Fail(AuthFailure.InvalidCredentials);
        }

        var user = await dbContext.Users
            .Include(candidate => candidate.Devices)
            .SingleOrDefaultAsync(
                candidate => candidate.NormalizedUsername == normalized,
                cancellationToken);
        if (user is null || user.DisabledAtUtc is not null)
        {
            return AuthResult<AuthTokens>.Fail(AuthFailure.InvalidCredentials);
        }

        var verification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, command.Password);
        if (verification == PasswordVerificationResult.Failed)
        {
            return AuthResult<AuthTokens>.Fail(AuthFailure.InvalidCredentials);
        }

        var now = timeProvider.GetUtcNow();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        await RevokeUserSessionsAsync(user.Id, now, cancellationToken);
        foreach (var existingDevice in user.Devices)
        {
            existingDevice.RevokedAtUtc = now;
        }

        var device = command.DeviceId is { } requestedId
            ? user.Devices.SingleOrDefault(candidate => candidate.Id == requestedId)
            : null;
        if (device is null)
        {
            device = new RegisteredDevice
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Label = NormalizeDeviceLabel(command.DeviceLabel),
                CreatedAtUtc = now,
                LastSeenAtUtc = now
            };
            dbContext.Devices.Add(device);
        }
        else
        {
            device.Label = NormalizeDeviceLabel(command.DeviceLabel, device.Label);
            device.LastSeenAtUtc = now;
            device.RevokedAtUtc = null;
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = passwordHasher.HashPassword(user, command.Password);
        }

        var issued = CreateRefreshSession(user.Id, device.Id, Guid.NewGuid(), now, null);
        dbContext.RefreshSessions.Add(issued.Session);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return AuthResult<AuthTokens>.Success(CreateAuthTokens(user, device, issued));
    }

    public async Task<AuthResult<AuthTokens>> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken) || refreshToken.Length > 512)
        {
            return AuthResult<AuthTokens>.Fail(AuthFailure.InvalidRefreshToken);
        }

        var hash = HashRefreshToken(refreshToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var current = await dbContext.RefreshSessions
            .Include(session => session.User)
            .Include(session => session.Device)
            .SingleOrDefaultAsync(session => session.RefreshTokenHash == hash, cancellationToken);
        if (current is null)
        {
            return AuthResult<AuthTokens>.Fail(AuthFailure.InvalidRefreshToken);
        }

        var now = timeProvider.GetUtcNow();
        if (current.RevokedAtUtc is not null)
        {
            if (current.ReplacedBySessionId is not null)
            {
                await RevokeFamilyAsync(current.FamilyId, now, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }

            return AuthResult<AuthTokens>.Fail(AuthFailure.InvalidRefreshToken);
        }

        if (current.User.DisabledAtUtc is not null ||
            current.Device.RevokedAtUtc is not null ||
            current.IdleExpiresAtUtc <= now ||
            current.AbsoluteExpiresAtUtc <= now)
        {
            await RevokeFamilyAsync(current.FamilyId, now, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return AuthResult<AuthTokens>.Fail(AuthFailure.InvalidRefreshToken);
        }

        var next = CreateRefreshSession(
            current.UserId,
            current.DeviceId,
            current.FamilyId,
            now,
            current.AbsoluteExpiresAtUtc);
        current.LastUsedAtUtc = now;
        current.RevokedAtUtc = now;
        current.ReplacedBySessionId = next.Session.Id;
        current.Device.LastSeenAtUtc = now;
        dbContext.RefreshSessions.Add(next.Session);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return AuthResult<AuthTokens>.Success(CreateAuthTokens(current.User, current.Device, next));
    }

    public async Task LogoutAsync(
        Guid userId,
        Guid sessionFamilyId,
        CancellationToken cancellationToken)
    {
        await dbContext.RefreshSessions
            .Where(session => session.UserId == userId &&
                              session.FamilyId == sessionFamilyId &&
                              session.RevokedAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    session => session.RevokedAtUtc,
                    timeProvider.GetUtcNow()),
                cancellationToken);
    }

    public async Task<CurrentSession?> GetCurrentSessionAsync(
        Guid userId,
        Guid deviceId,
        Guid sessionFamilyId,
        CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);
        var device = await dbContext.Devices.AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == deviceId && candidate.UserId == userId,
                cancellationToken);
        if (user is null || user.DisabledAtUtc is not null || device is null || device.RevokedAtUtc is not null)
        {
            return null;
        }

        var familySessions = await dbContext.RefreshSessions.AsNoTracking()
            .Where(session => session.UserId == userId &&
                              session.DeviceId == deviceId &&
                              session.FamilyId == sessionFamilyId)
            .ToListAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (!familySessions.Any(session => session.RevokedAtUtc is null &&
                                           session.IdleExpiresAtUtc > now &&
                                           session.AbsoluteExpiresAtUtc > now))
        {
            return null;
        }

        return new CurrentSession(user.Id, user.Username, device.Id, device.Label, sessionFamilyId);
    }

    public async Task<bool> RevokeDeviceAsync(
        Guid userId,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        var device = await dbContext.Devices.SingleOrDefaultAsync(
            candidate => candidate.Id == deviceId && candidate.UserId == userId,
            cancellationToken);
        if (device is null)
        {
            return false;
        }

        var now = timeProvider.GetUtcNow();
        device.RevokedAtUtc ??= now;
        await dbContext.RefreshSessions
            .Where(session => session.UserId == userId &&
                              session.DeviceId == deviceId &&
                              session.RevokedAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(session => session.RevokedAtUtc, now),
                cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private AuthTokens CreateAuthTokens(
        UserAccount user,
        RegisteredDevice device,
        IssuedRefreshSession refresh)
    {
        var access = accessTokenIssuer.Issue(user.Id, user.Username, device.Id, refresh.Session.FamilyId);
        return new AuthTokens(
            access.Value,
            access.ExpiresAtUtc,
            refresh.RawToken,
            refresh.Session.IdleExpiresAtUtc,
            refresh.Session.AbsoluteExpiresAtUtc,
            new AuthDevice(device.Id, device.Label));
    }

    private IssuedRefreshSession CreateRefreshSession(
        Guid userId,
        Guid deviceId,
        Guid familyId,
        DateTimeOffset now,
        DateTimeOffset? absoluteExpiry)
    {
        var rawToken = GenerateRefreshToken();
        var absolute = absoluteExpiry ?? now.AddDays(_options.RefreshTokenAbsoluteDays);
        var idle = now.AddDays(_options.RefreshTokenIdleDays);
        if (idle > absolute)
        {
            idle = absolute;
        }

        return new IssuedRefreshSession(
            rawToken,
            new RefreshSession
            {
                Id = Guid.NewGuid(),
                FamilyId = familyId,
                UserId = userId,
                DeviceId = deviceId,
                RefreshTokenHash = HashRefreshToken(rawToken),
                CreatedAtUtc = now,
                LastUsedAtUtc = now,
                IdleExpiresAtUtc = idle,
                AbsoluteExpiresAtUtc = absolute
            });
    }

    private Task RevokeUserSessionsAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        dbContext.RefreshSessions
            .Where(session => session.UserId == userId && session.RevokedAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(session => session.RevokedAtUtc, now),
                cancellationToken);

    private Task RevokeFamilyAsync(
        Guid familyId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        dbContext.RefreshSessions
            .Where(session => session.FamilyId == familyId && session.RevokedAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(session => session.RevokedAtUtc, now),
                cancellationToken);

    private static string GenerateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string HashRefreshToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string? NormalizeUsername(string username)
    {
        var trimmed = username.Trim();
        return UsernamePattern().IsMatch(trimmed) ? trimmed.ToUpperInvariant() : null;
    }

    private static bool IsValidPassword(string password) => password.Length is >= 12 and <= 256;

    private static string NormalizeDeviceLabel(string? label, string fallback = "Android device")
    {
        var normalized = string.IsNullOrWhiteSpace(label) ? fallback : label.Trim();
        return normalized.Length <= 100 ? normalized : normalized[..100];
    }

    [GeneratedRegex("^[A-Za-z0-9._-]{3,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex UsernamePattern();

    private sealed record IssuedRefreshSession(string RawToken, RefreshSession Session);
}

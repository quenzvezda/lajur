namespace Lajur.Application.Auth;

public sealed record LoginCommand(
    string Username,
    string Password,
    Guid? DeviceId,
    string? DeviceLabel);

public sealed record AuthTokens(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTimeOffset RefreshTokenIdleExpiresAtUtc,
    DateTimeOffset RefreshTokenAbsoluteExpiresAtUtc,
    AuthDevice Device);

public sealed record AuthDevice(Guid Id, string Label);

public sealed record CurrentSession(
    Guid UserId,
    string Username,
    Guid DeviceId,
    string DeviceLabel,
    Guid SessionFamilyId);

public enum AuthFailure
{
    InvalidCredentials,
    InvalidRefreshToken,
    SessionRevoked,
    DeviceNotFound,
    UsernameAlreadyExists,
    UserNotFound,
    InvalidUsername,
    InvalidPassword
}

public sealed record AuthResult<T>(T? Value, AuthFailure? Failure)
{
    public bool Succeeded => Failure is null;

    public static AuthResult<T> Success(T value) => new(value, null);
    public static AuthResult<T> Fail(AuthFailure failure) => new(default, failure);
}

public interface IAuthService
{
    Task<AuthResult<AuthTokens>> LoginAsync(LoginCommand command, CancellationToken cancellationToken);
    Task<AuthResult<AuthTokens>> RefreshAsync(string refreshToken, CancellationToken cancellationToken);
    Task LogoutAsync(Guid userId, Guid sessionFamilyId, CancellationToken cancellationToken);
    Task<CurrentSession?> GetCurrentSessionAsync(
        Guid userId,
        Guid deviceId,
        Guid sessionFamilyId,
        CancellationToken cancellationToken);
    Task<bool> RevokeDeviceAsync(Guid userId, Guid deviceId, CancellationToken cancellationToken);
}

public interface IAccountAdministration
{
    Task<AuthResult<Guid>> CreateAsync(string username, string password, CancellationToken cancellationToken);
    Task<AuthResult<Guid>> ResetPasswordAsync(string username, string password, CancellationToken cancellationToken);
}

public interface IUserStoragePathResolver
{
    string GetUserRoot(Guid userId);
}

public sealed record IssuedAccessToken(string Value, DateTimeOffset ExpiresAtUtc);

public interface IAccessTokenIssuer
{
    IssuedAccessToken Issue(Guid userId, string username, Guid deviceId, Guid sessionFamilyId);
}

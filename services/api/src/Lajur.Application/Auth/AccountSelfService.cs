namespace Lajur.Application.Auth;

public sealed class RegistrationOptions
{
    public const string SectionName = "Lajur:Registration";
    public string Mode { get; set; } = "invite";
    public int MaxAccounts { get; set; } = 100;
}

public sealed record RegistrationPolicy(string Mode);
public sealed record RegisterAccount(string Username, string Password, string InvitationCode);
public sealed record RecoveryCodes(IReadOnlyList<string> Codes);
public sealed record RegistrationReceipt(string Username, IReadOnlyList<string> Codes);
public sealed record InvitationReceipt(string Code, DateTimeOffset ExpiresAtUtc);
public interface IAccountSelfService
{
    RegistrationPolicy Policy { get; }
    Task<AuthResult<RegistrationReceipt>> RegisterAsync(RegisterAccount command, CancellationToken cancellationToken);
    Task<bool> RecoverAsync(string username, string code, string newPassword, CancellationToken cancellationToken);
    Task<bool> ChangePasswordAsync(Guid userId, string password, string newPassword, CancellationToken cancellationToken);
    Task<AuthResult<RecoveryCodes>> RegenerateCodesAsync(Guid userId, string password, CancellationToken cancellationToken);
    Task<AuthResult<InvitationReceipt>> InviteAsync(Guid userId, string password, CancellationToken cancellationToken);
}

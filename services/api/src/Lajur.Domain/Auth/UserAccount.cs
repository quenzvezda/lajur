namespace Lajur.Domain.Auth;

public sealed class UserAccount
{
    public Guid Id { get; set; }
    public required string Username { get; set; }
    public required string NormalizedUsername { get; set; }
    public required string PasswordHash { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? DisabledAtUtc { get; set; }
    public List<RegisteredDevice> Devices { get; set; } = [];
    public List<RefreshSession> RefreshSessions { get; set; } = [];
}

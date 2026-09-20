namespace Lajur.Domain.Auth;

public sealed class RegisteredDevice
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string Label { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset LastSeenAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
    public UserAccount User { get; set; } = null!;
    public List<RefreshSession> RefreshSessions { get; set; } = [];
}

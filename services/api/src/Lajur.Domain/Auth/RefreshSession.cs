namespace Lajur.Domain.Auth;

public sealed class RefreshSession
{
    public Guid Id { get; set; }
    public Guid FamilyId { get; set; }
    public Guid UserId { get; set; }
    public Guid DeviceId { get; set; }
    public required string RefreshTokenHash { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset LastUsedAtUtc { get; set; }
    public DateTimeOffset IdleExpiresAtUtc { get; set; }
    public DateTimeOffset AbsoluteExpiresAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
    public Guid? ReplacedBySessionId { get; set; }
    public UserAccount User { get; set; } = null!;
    public RegisteredDevice Device { get; set; } = null!;
}

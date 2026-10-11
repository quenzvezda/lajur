namespace Lajur.Domain.Auth;

public sealed class AccountRecoveryCode
{
    public required string Hash { get; set; }
    public Guid UserId { get; set; }
    public long CreatedAtUnix { get; set; }
    public long? UsedAtUnix { get; set; }
}

public sealed class AccountInvitation
{
    public required string Hash { get; set; }
    public Guid CreatedBy { get; set; }
    public long ExpiresAtUnix { get; set; }
    public long? UsedAtUnix { get; set; }
}

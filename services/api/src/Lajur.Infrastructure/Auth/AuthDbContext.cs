using Lajur.Domain.Auth;
using Microsoft.EntityFrameworkCore;

namespace Lajur.Infrastructure.Auth;

public sealed class AuthDbContext(DbContextOptions<AuthDbContext> options) : DbContext(options)
{
    public DbSet<UserAccount> Users => Set<UserAccount>();
    public DbSet<RegisteredDevice> Devices => Set<RegisteredDevice>();
    public DbSet<RefreshSession> RefreshSessions => Set<RefreshSession>();
    public DbSet<AccountRecoveryCode> RecoveryCodes => Set<AccountRecoveryCode>();
    public DbSet<AccountInvitation> Invitations => Set<AccountInvitation>();
    public DbSet<SelfRegisteredAccount> SelfRegisteredAccounts => Set<SelfRegisteredAccount>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var registrations = modelBuilder.Entity<SelfRegisteredAccount>();
        registrations.ToTable("SelfRegisteredAccounts");
        registrations.HasKey(account => account.UserId);
        registrations.HasOne<UserAccount>().WithOne().HasForeignKey<SelfRegisteredAccount>(account => account.UserId).OnDelete(DeleteBehavior.Cascade);
        var recovery = modelBuilder.Entity<AccountRecoveryCode>();
        recovery.ToTable("RecoveryCodes");
        recovery.HasKey(code => code.Hash);
        recovery.HasIndex(code => code.UserId);
        recovery.HasOne<UserAccount>().WithMany().HasForeignKey(code => code.UserId).OnDelete(DeleteBehavior.Cascade);
        var invitations = modelBuilder.Entity<AccountInvitation>();
        invitations.ToTable("AccountInvitations");
        invitations.HasKey(code => code.Hash);
        invitations.HasIndex(code => code.CreatedBy);
        invitations.HasOne<UserAccount>().WithMany().HasForeignKey(code => code.CreatedBy).OnDelete(DeleteBehavior.Cascade);
        var users = modelBuilder.Entity<UserAccount>();
        users.ToTable("Users");
        users.HasKey(user => user.Id);
        users.Property(user => user.Username).HasMaxLength(64).IsRequired();
        users.Property(user => user.NormalizedUsername).HasMaxLength(64).IsRequired();
        users.Property(user => user.PasswordHash).IsRequired();
        users.HasIndex(user => user.NormalizedUsername).IsUnique();

        var devices = modelBuilder.Entity<RegisteredDevice>();
        devices.ToTable("Devices");
        devices.HasKey(device => device.Id);
        devices.Property(device => device.Label).HasMaxLength(100).IsRequired();
        devices.HasIndex(device => new { device.UserId, device.RevokedAtUtc });
        devices.HasOne(device => device.User)
            .WithMany(user => user.Devices)
            .HasForeignKey(device => device.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        var sessions = modelBuilder.Entity<RefreshSession>();
        sessions.ToTable("RefreshSessions");
        sessions.HasKey(session => session.Id);
        sessions.Property(session => session.RefreshTokenHash).HasMaxLength(64).IsRequired();
        sessions.HasIndex(session => session.RefreshTokenHash).IsUnique();
        sessions.HasIndex(session => new { session.UserId, session.FamilyId });
        sessions.HasOne(session => session.User)
            .WithMany(user => user.RefreshSessions)
            .HasForeignKey(session => session.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        sessions.HasOne(session => session.Device)
            .WithMany(device => device.RefreshSessions)
            .HasForeignKey(session => session.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

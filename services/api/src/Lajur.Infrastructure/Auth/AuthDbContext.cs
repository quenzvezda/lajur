using Lajur.Domain.Auth;
using Microsoft.EntityFrameworkCore;

namespace Lajur.Infrastructure.Auth;

public sealed class AuthDbContext(DbContextOptions<AuthDbContext> options) : DbContext(options)
{
    public DbSet<UserAccount> Users => Set<UserAccount>();
    public DbSet<RegisteredDevice> Devices => Set<RegisteredDevice>();
    public DbSet<RefreshSession> RefreshSessions => Set<RefreshSession>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
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

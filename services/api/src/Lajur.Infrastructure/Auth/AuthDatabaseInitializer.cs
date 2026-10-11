using Microsoft.EntityFrameworkCore;

namespace Lajur.Infrastructure.Auth;

public sealed class AuthDatabaseInitializer(AuthDbContext dbContext)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        // Additive, transactional auth metadata upgrade. Never replaces Users,
        // existing sessions or workbook paths; old binaries ignore these tables.
        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            await dbContext.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS AuthSchemaVersions (Version INTEGER NOT NULL PRIMARY KEY);
                CREATE TABLE IF NOT EXISTS SelfRegisteredAccounts (
                    UserId TEXT NOT NULL PRIMARY KEY,
                    FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE CASCADE);
                CREATE TABLE IF NOT EXISTS RecoveryCodes (
                    Hash TEXT NOT NULL PRIMARY KEY, UserId TEXT NOT NULL,
                    CreatedAtUnix INTEGER NOT NULL, UsedAtUnix INTEGER NULL,
                    FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE CASCADE);
                CREATE INDEX IF NOT EXISTS IX_RecoveryCodes_UserId ON RecoveryCodes(UserId);
                CREATE TABLE IF NOT EXISTS AccountInvitations (
                    Hash TEXT NOT NULL PRIMARY KEY, CreatedBy TEXT NOT NULL,
                    ExpiresAtUnix INTEGER NOT NULL, UsedAtUnix INTEGER NULL,
                    FOREIGN KEY(CreatedBy) REFERENCES Users(Id) ON DELETE CASCADE);
                CREATE INDEX IF NOT EXISTS IX_AccountInvitations_CreatedBy ON AccountInvitations(CreatedBy);
                INSERT OR IGNORE INTO AuthSchemaVersions (Version) VALUES (1);
                """, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        await dbContext.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=ON;", cancellationToken);
    }
}

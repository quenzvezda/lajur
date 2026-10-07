using Lajur.Application.Auth;
using Lajur.Domain.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Lajur.Infrastructure.Auth;

public static class AuthServiceCollectionExtensions
{
    public static IServiceCollection AddLajurAuthPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<AuthOptions>()
            .Bind(configuration.GetSection(AuthOptions.SectionName))
            .Validate(options => options.AccessTokenMinutes is >= 1 and <= 60,
                "Access token lifetime must be between 1 and 60 minutes.")
            .Validate(options => options.RefreshTokenIdleDays >= 1,
                "Refresh idle lifetime must be at least one day.")
            .Validate(options => options.RefreshTokenAbsoluteDays >= options.RefreshTokenIdleDays,
                "Refresh absolute lifetime must not be shorter than idle lifetime.")
            .ValidateOnStart();
        services.AddOptions<StorageOptions>()
            .Bind(configuration.GetSection(StorageOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.RootPath),
                "Storage root path is required.")
            .ValidateOnStart();

        services.AddDbContext<AuthDbContext>((provider, options) =>
        {
            var storage = provider.GetRequiredService<IOptions<StorageOptions>>().Value;
            var authDirectory = Path.GetFullPath(Path.Combine(storage.RootPath, "auth"));
            Directory.CreateDirectory(authDirectory);
            var configuredConnection = provider.GetRequiredService<IConfiguration>().GetConnectionString("Auth");
            var connectionString = configuredConnection ??
                                   $"Data Source={Path.Combine(authDirectory, "lajur-auth.db")};Cache=Shared;Foreign Keys=True";
            options.UseSqlite(connectionString);
        });
        services.AddScoped<AuthDatabaseInitializer>();
        services.AddScoped<IPasswordHasher<UserAccount>, PasswordHasher<UserAccount>>();
        services.AddScoped<SqliteAuthService>();
        services.AddScoped<IAuthService>(provider => provider.GetRequiredService<SqliteAuthService>());
        services.AddScoped<IAccountAdministration>(provider => provider.GetRequiredService<SqliteAuthService>());
        services.AddScoped<IBrowserAuthService>(provider => provider.GetRequiredService<SqliteAuthService>());
        services.AddSingleton<IUserStoragePathResolver, UserStoragePathResolver>();
        services.AddSingleton(TimeProvider.System);
        return services;
    }
}

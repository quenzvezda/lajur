using Lajur.Application.Auth;
using Microsoft.Extensions.Options;

namespace Lajur.Infrastructure.Auth;

public sealed class UserStoragePathResolver(IOptions<StorageOptions> options) : IUserStoragePathResolver
{
    private readonly string _rootPath = Path.GetFullPath(options.Value.RootPath);

    public string GetUserRoot(Guid userId)
    {
        var usersRoot = Path.Combine(_rootPath, "data", "users");
        var userRoot = Path.GetFullPath(Path.Combine(usersRoot, userId.ToString("N")));
        var expectedPrefix = Path.GetFullPath(usersRoot) + Path.DirectorySeparatorChar;

        if (!userRoot.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Resolved user storage path escaped the configured root.");
        }

        return userRoot;
    }
}

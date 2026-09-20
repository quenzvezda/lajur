using System.Security.Cryptography;

namespace Lajur.Api.Auth;

public static class JwtSigningKeyStore
{
    public static byte[] LoadOrCreate(IConfiguration configuration)
    {
        var rootPath = configuration["Lajur:Storage:RootPath"] ?? ".local/lajur";
        var keysDirectory = Path.GetFullPath(Path.Combine(rootPath, "keys"));
        Directory.CreateDirectory(keysDirectory);
        var keyPath = Path.Combine(keysDirectory, "jwt-signing-key");

        if (File.Exists(keyPath))
        {
            return ReadKey(keyPath);
        }

        var generatedKey = RandomNumberGenerator.GetBytes(64);
        var temporaryPath = Path.Combine(keysDirectory, $"jwt-signing-key.{Guid.NewGuid():N}.tmp");
        File.WriteAllText(temporaryPath, Convert.ToBase64String(generatedKey));
        try
        {
            File.Move(temporaryPath, keyPath, overwrite: false);
            RestrictPermissions(keyPath);
            return generatedKey;
        }
        catch (IOException) when (File.Exists(keyPath))
        {
            File.Delete(temporaryPath);
            return ReadKey(keyPath);
        }
    }

    private static byte[] ReadKey(string keyPath)
    {
        var encoded = File.ReadAllText(keyPath).Trim();
        var key = Convert.FromBase64String(encoded);
        if (key.Length < 32)
        {
            throw new InvalidOperationException("JWT signing key must contain at least 256 bits.");
        }

        return key;
    }

    private static void RestrictPermissions(string keyPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(keyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}

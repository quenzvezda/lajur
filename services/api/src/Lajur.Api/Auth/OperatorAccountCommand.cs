using Lajur.Application.Auth;

namespace Lajur.Api.Auth;

public static class OperatorAccountCommand
{
    public static async Task<bool> TryRunAsync(
        string[] args,
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        if (args.Length < 2 || !string.Equals(args[0], "account", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var operation = args[1].ToLowerInvariant();
        if (operation is not ("create" or "reset"))
        {
            Console.Error.WriteLine("Usage: account <create|reset> --username <name>");
            Environment.ExitCode = 2;
            return true;
        }

        var username = ReadOption(args, "--username");
        if (string.IsNullOrWhiteSpace(username))
        {
            Console.Error.WriteLine("Missing required --username option.");
            Environment.ExitCode = 2;
            return true;
        }

        Console.Write("Password (minimum 12 characters): ");
        var password = ReadPassword();
        Console.WriteLine();

        await using var scope = services.CreateAsyncScope();
        var administration = scope.ServiceProvider.GetRequiredService<IAccountAdministration>();
        var result = operation == "create"
            ? await administration.CreateAsync(username, password, cancellationToken)
            : await administration.ResetPasswordAsync(username, password, cancellationToken);

        if (!result.Succeeded)
        {
            Console.Error.WriteLine($"Account command failed: {result.Failure}.");
            Environment.ExitCode = 1;
            return true;
        }

        Console.WriteLine($"Account '{username}' {operation} succeeded. User ID: {result.Value}");
        return true;
    }

    private static string? ReadOption(string[] args, string name)
    {
        var index = Array.FindIndex(args, argument =>
            string.Equals(argument, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static string ReadPassword()
    {
        if (Console.IsInputRedirected)
        {
            return (Console.ReadLine() ?? string.Empty).TrimEnd('\r', '\n');
        }

        var password = new List<char>();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                return new string(password.ToArray());
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (password.Count > 0)
                {
                    password.RemoveAt(password.Count - 1);
                }

                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                password.Add(key.KeyChar);
            }
        }
    }
}

namespace VibeDesk.Cli;

public static partial class Commands
{
    private static async Task<int> LoginAsync(string[] args, CancellationToken ct)
    {
        var config = CliConfig.Load();

        var email = args.FirstOrDefault(a => !a.StartsWith('-')) ?? Ask("Email: ");
        var password = AskSecret("Password: ");

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            Output.Error("Email and password are both required.");
            return 1;
        }

        using var api = new ApiClient(config with { ApiKey = null, AccessToken = null });

        var (token, expires, name) = await api.LoginAsync(email, password, ct);

        // The API key wins over the token in ApiClient, so a stale one left here would silently
        // outrank the credential just obtained.
        (config with { AccessToken = token, ExpiresAt = expires, UserName = name, ApiKey = null }).Save();

        Output.Success($"Signed in as {name}. The token expires {expires.ToLocalTime():g}.");
        Output.Hint("For unattended use, create an API key in Settings and run 'vibedesk config --key <key>'.");

        return 0;
    }

    private static int Logout()
    {
        var config = CliConfig.Load();

        (new CliConfig { Endpoint = config.Endpoint }).Save();

        Output.Success("Credential removed.");
        return 0;
    }

    private static int Config(string[] args)
    {
        var config = CliConfig.Load();
        var changed = false;

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--key" && i + 1 < args.Length)
            {
                // A key supersedes the token, and keeping the old token around would only confuse
                // the next person reading the file.
                config = config with { ApiKey = args[++i], AccessToken = null, ExpiresAt = null };
                changed = true;
            }
            else if (!args[i].StartsWith('-'))
            {
                config = config with { Endpoint = args[i].TrimEnd('/') };
                changed = true;
            }
        }

        if (changed)
        {
            config.Save();
            Output.Success("Saved.");
        }

        Output.Write($"Endpoint    {config.Endpoint}");
        Output.Write($"Credential  {(config.ApiKey is not null ? "API key" : config.TokenIsFresh ? $"token, valid to {config.ExpiresAt?.ToLocalTime():g}" : config.AccessToken is not null ? "token (expired)" : "none")}");
        Output.Muted(CliConfig.Path);

        return 0;
    }

    private static async Task<int> WhoAmIAsync(CancellationToken ct)
    {
        var config = CliConfig.Load();

        if (config.ApiKey is null && config.AccessToken is null)
        {
            Output.Error("Not signed in. Run 'vibedesk login'.");
            return 1;
        }

        using var api = new ApiClient(config);

        // Listing is the cheapest authenticated call, and it proves the credential end to end.
        var scripts = await api.ListAsync(ct);

        Output.Success($"{config.UserName ?? "Authenticated"} at {config.Endpoint} — {scripts.Count} script(s).");
        return 0;
    }

    private static string Ask(string prompt)
    {
        Console.Write(prompt);
        return Console.ReadLine()?.Trim() ?? string.Empty;
    }

    /// <summary>Reads without echoing, so a password never lands in a shared terminal's scrollback.</summary>
    private static string AskSecret(string prompt)
    {
        Console.Write(prompt);

        // A redirected stdin has no key events to read; the password is being piped in.
        if (Console.IsInputRedirected) return Console.ReadLine()?.Trim() ?? string.Empty;

        var buffer = new System.Text.StringBuilder();

        while (true)
        {
            var key = Console.ReadKey(intercept: true);

            if (key.Key == ConsoleKey.Enter) break;

            if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0) buffer.Length--;
                continue;
            }

            if (!char.IsControl(key.KeyChar)) buffer.Append(key.KeyChar);
        }

        Console.WriteLine();
        return buffer.ToString();
    }
}

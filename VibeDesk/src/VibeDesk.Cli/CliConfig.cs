using System.Text.Json;
using System.Text.Json.Serialization;

namespace VibeDesk.Cli;

/// <summary>
/// Where the CLI remembers which server it talks to and how it authenticates.
/// </summary>
/// <remarks>
/// Stored in the user's profile rather than the working directory, so a token never lands in a repo
/// by accident. An API key is preferred over a bearer token for anything scripted: tokens expire in
/// an hour, keys do not, and a key can be revoked on its own from Settings.
/// </remarks>
public sealed record CliConfig
{
    public string Endpoint { get; init; } = "http://localhost:5299";
    public string? AccessToken { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public string? ApiKey { get; init; }
    public string? UserName { get; init; }

    [JsonIgnore]
    public bool TokenIsFresh => AccessToken is not null && ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1);

    public static string Path { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".vibedesk", "cli.json");

    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static CliConfig Load()
    {
        try
        {
            return File.Exists(Path)
                ? JsonSerializer.Deserialize<CliConfig>(File.ReadAllText(Path), Json) ?? new CliConfig()
                : new CliConfig();
        }
        catch (Exception)
        {
            // A corrupt config should not stop `vibedesk login` from fixing it.
            return new CliConfig();
        }
    }

    public void Save()
    {
        var directory = System.IO.Path.GetDirectoryName(Path)!;
        Directory.CreateDirectory(directory);

        File.WriteAllText(Path, JsonSerializer.Serialize(this, Json));

        // Credentials, so no group or world access where the platform can express that.
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(Path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}

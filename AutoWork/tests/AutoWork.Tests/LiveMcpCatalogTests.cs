using AutoWork.Core.Configuration;
using AutoWork.Core.Security;
using AutoWork.Integrations.Mcp;

namespace AutoWork.Tests;

/// <summary>
/// Starts the catalogue entries for real.
///
/// Checking a package exists on npm proves it was published, not that it runs. These tests
/// launch each server that needs no credential, speak MCP to it, and require it to list at
/// least one tool — the difference between "the catalogue names real packages" and "the
/// catalogue offers things that work".
///
/// Entries needing a key the developer does not have (Firecrawl, Notion) and the remote proxy,
/// which needs a URL, are not covered and are recorded as such in Progress.md.
/// </summary>
public sealed class LiveMcpCatalogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "autowork-mcp-catalog", Guid.NewGuid().ToString("n")[..8]);

    public LiveMcpCatalogTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    /// <summary>
    /// The four that need nothing at all. If any stops working the catalogue is offering a dead
    /// entry, which is the failure this exists to catch.
    /// </summary>
    [Theory]
    [InlineData("memory")]
    [InlineData("sequential-thinking")]
    [InlineData("context7")]
    [InlineData("everything")]
    public async Task A_catalogue_entry_that_needs_no_credential_starts_and_offers_tools(string id)
    {
        SkipUnlessLive();

        var entry = McpCatalog.Find(id);
        Assert.NotNull(entry);

        var result = await ProbeAsync(McpCatalog.CreateSettings(entry, new Dictionary<string, string>()));

        Assert.True(result.Success, $"{entry.Name}: {result.Message}");
        Assert.True(result.ToolCount > 0, $"{entry.Name} connected but offered no tools.");
    }

    /// <summary>The filesystem server takes the folder it may touch as a positional argument.</summary>
    [Fact]
    public async Task The_filesystem_entry_starts_with_the_folder_it_was_given()
    {
        SkipUnlessLive();

        var entry = McpCatalog.Find("filesystem");
        Assert.NotNull(entry);

        var settings = McpCatalog.CreateSettings(entry, new Dictionary<string, string> { ["arg"] = _dir });

        Assert.Contains(_dir, settings.Arguments);

        var result = await ProbeAsync(settings);

        Assert.True(result.Success, result.Message);
        Assert.True(result.ToolCount > 0);
    }

    /// <summary>
    /// The entries added for the app and vendor catalogue that still need no credential.
    ///
    /// The OAuth ones — Figma, GitHub, Atlassian, Linear, Asana — open a browser to sign in and
    /// cannot be probed unattended; their addresses are checked against the vendor's own domain
    /// in <c>McpCatalogContentTests</c> instead.
    /// </summary>
    [Theory]
    [InlineData("microsoft-learn")]
    [InlineData("chrome-devtools")]
    public async Task A_newly_added_keyless_entry_starts_and_offers_tools(string id)
    {
        SkipUnlessLive();

        var entry = McpCatalog.Find(id);
        Assert.NotNull(entry);

        var result = await ProbeAsync(McpCatalog.CreateSettings(entry, new Dictionary<string, string>()));

        Assert.True(result.Success, $"{entry.Name}: {result.Message}");
        Assert.True(result.ToolCount > 0, $"{entry.Name} connected but offered no tools.");
    }

    /// <summary>
    /// The uv-based entries — AWS documentation, AWS knowledge, MarkItDown. Skipped where uv is
    /// not installed rather than reported as broken, because that is what it would be measuring.
    /// </summary>
    [Theory]
    [InlineData("aws-documentation")]
    [InlineData("aws-knowledge")]
    [InlineData("markitdown")]
    public async Task A_uv_based_entry_starts_where_uv_is_installed(string id)
    {
        SkipUnlessLive();
        Assert.SkipWhen(FindOnPath("uvx") is null, "uv is not installed on this machine. Skipped.");

        var entry = McpCatalog.Find(id);
        Assert.NotNull(entry);

        var result = await ProbeAsync(McpCatalog.CreateSettings(entry, new Dictionary<string, string>()));

        Assert.True(result.Success, $"{entry.Name}: {result.Message}");
        Assert.True(result.ToolCount > 0, $"{entry.Name} connected but offered no tools.");
    }

    private static string? FindOnPath(string command)
    {
        var extensions = OperatingSystem.IsWindows() ? new[] { ".exe", ".cmd", ".bat", "" } : [""];

        return (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator)
            .Where(directory => directory.Length > 0)
            .SelectMany(directory => extensions.Select(extension => Path.Combine(directory, command + extension)))
            .FirstOrDefault(File.Exists);
    }

    /// <summary>Tavily's entry needs a key, and the developer machine has one for the search tests.</summary>
    [Fact]
    public async Task The_tavily_entry_starts_when_given_its_key()
    {
        var key = Environment.GetEnvironmentVariable("AUTOWORK_LIVE_TAVILY_KEY");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(key), "Needs AUTOWORK_LIVE_TAVILY_KEY. Skipped.");

        var entry = McpCatalog.Find("tavily");
        Assert.NotNull(entry);

        var settings = McpCatalog.CreateSettings(entry, new Dictionary<string, string> { ["TAVILY_API_KEY"] = key! });

        var result = await ProbeAsync(settings);

        Assert.True(result.Success, result.Message);
        Assert.True(result.ToolCount > 0);
    }

    /// <summary>
    /// Playwright downloads a browser the first time it runs, which is minutes and hundreds of
    /// megabytes. Worth proving once, worth its own long budget, and worth being explicit that
    /// this is why it is separate from the others.
    /// </summary>
    [Fact]
    public async Task The_playwright_entry_starts_although_it_must_fetch_a_browser_first()
    {
        SkipUnlessLive();
        Assert.SkipWhen(Environment.GetEnvironmentVariable("AUTOWORK_LIVE_SLOW") is null,
            "Downloads a browser. Set AUTOWORK_LIVE_SLOW=1 to run. Skipped.");

        var entry = McpCatalog.Find("playwright");
        Assert.NotNull(entry);

        var result = await ProbeAsync(McpCatalog.CreateSettings(entry, new Dictionary<string, string>()));

        Assert.True(result.Success, result.Message);
        Assert.True(result.ToolCount > 0);
    }

    private static void SkipUnlessLive() =>
        Assert.SkipWhen(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AUTOWORK_LIVE_API_KEY")),
            "Starts external processes; runs with the live suite. Skipped.");

    private async Task<McpConnectionResult> ProbeAsync(McpServerSettings settings)
    {
        var config = new ConfigStore(Path.Combine(_dir, $"{Guid.NewGuid():n}.json"));
        config.Save(new AutoWorkConfig { McpServers = [settings] });

        await using var registry = new McpServerRegistry(config, new PassThroughSecrets());

        return await registry.ProbeAsync(settings, TestContext.Current.CancellationToken);
    }

    /// <summary>Values are supplied inline here, so nothing needs resolving.</summary>
    private sealed class PassThroughSecrets : ISecretStore
    {
        public string? Resolve(string? reference) => reference;
        public string? Get(string name) => null;
        public void Set(string name, string value) { }
        public void Delete(string name) { }
        public IReadOnlyCollection<string> Names => [];
    }
}

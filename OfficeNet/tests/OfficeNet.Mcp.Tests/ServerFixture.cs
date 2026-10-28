// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using OfficeNet.TestKit;
using Xunit;

namespace OfficeNet.Mcp.Tests;

/// <summary>
/// Starts the real server as a separate process and talks to it over stdio, exactly as Claude,
/// VS Code or any other MCP client does.
/// </summary>
/// <remarks>
/// Calling the tool classes directly would test the tools and miss everything around them: the
/// JSON schemas generated from the parameter types, argument binding, how exceptions become
/// error results, and whether anything stray is written to stdout and corrupts the stream. Those
/// are the failures a client actually sees.
/// </remarks>
public sealed class ServerFixture : IAsyncLifetime
{
    public TempDirectory Root { get; } = new();

    public McpClient Client { get; private set; } = null!;

    public async ValueTask InitializeAsync() => Client = await Start(Root.Path);

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
        Root.Dispose();
    }

    /// <summary>
    /// How long a call may take. A server that corrupts its own stdout does not fail a call — the
    /// client just waits for a response that never parses — so without a deadline that regression
    /// shows up as a test run that never ends instead of a failure.
    /// </summary>
    public static readonly TimeSpan Deadline = TimeSpan.FromSeconds(60);

    /// <summary>Launches a server on <paramref name="root"/> with extra arguments.</summary>
    public static Task<McpClient> Start(string root, params string[] extra) =>
        McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "officenet",
            Command = "dotnet",
            Arguments = [ServerPath(), "--root", root, .. extra],
        }), cancellationToken: new CancellationTokenSource(Deadline).Token);

    /// <summary>The server's own build output, in the configuration the tests were built in.</summary>
    public static string ServerPath()
    {
        var here = AppContext.BaseDirectory;
        var configuration = here.Contains($"{Path.DirectorySeparatorChar}Debug{Path.DirectorySeparatorChar}")
            ? "Debug"
            : "Release";

        var directory = new DirectoryInfo(here);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OfficeNet.sln")))
        {
            directory = directory.Parent;
        }

        var path = Path.Combine(directory?.FullName ?? throw new InvalidOperationException("OfficeNet.sln not found."),
            "src", "OfficeNet.Mcp", "bin", configuration, "net10.0", "Gravicode.OfficeNet.Mcp.dll");

        return File.Exists(path) ? path : throw new FileNotFoundException("Build the server first.", path);
    }
}

internal static class Results
{
    internal static string Text(this CallToolResult result) =>
        string.Join("\n", result.Content.OfType<TextContentBlock>().Select(t => t.Text));

    internal static CallToolResult Ok(this CallToolResult result)
    {
        Assert.False(result.IsError ?? false, "The tool reported an error: " + result.Text());
        return result;
    }
}

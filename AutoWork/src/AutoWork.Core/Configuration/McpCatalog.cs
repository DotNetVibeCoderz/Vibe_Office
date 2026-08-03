namespace AutoWork.Core.Configuration;

/// <summary>A parameter a catalogue entry needs before it can run.</summary>
public sealed record McpParameter
{
    public required string Key { get; init; }
    public required string Label { get; init; }

    /// <summary>Environment variable for a secret, or "arg" for a positional argument.</summary>
    public bool IsSecret { get; init; }

    public string Placeholder { get; init; } = "";
    public bool Required { get; init; } = true;
}

/// <summary>An MCP server the gallery offers, with everything needed to launch it.</summary>
public sealed record McpCatalogEntry
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }

    /// <summary>What the tools it adds are for, in the user's terms.</summary>
    public required string Category { get; init; }

    public McpTransport Transport { get; init; } = McpTransport.Stdio;

    public string Command { get; init; } = "npx";
    public IReadOnlyList<string> Arguments { get; init; } = [];
    public string Url { get; init; } = "";

    /// <summary>What the user must supply — an API key, a folder to expose.</summary>
    public IReadOnlyList<McpParameter> Parameters { get; init; } = [];

    /// <summary>What must already be installed, stated plainly so a failure is not a mystery.</summary>
    public string Requires { get; init; } = "Node.js (npx)";

    public string HomeUrl { get; init; } = "";
}

/// <summary>
/// The MCP servers offered out of the box.
///
/// Every entry was checked against its registry and none is deprecated — a gallery that offers
/// abandoned packages wastes the user's time in a way that looks like the app is broken. The
/// official servers that *are* deprecated (github, slack, postgres, brave-search) are
/// deliberately absent rather than listed with a warning.
///
/// This is a starting point, not a whitelist: the gallery lets any command or URL be added by
/// hand, because the interesting MCP server is usually the company's own.
/// </summary>
public static class McpCatalog
{
    public static IReadOnlyList<McpCatalogEntry> All { get; } =
    [
        new()
        {
            Id = "filesystem",
            Name = "Filesystem",
            Category = "Files",
            Description = "Read and write files in folders you name here. Useful when you want a " +
                          "different boundary from AutoWork's own granted folders.",
            Arguments = ["-y", "@modelcontextprotocol/server-filesystem"],
            Parameters =
            [
                new() { Key = "arg", Label = "Folder the server may access", Placeholder = @"C:\Users\you\Projects" },
            ],
            HomeUrl = "https://github.com/modelcontextprotocol/servers",
        },
        new()
        {
            Id = "memory",
            Name = "Memory",
            Category = "Memory",
            Description = "A knowledge graph the agent can write to and read back across runs.",
            Arguments = ["-y", "@modelcontextprotocol/server-memory"],
            HomeUrl = "https://github.com/modelcontextprotocol/servers",
        },
        new()
        {
            Id = "sequential-thinking",
            Name = "Sequential Thinking",
            Category = "Reasoning",
            Description = "Gives the model a scratchpad for working a hard problem through in steps.",
            Arguments = ["-y", "@modelcontextprotocol/server-sequential-thinking"],
            HomeUrl = "https://github.com/modelcontextprotocol/servers",
        },
        new()
        {
            Id = "playwright",
            Name = "Playwright Browser",
            Category = "Web",
            Description = "Drives a real browser: navigate, click, fill forms, read the page. The " +
                          "web automation AutoWork's own web_fetch cannot do.",
            Arguments = ["-y", "@playwright/mcp@latest"],
            Requires = "Node.js (npx). Downloads a browser on first use.",
            HomeUrl = "https://github.com/microsoft/playwright-mcp",
        },
        new()
        {
            Id = "context7",
            Name = "Context7 Docs",
            Category = "Reference",
            Description = "Up-to-date documentation and code examples for public libraries, fetched " +
                          "on demand instead of recalled from training data.",
            Arguments = ["-y", "@upstash/context7-mcp"],
            HomeUrl = "https://github.com/upstash/context7",
        },
        new()
        {
            Id = "tavily",
            Name = "Tavily Search",
            Category = "Web",
            Description = "Web search and page extraction built for models. The same service " +
                          "AutoWork's own web_search can use, with more tools around it.",
            Arguments = ["-y", "tavily-mcp"],
            Parameters =
            [
                new() { Key = "TAVILY_API_KEY", Label = "Tavily API key", IsSecret = true, Placeholder = "tvly-…" },
            ],
            HomeUrl = "https://github.com/tavily-ai/tavily-mcp",
        },
        new()
        {
            Id = "firecrawl",
            Name = "Firecrawl",
            Category = "Web",
            Description = "Scrapes and crawls websites into clean Markdown, including pages that " +
                          "need JavaScript to render.",
            Arguments = ["-y", "firecrawl-mcp"],
            Parameters =
            [
                new() { Key = "FIRECRAWL_API_KEY", Label = "Firecrawl API key", IsSecret = true, Placeholder = "fc-…" },
            ],
            HomeUrl = "https://github.com/firecrawl/firecrawl-mcp-server",
        },
        new()
        {
            Id = "notion",
            Name = "Notion",
            Category = "Productivity",
            Description = "Search, read and update Notion pages and databases. Notion's own server.",
            Arguments = ["-y", "@notionhq/notion-mcp-server"],
            Parameters =
            [
                new() { Key = "NOTION_TOKEN", Label = "Notion integration token", IsSecret = true, Placeholder = "ntn_…" },
            ],
            HomeUrl = "https://github.com/makenotion/notion-mcp-server",
        },
        new()
        {
            Id = "everything",
            Name = "Everything (reference server)",
            Category = "Testing",
            Description = "The protocol's own demo server. Useful for checking that MCP works on " +
                          "this machine before trusting a real one.",
            Arguments = ["-y", "@modelcontextprotocol/server-everything"],
            HomeUrl = "https://github.com/modelcontextprotocol/servers",
        },
        new()
        {
            Id = "remote",
            Name = "Remote server (via mcp-remote)",
            Category = "Custom",
            Description = "Connects to a hosted MCP server that speaks HTTP, handling the OAuth " +
                          "dance for you. Point it at the vendor's documented URL.",
            Arguments = ["-y", "mcp-remote"],
            Parameters =
            [
                new() { Key = "arg", Label = "Server URL", Placeholder = "https://example.com/mcp" },
            ],
            HomeUrl = "https://github.com/geelen/mcp-remote",
        },
    ];

    public static McpCatalogEntry? Find(string id) =>
        All.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Turns a catalogue entry plus the user's answers into a configured server.</summary>
    public static McpServerSettings CreateSettings(McpCatalogEntry entry, IReadOnlyDictionary<string, string> values)
    {
        var settings = new McpServerSettings
        {
            Name = entry.Name,
            Description = entry.Description,
            CatalogId = entry.Id,
            Transport = entry.Transport,
            Command = entry.Command,
            Arguments = [.. entry.Arguments],
            Url = entry.Url,
        };

        foreach (var parameter in entry.Parameters)
        {
            if (!values.TryGetValue(parameter.Key, out var value) || string.IsNullOrWhiteSpace(value)) continue;

            if (parameter.Key == "arg") settings.Arguments.Add(value.Trim());
            else settings.Environment[parameter.Key] = value.Trim();
        }

        return settings;
    }
}

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

    /// <summary>
    /// True when the vendor publishes this themselves. Shown in the gallery, because "Figma's own
    /// server" and "someone's Figma server" are very different things to hand your account to.
    /// </summary>
    public bool Official { get; init; } = true;

    /// <summary>Set when something must be installed or enabled outside AutoWork first.</summary>
    public string Setup { get; init; } = "";
}

/// <summary>Where to go looking for servers this catalogue does not carry.</summary>
public sealed record McpSource(string Name, string Url, string Description);

/// <summary>
/// Reading what a vendor's page tells you to run into something launchable.
///
/// Lives here rather than in the view model because it is parsing, not presentation — and
/// because getting it wrong produces a server that fails to start with an unhelpful message.
/// </summary>
public static class McpManualEntry
{
    /// <summary>
    /// Splits an argument line the way a shell would, honouring quotes. Paths under
    /// "Program Files" are common in vendor READMEs, and splitting on whitespace alone turns one
    /// argument into two broken ones.
    /// </summary>
    public static List<string> SplitArguments(string line)
    {
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        var quote = '\0';

        foreach (var c in line)
        {
            if (quote != '\0')
            {
                if (c == quote) quote = '\0';
                else current.Append(c);
            }
            else if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (char.IsWhiteSpace(c))
            {
                if (current.Length > 0) { parts.Add(current.ToString()); current.Clear(); }
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0) parts.Add(current.ToString());
        return parts;
    }

    /// <summary>One <c>NAME=value</c> per line, the shape every vendor README shows.</summary>
    public static IReadOnlyList<(string Key, string Value)> ParseEnvironment(string text)
    {
        var pairs = new List<(string, string)>();

        foreach (var raw in text.ReplaceLineEndings("\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var split = line.IndexOf('=');
            if (split <= 0) continue;

            var key = line[..split].Trim();
            var value = line[(split + 1)..].Trim().Trim('"');

            if (key.Length > 0 && value.Length > 0) pairs.Add((key, value));
        }

        return pairs;
    }
}

/// <summary>
/// The MCP servers offered out of the box.
///
/// Every entry was read off the vendor's own documentation, not off a directory listing or a
/// search result. That matters more here than anywhere else in the product: a catalogue entry is
/// an instruction to run somebody's code with the user's full rights, and a package name that is
/// merely plausible is exactly how a typosquat gets installed. Where a vendor publishes the
/// server themselves it is marked as official; the one that is not — Blender — says so.
///
/// Also checked for deprecation: a gallery that offers abandoned packages wastes the user's time
/// in a way that looks like the app is broken. The official servers that *are* deprecated
/// (slack, postgres, brave-search) are deliberately absent rather than listed with a warning.
///
/// Two things are deliberately missing. **Unreal Engine** has no canonical server — eight
/// competing community projects, each needing its own C++ plugin built, and picking one would be
/// blessing a stranger's repository. **Unity** ships an official integration, but it is an
/// in-Editor relay that writes the client configuration for you rather than a command anyone can
/// paste. Both are exactly what "add one by hand" is for.
///
/// This is a starting point, not a whitelist: any command or URL can be added by hand, because
/// the interesting MCP server is usually the company's own.
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
        // ── Design and creative ───────────────────────────────────────────────────────────

        new()
        {
            Id = "figma",
            Name = "Figma",
            Category = "Design",
            Description = "Read your Figma files: frames, components, variables and design tokens, " +
                          "so a design can be turned into code or documentation without screenshots.",
            Arguments = ["-y", "mcp-remote", "https://mcp.figma.com/mcp"],
            Requires = "Node.js (npx). Signs in through your browser on first use.",
            HomeUrl = "https://developers.figma.com/docs/figma-mcp-server/",
        },
        new()
        {
            Id = "canva",
            Name = "Canva",
            Category = "Design",
            Description = "Canva's own developer server, for building and working with Canva apps.",
            Arguments = ["-y", "@canva/cli@latest", "mcp"],
            HomeUrl = "https://www.canva.dev/docs/apps/mcp-server/",
        },
        new()
        {
            Id = "blender",
            Name = "Blender",
            Category = "3D",
            Description = "Drives Blender: build scenes, place and modify objects, set materials, " +
                          "and inspect what is in the file.",
            Command = "uvx",
            Arguments = ["blender-mcp"],
            Requires = "Python with uv (uvx), and Blender.",
            Setup = "Install addon.py from the project's repository through Blender's " +
                    "Edit › Preferences › Add-ons, then start the server from the BlenderMCP panel.",
            Official = false,
            HomeUrl = "https://github.com/ahujasid/blender-mcp",
        },

        // ── Developer platforms ───────────────────────────────────────────────────────────

        new()
        {
            Id = "github",
            Name = "GitHub",
            Category = "Development",
            Description = "GitHub's own server: repositories, issues, pull requests, code search " +
                          "and actions, against your real account.",
            Arguments = ["-y", "mcp-remote", "https://api.githubcopilot.com/mcp/"],
            Requires = "Node.js (npx). Signs in through your browser on first use.",
            HomeUrl = "https://github.com/github/github-mcp-server",
        },
        new()
        {
            Id = "atlassian",
            Name = "Atlassian (Jira, Confluence, Bitbucket)",
            Category = "Productivity",
            Description = "Atlassian's own server for Jira issues, Confluence pages, Jira Service " +
                          "Management, Bitbucket and Compass.",
            Arguments = ["-y", "mcp-remote", "https://mcp.atlassian.com/v1/mcp/authv2"],
            Requires = "Node.js (npx). Signs in through your browser on first use.",
            HomeUrl = "https://github.com/atlassian/atlassian-mcp-server",
        },
        new()
        {
            Id = "linear",
            Name = "Linear",
            Category = "Productivity",
            Description = "Linear's own server: read and update issues, projects and cycles.",
            Arguments = ["-y", "mcp-remote", "https://mcp.linear.app/sse"],
            Requires = "Node.js (npx). Signs in through your browser on first use.",
            HomeUrl = "https://linear.app/docs/mcp",
        },
        new()
        {
            Id = "asana",
            Name = "Asana",
            Category = "Productivity",
            Description = "Asana's own server: tasks, projects and portfolios.",
            Arguments = ["-y", "mcp-remote", "https://mcp.asana.com/sse"],
            Requires = "Node.js (npx). Signs in through your browser on first use.",
            HomeUrl = "https://developers.asana.com/docs/mcp-server",
        },
        new()
        {
            Id = "chrome-devtools",
            Name = "Chrome DevTools",
            Category = "Web",
            Description = "Inspect and control a live Chrome: performance traces, console, network " +
                          "and the DOM. Chrome's own server, published by the DevTools team.",
            Arguments = ["-y", "chrome-devtools-mcp@latest"],
            Requires = "Node.js (npx), and Chrome.",
            HomeUrl = "https://github.com/ChromeDevTools/chrome-devtools-mcp",
        },

        // ── Microsoft ─────────────────────────────────────────────────────────────────────

        new()
        {
            Id = "microsoft-learn",
            Name = "Microsoft Learn",
            Category = "Reference",
            Description = "Official Microsoft and Azure documentation, fetched live rather than " +
                          "recalled from training data. No account needed.",
            Transport = McpTransport.Http,
            Url = "https://learn.microsoft.com/api/mcp",
            Requires = "Nothing — it is a hosted service.",
            HomeUrl = "https://github.com/microsoft/mcp",
        },
        new()
        {
            Id = "azure-devops",
            Name = "Azure DevOps",
            Category = "Development",
            Description = "Microsoft's own server for Azure DevOps: work items, repositories, " +
                          "pipelines and pull requests.",
            Arguments = ["-y", "@azure-devops/mcp"],
            Parameters =
            [
                new() { Key = "arg", Label = "Azure DevOps organisation name", Placeholder = "contoso" },
            ],
            HomeUrl = "https://github.com/microsoft/mcp",
        },
        new()
        {
            Id = "markitdown",
            Name = "MarkItDown",
            Category = "Documents",
            Description = "Microsoft's converter for turning PDFs, Office files and web pages into " +
                          "Markdown. The same family as AutoWork's own document reading.",
            Command = "uvx",
            Arguments = ["markitdown-mcp"],
            Requires = "Python with uv (uvx).",
            HomeUrl = "https://github.com/microsoft/markitdown",
        },

        // ── AWS ───────────────────────────────────────────────────────────────────────────

        new()
        {
            Id = "aws-documentation",
            Name = "AWS Documentation",
            Category = "Reference",
            Description = "Search and read the official AWS documentation. No AWS account needed.",
            Command = "uvx",
            Arguments = ["awslabs.aws-documentation-mcp-server@latest"],
            Requires = "Python with uv (uvx).",
            HomeUrl = "https://github.com/awslabs/mcp",
        },
        new()
        {
            Id = "aws-knowledge",
            Name = "AWS Knowledge (hosted)",
            Category = "Reference",
            Description = "AWS's own hosted knowledge server — documentation, blog posts, " +
                          "architectural guidance and API references. No AWS account needed.",
            Command = "uvx",
            Arguments = ["mcp-proxy-for-aws@latest", "https://aws-mcp.us-east-1.api.aws/mcp"],
            Requires = "Python with uv (uvx).",
            HomeUrl = "https://github.com/awslabs/mcp",
        },

        // ── Anything else ─────────────────────────────────────────────────────────────────

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

    /// <summary>
    /// Where to find servers this catalogue does not carry.
    ///
    /// Offered as links rather than scraped into the list. A catalogue entry is an instruction to
    /// run somebody's code with the user's full rights, so every one above was read off the
    /// vendor's own page; pulling a directory in automatically would mean shipping whatever it
    /// happened to contain that day.
    /// </summary>
    public static IReadOnlyList<McpSource> Sources { get; } =
    [
        new("Official MCP servers", "https://github.com/modelcontextprotocol/servers",
            "The reference servers, plus a long list of community and vendor ones."),

        new("Microsoft", "https://github.com/microsoft/mcp",
            "Azure, Fabric, Dev Box, SQL, Dataverse, Microsoft 365 and more."),

        new("Google", "https://github.com/google/mcp",
            "Google Cloud databases, Workspace, Firebase, Maps and Chrome DevTools."),

        new("AWS", "https://github.com/awslabs/mcp",
            "Documentation, infrastructure-as-code, and one server per AWS service."),

        new("Remote servers", "https://mcpservers.org/remote-mcp-servers",
            "Hosted servers you connect to by URL, with no local install."),
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

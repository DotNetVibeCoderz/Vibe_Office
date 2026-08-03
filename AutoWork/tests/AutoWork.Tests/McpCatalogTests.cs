using AutoWork.Core.Configuration;

namespace AutoWork.Tests;

/// <summary>
/// The catalogue, checked for the things that make it safe to click "Add".
///
/// A catalogue entry is an instruction to run somebody's code with the user's full rights, so
/// these are about shape and honesty rather than about the servers themselves — whether the
/// entries actually start is `LiveMcpCatalogTests`.
/// </summary>
public sealed class McpCatalogContentTests
{
    [Fact]
    public void Every_entry_is_complete_enough_to_launch()
    {
        Assert.All(McpCatalog.All, entry =>
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Id), $"{entry.Name} has no id.");
            Assert.False(string.IsNullOrWhiteSpace(entry.Name), $"{entry.Id} has no name.");
            Assert.False(string.IsNullOrWhiteSpace(entry.Description), $"{entry.Id} has no description.");
            Assert.False(string.IsNullOrWhiteSpace(entry.Category), $"{entry.Id} has no category.");

            if (entry.Transport == McpTransport.Http)
            {
                Assert.True(Uri.TryCreate(entry.Url, UriKind.Absolute, out _), $"{entry.Id} has no usable URL.");
            }
            else
            {
                Assert.False(string.IsNullOrWhiteSpace(entry.Command), $"{entry.Id} has no command.");

                // Every stdio entry either carries its own arguments or asks the user for one.
                Assert.True(entry.Arguments.Count > 0 || entry.Parameters.Count > 0,
                    $"{entry.Id} would launch a bare command.");
            }
        });
    }

    /// <summary>
    /// Every entry has to say where it came from, because "is this really Figma's?" is the
    /// question a person should be able to answer before handing over their account.
    /// </summary>
    [Fact]
    public void Every_entry_links_to_the_page_it_was_taken_from()
    {
        Assert.All(McpCatalog.All, entry =>
            Assert.True(Uri.TryCreate(entry.HomeUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps,
                $"{entry.Id} has no https home page."));
    }

    /// <summary>An entry that is not the vendor's own must say so, and say what to install.</summary>
    [Fact]
    public void A_community_entry_is_labelled_and_explains_its_setup()
    {
        foreach (var entry in McpCatalog.All.Where(e => !e.Official))
            Assert.False(string.IsNullOrWhiteSpace(entry.Setup), $"{entry.Id} is community-made but explains no setup.");

        // Blender is the one that is not published by the vendor, and it is marked.
        Assert.False(McpCatalog.Find("blender")!.Official);
        Assert.True(McpCatalog.Find("figma")!.Official);
        Assert.True(McpCatalog.Find("github")!.Official);
    }

    /// <summary>
    /// The deprecated official servers stay out. Offering an abandoned package wastes the user's
    /// time in a way that looks like the app is broken.
    /// </summary>
    [Theory]
    [InlineData("server-slack")]
    [InlineData("server-postgres")]
    [InlineData("server-brave-search")]
    [InlineData("server-github")]
    public void Deprecated_packages_are_not_offered(string package)
    {
        Assert.DoesNotContain(McpCatalog.All,
            e => e.Arguments.Any(a => a.Contains(package, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// A hosted server is reached at the vendor's own domain. A lookalike would be a way to send
    /// someone through an OAuth flow at an address that is not theirs.
    /// </summary>
    [Theory]
    [InlineData("figma", "mcp.figma.com")]
    [InlineData("github", "api.githubcopilot.com")]
    [InlineData("atlassian", "mcp.atlassian.com")]
    [InlineData("linear", "mcp.linear.app")]
    [InlineData("asana", "mcp.asana.com")]
    [InlineData("microsoft-learn", "learn.microsoft.com")]
    [InlineData("aws-knowledge", "aws-mcp.us-east-1.api.aws")]
    public void A_hosted_entry_points_at_the_vendors_own_domain(string id, string host)
    {
        var entry = McpCatalog.Find(id);
        Assert.NotNull(entry);

        var candidates = entry.Arguments.Concat([entry.Url]);

        var url = candidates.FirstOrDefault(a => a.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(url);

        Assert.Equal(host, new Uri(url).Host);
    }

    [Fact]
    public void The_requested_vendors_are_all_present()
    {
        foreach (var id in new[] { "figma", "canva", "blender", "github", "microsoft-learn", "azure-devops", "aws-documentation" })
            Assert.NotNull(McpCatalog.Find(id));
    }

    /// <summary>Every collection link is somewhere a person can actually go and read.</summary>
    [Fact]
    public void The_further_sources_are_real_https_links()
    {
        Assert.NotEmpty(McpCatalog.Sources);

        Assert.All(McpCatalog.Sources, source =>
        {
            Assert.False(string.IsNullOrWhiteSpace(source.Name));
            Assert.False(string.IsNullOrWhiteSpace(source.Description));
            Assert.True(Uri.TryCreate(source.Url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps);
        });
    }

    [Fact]
    public void Answers_become_arguments_or_environment_variables_as_the_entry_declares()
    {
        var settings = McpCatalog.CreateSettings(
            McpCatalog.Find("azure-devops")!,
            new Dictionary<string, string> { ["arg"] = "contoso" });

        Assert.Equal(["-y", "@azure-devops/mcp", "contoso"], settings.Arguments);

        var keyed = McpCatalog.CreateSettings(
            McpCatalog.Find("tavily")!,
            new Dictionary<string, string> { ["TAVILY_API_KEY"] = "tvly-abc" });

        Assert.Equal("tvly-abc", keyed.Environment["TAVILY_API_KEY"]);
    }
}

/// <summary>
/// Adding a server by hand — the path most MCP servers actually need, since the catalogue can
/// only ever carry a fraction of them.
/// </summary>
public sealed class McpManualEntryTests
{
    [Fact]
    public void An_argument_line_is_split_the_way_a_shell_would()
    {
        Assert.Equal(["-y", "some-mcp-server", "--port", "8080"],
            McpManualEntry.SplitArguments("-y some-mcp-server --port 8080"));
    }

    /// <summary>
    /// A path from a vendor's README routinely has a space in it. Splitting on whitespace alone
    /// turns one argument into two broken ones and the server fails with something unhelpful.
    /// </summary>
    [Fact]
    public void A_quoted_path_with_spaces_stays_one_argument()
    {
        Assert.Equal(["--root", @"C:\Program Files\My App", "--verbose"],
            McpManualEntry.SplitArguments("--root \"C:\\Program Files\\My App\" --verbose"));
    }

    [Fact]
    public void An_empty_argument_line_yields_no_arguments()
    {
        Assert.Empty(McpManualEntry.SplitArguments("   "));
    }

    [Fact]
    public void Environment_variables_are_read_one_per_line()
    {
        var parsed = McpManualEntry.ParseEnvironment(
            """
            API_KEY=abc123
            REGION = eu-west-1
            QUOTED="with spaces"
            """);

        Assert.Equal(
            [("API_KEY", "abc123"), ("REGION", "eu-west-1"), ("QUOTED", "with spaces")],
            parsed);
    }

    [Fact]
    public void Blank_lines_comments_and_malformed_lines_are_ignored()
    {
        var parsed = McpManualEntry.ParseEnvironment(
            """
            # a comment

            GOOD=yes
            nonsense-without-an-equals
            =novalue
            EMPTY=
            """);

        Assert.Equal([("GOOD", "yes")], parsed);
    }
}

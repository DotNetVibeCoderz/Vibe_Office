using AutoWork.Core.Configuration;
using AutoWork.Core.Skills;
using AutoWork.Integrations.Skills;

namespace AutoWork.Tests;

public sealed class SkillManifestTests
{
    /// <summary>The shape every published skill uses, taken from a real one.</summary>
    [Fact]
    public void Front_matter_supplies_the_name_and_the_description()
    {
        var (name, description, body) = SkillManifest.Parse("""
            ---
            name: pdf
            description: Use this skill whenever the user wants to do anything with PDF files.
            license: Proprietary
            ---

            # PDF Processing Guide

            Read the file with pdfplumber.
            """, "fallback");

        Assert.Equal("pdf", name);
        Assert.StartsWith("Use this skill whenever", description);
        Assert.StartsWith("# PDF Processing Guide", body);
        Assert.DoesNotContain("license:", body);
    }

    /// <summary>
    /// Descriptions are long sentences and authors wrap them in a block scalar. Reading only the
    /// first line would put a truncated fragment in the system prompt, which is exactly the text
    /// the model uses to decide whether the skill applies.
    /// </summary>
    [Fact]
    public void A_folded_description_is_joined_back_into_one_line()
    {
        var (_, description, _) = SkillManifest.Parse("""
            ---
            name: brand-guidelines
            description: >-
              Apply the company's brand when producing any document,
              including colours, typography and tone of voice.
            ---

            Body here.
            """, "fallback");

        Assert.Equal(
            "Apply the company's brand when producing any document, including colours, typography and tone of voice.",
            description);
    }

    [Fact]
    public void Quoted_values_lose_their_quotes()
    {
        var (name, description, _) = SkillManifest.Parse("""
            ---
            name: "docx"
            description: 'Work with Word documents.'
            ---

            Body.
            """, "fallback");

        Assert.Equal("docx", name);
        Assert.Equal("Work with Word documents.", description);
    }

    /// <summary>
    /// A skill without front matter is still a usable skill. Refusing it would mean the gallery
    /// silently skips anything not written to the letter of the convention.
    /// </summary>
    [Fact]
    public void A_file_with_no_front_matter_falls_back_to_the_folder_name_and_first_line()
    {
        var (name, description, body) = SkillManifest.Parse("""
            # Debugging

            Reproduce the failure before changing anything.
            """, "systematic-debugging");

        Assert.Equal("systematic-debugging", name);
        Assert.Equal("Reproduce the failure before changing anything.", description);
        Assert.Contains("Reproduce the failure", body);
    }

    [Fact]
    public void Empty_input_does_not_throw()
    {
        var (name, _, body) = SkillManifest.Parse("", "empty");

        Assert.Equal("empty", name);
        Assert.Equal("", body);
    }
}

public sealed class SkillStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "autowork-skills", Guid.NewGuid().ToString("n")[..8]);
    private readonly FileSkillStore _store;

    public SkillStoreTests() => _store = new FileSkillStore(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void An_installed_skill_comes_back_with_its_body_intact()
    {
        _store.Install("---\nname: pdf\ndescription: Handle PDFs.\n---\n\nUse pdfplumber.", "anthropics/skills", "pdf");

        var skill = Assert.Single(_store.List());

        Assert.Equal("pdf", skill.Name);
        Assert.Equal("Handle PDFs.", skill.Description);
        Assert.Contains("pdfplumber", skill.Body);
        Assert.Equal("anthropics/skills", skill.Source);
    }

    [Fact]
    public void Installing_the_same_skill_again_replaces_it_rather_than_duplicating()
    {
        _store.Install("---\nname: pdf\ndescription: First.\n---\n\nOne.", "repo", "pdf");
        _store.Install("---\nname: pdf\ndescription: Second.\n---\n\nTwo.", "repo", "pdf");

        var skill = Assert.Single(_store.List());
        Assert.Equal("Second.", skill.Description);
    }

    [Fact]
    public void Removing_a_skill_removes_it()
    {
        var installed = _store.Install("---\nname: pdf\ndescription: d\n---\n\nBody.", "repo", "pdf");

        _store.Remove(installed.Id);

        Assert.Empty(_store.List());
    }

    /// <summary>
    /// The id becomes a folder name, and skill names come from files downloaded off the
    /// internet. A name carrying separators or dots must not be able to escape the directory.
    /// </summary>
    [Theory]
    [InlineData("../../etc/passwd", "etc-passwd")]
    [InlineData("..", "skill")]
    [InlineData("a/b\\c", "a-b-c")]
    [InlineData("  Spaced Name  ", "spaced-name")]
    [InlineData("", "skill")]
    public void An_id_derived_from_a_name_can_never_leave_its_folder(string name, string expected)
    {
        var id = FileSkillStore.ToId(name);

        Assert.Equal(expected, id);
        Assert.DoesNotContain(Path.DirectorySeparatorChar, id);
        Assert.DoesNotContain(Path.AltDirectorySeparatorChar, id);
        Assert.DoesNotContain("..", id);
    }

    [Fact]
    public void A_hostile_skill_name_writes_inside_the_store_and_nowhere_else()
    {
        _store.Install("---\nname: ../../escaped\ndescription: d\n---\n\nBody.", "repo", "x");

        var skill = Assert.Single(_store.List());

        Assert.True(Directory.Exists(Path.Combine(_dir, skill.Id)));
        Assert.False(File.Exists(Path.Combine(_dir, "..", "..", "escaped", "SKILL.md")));
    }
}

public sealed class SkillRepositoryTests
{
    [Theory]
    [InlineData("anthropics/skills", "anthropics", "skills")]
    [InlineData("  obra/superpowers  ", "obra", "superpowers")]
    [InlineData("https://github.com/anthropics/skills", "anthropics", "skills")]
    [InlineData("https://github.com/anthropics/skills.git", "anthropics", "skills")]
    public void Repositories_are_accepted_as_a_slug_or_a_url(string input, string owner, string name)
    {
        var repository = SkillRepository.Parse(input);

        Assert.NotNull(repository);
        Assert.Equal(owner, repository.Owner);
        Assert.Equal(name, repository.Name);
    }

    /// <summary>Only GitHub is browsed, so anything else has to be rejected rather than guessed at.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("skills")]
    [InlineData("https://gitlab.com/owner/repo")]
    public void Anything_that_is_not_a_github_repository_is_refused(string input) =>
        Assert.Null(SkillRepository.Parse(input));
}

public sealed class McpCatalogTests
{
    [Fact]
    public void Every_catalogue_entry_can_actually_be_launched()
    {
        Assert.NotEmpty(McpCatalog.All);

        foreach (var entry in McpCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Name), $"{entry.Id} has no name");
            Assert.False(string.IsNullOrWhiteSpace(entry.Description), $"{entry.Id} has no description");

            if (entry.Transport == McpTransport.Stdio)
                Assert.False(string.IsNullOrWhiteSpace(entry.Command), $"{entry.Id} has no command");
            else
                Assert.False(string.IsNullOrWhiteSpace(entry.Url), $"{entry.Id} has no URL");
        }
    }

    [Fact]
    public void Catalogue_ids_are_unique_so_added_state_cannot_be_ambiguous()
    {
        var ids = McpCatalog.All.Select(e => e.Id).ToArray();
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void A_secret_parameter_becomes_an_environment_variable_and_a_positional_one_an_argument()
    {
        var entry = McpCatalog.Find("tavily");
        Assert.NotNull(entry);

        var settings = McpCatalog.CreateSettings(entry, new Dictionary<string, string>
        {
            ["TAVILY_API_KEY"] = "tvly-test",
        });

        Assert.Equal("tvly-test", settings.Environment["TAVILY_API_KEY"]);
        Assert.Contains("tavily-mcp", settings.Arguments);

        var filesystem = McpCatalog.Find("filesystem");
        Assert.NotNull(filesystem);

        var withFolder = McpCatalog.CreateSettings(filesystem, new Dictionary<string, string>
        {
            ["arg"] = @"C:\Projects",
        });

        Assert.Equal(@"C:\Projects", withFolder.Arguments[^1]);
    }

    /// <summary>
    /// Adding a server writes a command line; it must not also start one. Enabling is the
    /// separate, deliberate step.
    /// </summary>
    [Fact]
    public void A_newly_created_server_is_not_enabled()
    {
        var entry = McpCatalog.Find("memory");
        Assert.NotNull(entry);

        Assert.False(McpCatalog.CreateSettings(entry, new Dictionary<string, string>()).Enabled);
    }

    [Fact]
    public void Mcp_is_off_until_the_permission_is_granted()
    {
        Assert.False(new AutoWork.Core.Security.PermissionPolicy().AllowMcpServers);
    }
}

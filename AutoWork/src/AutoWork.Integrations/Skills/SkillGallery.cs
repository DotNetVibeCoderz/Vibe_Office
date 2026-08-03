using System.Text.Json;
using AutoWork.Core.Skills;

namespace AutoWork.Integrations.Skills;

/// <summary>One skill found in a repository, before it is installed.</summary>
public sealed record SkillListing
{
    public required string Name { get; init; }
    public required string Description { get; init; }

    /// <summary>"anthropics/skills".</summary>
    public required string Repository { get; init; }

    /// <summary>Path to the SKILL.md inside the repository.</summary>
    public required string Path { get; init; }

    public string Source => $"{Repository} · {System.IO.Path.GetDirectoryName(Path)?.Replace('\\', '/')}";
}

/// <summary>A repository the gallery searches.</summary>
public sealed record SkillRepository(string Owner, string Name, string Branch = "main")
{
    public string FullName => $"{Owner}/{Name}";

    /// <summary>Parses "owner/name" or a GitHub URL. Returns null for anything else.</summary>
    public static SkillRepository? Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        var text = input.Trim();

        if (Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            if (!uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)) return null;
            text = uri.AbsolutePath.Trim('/');
        }

        var parts = text.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2) return null;

        return new SkillRepository(parts[0], parts[1].Replace(".git", "", StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Browses skills published as `SKILL.md` files in public GitHub repositories.
///
/// Skills are instructions the model will follow, so where they come from is a trust decision,
/// not a convenience. The gallery therefore never searches GitHub at large: it reads the
/// repositories the user has listed, seeded with two well-known ones. Adding a repository is an
/// explicit act, and the repository is always shown next to the skill so nobody installs
/// instructions without knowing whose they are.
///
/// Nothing is executed. A skill is text the model reads; it can call only the tools the
/// permission policy already allows, and installing one grants no new capability.
/// </summary>
public sealed class SkillGallery
{
    /// <summary>
    /// Seeded, not fixed. Both are widely used and public; the user can remove either and add
    /// their own — a company's internal skills repository is the obvious case.
    /// </summary>
    public static IReadOnlyList<SkillRepository> DefaultRepositories { get; } =
    [
        new("anthropics", "skills"),
        new("obra", "superpowers"),
    ];

    private const string ManifestFile = "SKILL.md";

    private readonly HttpClient _http;

    public SkillGallery(HttpClient? http = null)
    {
        _http = http ?? CreateClient();
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AutoWork/0.1 (+https://github.com/gravicode/autowork)");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    /// <summary>
    /// Lists every skill in a repository. A repository that cannot be read — renamed, private,
    /// rate-limited — yields nothing rather than failing the whole gallery.
    /// </summary>
    public async Task<IReadOnlyList<SkillListing>> BrowseAsync(
        SkillRepository repository, CancellationToken cancellationToken = default)
    {
        var paths = await FindManifestsAsync(repository, cancellationToken).ConfigureAwait(false);

        var listings = new List<SkillListing>();

        // Sequential on purpose: a burst of parallel requests to an unauthenticated GitHub is a
        // fast route to a 403, and a repository holds tens of skills, not thousands.
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var markdown = await ReadRawAsync(repository, path, cancellationToken).ConfigureAwait(false);
            if (markdown is null) continue;

            var folder = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "") ?? "skill";
            var (name, description, _) = SkillManifest.Parse(markdown, folder);

            listings.Add(new SkillListing
            {
                Name = name,
                Description = description,
                Repository = repository.FullName,
                Path = path,
            });
        }

        return listings.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>Fetches the full manifest so it can be installed.</summary>
    public Task<string?> DownloadAsync(SkillListing listing, CancellationToken cancellationToken = default)
    {
        var repository = SkillRepository.Parse(listing.Repository);
        return repository is null
            ? Task.FromResult<string?>(null)
            : ReadRawAsync(repository, listing.Path, cancellationToken);
    }

    private async Task<IReadOnlyList<string>> FindManifestsAsync(
        SkillRepository repository, CancellationToken cancellationToken)
    {
        try
        {
            var url = $"https://api.github.com/repos/{repository.Owner}/{repository.Name}" +
                      $"/git/trees/{repository.Branch}?recursive=1";

            using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return [];

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!document.RootElement.TryGetProperty("tree", out var tree) || tree.ValueKind != JsonValueKind.Array)
                return [];

            return tree.EnumerateArray()
                .Select(e => e.TryGetProperty("path", out var p) ? p.GetString() : null)
                .Where(p => p is not null && p.EndsWith(ManifestFile, StringComparison.OrdinalIgnoreCase))
                .Select(p => p!)
                .ToArray();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return []; }
    }

    private async Task<string?> ReadRawAsync(
        SkillRepository repository, string path, CancellationToken cancellationToken)
    {
        try
        {
            var url = $"https://raw.githubusercontent.com/{repository.Owner}/{repository.Name}" +
                      $"/{repository.Branch}/{path}";

            using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;

            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return null; }
    }
}

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

/// <summary>One file that ships alongside a skill's manifest: a template, a script, a schema.</summary>
public sealed record SkillFile(string RelativePath, byte[] Content);

/// <summary>A skill's manifest plus everything in its folder, and what had to be left behind.</summary>
public sealed record SkillBundle(string Markdown, IReadOnlyList<SkillFile> Files, IReadOnlyList<string> Notes);

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

    /// <summary>
    /// Bounds on what one skill may bring with it. A skill is a folder in someone else's
    /// repository, and nothing stops it holding a gigabyte of sample data — so the limits are
    /// generous enough for the real ones (`docx` ships 61 files of schemas and fonts) and finite
    /// anyway. Anything skipped is reported rather than dropped quietly.
    /// </summary>
    private const int MaxFiles = 250;
    private const long MaxFileBytes = 8L * 1024 * 1024;
    private const long MaxBundleBytes = 40L * 1024 * 1024;

    private readonly HttpClient _http;
    private readonly Dictionary<string, IReadOnlyList<TreeEntry>> _trees = new(StringComparer.OrdinalIgnoreCase);

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

    /// <summary>
    /// Fetches everything in the skill's folder, not just the manifest.
    ///
    /// Real skills are not one file. `anthropics/skills/pdf` ships eight Python scripts plus a
    /// reference and a forms guide, and its SKILL.md says in so many words "see REFERENCE.md".
    /// Installing the manifest alone leaves the model following instructions that point at files
    /// which are not there — worse than not installing it at all.
    /// </summary>
    public async Task<SkillBundle?> DownloadBundleAsync(
        SkillListing listing, CancellationToken cancellationToken = default)
    {
        var repository = SkillRepository.Parse(listing.Repository);
        if (repository is null) return null;

        var markdown = await ReadRawAsync(repository, listing.Path, cancellationToken).ConfigureAwait(false);
        if (markdown is null) return null;

        var folder = Folder(listing.Path);
        var entries = await ListFolderAsync(repository, folder, cancellationToken).ConfigureAwait(false);

        var files = new List<SkillFile>();
        var notes = new List<string>();
        var total = 0L;

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relative = entry.Path[folder.Length..].TrimStart('/');

            // The manifest is carried separately; everything else is a bundled resource.
            if (relative.Equals(ManifestFile, StringComparison.OrdinalIgnoreCase)) continue;

            if (files.Count >= MaxFiles)
            {
                notes.Add($"stopped after {MaxFiles} files");
                break;
            }

            if (entry.Size > MaxFileBytes)
            {
                notes.Add($"skipped {relative} ({Human(entry.Size)})");
                continue;
            }

            if (total + entry.Size > MaxBundleBytes)
            {
                notes.Add($"stopped at {Human(MaxBundleBytes)}");
                break;
            }

            var content = await ReadBytesAsync(repository, entry.Path, cancellationToken).ConfigureAwait(false);
            if (content is null)
            {
                notes.Add($"could not download {relative}");
                continue;
            }

            files.Add(new SkillFile(relative, content));
            total += content.Length;
        }

        return new SkillBundle(markdown, files, notes);
    }

    /// <summary>Folder holding the manifest, with a trailing slash. "" for a root-level SKILL.md.</summary>
    private static string Folder(string manifestPath)
    {
        var slash = manifestPath.LastIndexOf('/');
        return slash < 0 ? "" : manifestPath[..(slash + 1)];
    }

    private async Task<IReadOnlyList<TreeEntry>> ListFolderAsync(
        SkillRepository repository, string folder, CancellationToken cancellationToken)
    {
        var tree = await ReadTreeAsync(repository, cancellationToken).ConfigureAwait(false);

        return tree
            .Where(e => e.Path.StartsWith(folder, StringComparison.Ordinal))
            .OrderBy(e => e.Path, StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<IReadOnlyList<string>> FindManifestsAsync(
        SkillRepository repository, CancellationToken cancellationToken)
    {
        var tree = await ReadTreeAsync(repository, cancellationToken).ConfigureAwait(false);

        return tree
            .Where(e => e.Path.EndsWith(ManifestFile, StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Path)
            .ToArray();
    }

    private sealed record TreeEntry(string Path, long Size);

    /// <summary>
    /// The repository's file list, with sizes. Cached per repository for the life of the gallery:
    /// browsing then installing several skills would otherwise fetch the same tree each time, and
    /// unauthenticated GitHub does not take kindly to that.
    /// </summary>
    private async Task<IReadOnlyList<TreeEntry>> ReadTreeAsync(
        SkillRepository repository, CancellationToken cancellationToken)
    {
        if (_trees.TryGetValue(repository.FullName, out var cached)) return cached;

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

            var entries = tree.EnumerateArray()
                .Where(e => e.TryGetProperty("type", out var t) && t.GetString() == "blob")
                .Select(e => new TreeEntry(
                    e.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "",
                    e.TryGetProperty("size", out var s) && s.TryGetInt64(out var size) ? size : 0))
                .Where(e => e.Path.Length > 0)
                .ToArray();

            _trees[repository.FullName] = entries;
            return entries;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return []; }
    }

    private async Task<byte[]?> ReadBytesAsync(
        SkillRepository repository, string path, CancellationToken cancellationToken)
    {
        try
        {
            // Escaped per segment: the separators have to stay separators, but a skill folder
            // named with a space or a hash must not break the URL.
            var encoded = string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

            var url = $"https://raw.githubusercontent.com/{repository.Owner}/{repository.Name}" +
                      $"/{repository.Branch}/{encoded}";

            using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;

            return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return null; }
    }

    private static string Human(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / (1024.0 * 1024):0.#} MB",
    };

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

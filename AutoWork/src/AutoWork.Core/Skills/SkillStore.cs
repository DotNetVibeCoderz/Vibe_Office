using System.Text.RegularExpressions;

namespace AutoWork.Core.Skills;

/// <summary>
/// A skill the agent can call on: a name, a one-line description of when it applies, and a body
/// of instructions.
/// </summary>
public sealed record Skill
{
    /// <summary>Folder name on disk. Stable, filesystem-safe, unique per install.</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Body { get; init; }

    /// <summary>Where it came from — "anthropics/skills · skills/pdf", or "local".</summary>
    public string Source { get; init; } = "";

    public DateTimeOffset InstalledAt { get; init; } = DateTimeOffset.Now;
}

/// <summary>
/// Reads the open Agent Skills format: a `SKILL.md` whose YAML front matter carries the name and
/// the description, followed by the instructions.
///
/// The parser is deliberately narrow rather than a YAML library. Front matter here is two or
/// three scalar keys, and a full parser would invite the rest of YAML — anchors, includes, type
/// coercion — into a file downloaded from the internet. Anything it cannot read falls back to
/// the file name and the first heading, so an unusual skill degrades to a usable one.
/// </summary>
public static partial class SkillManifest
{
    public static (string Name, string Description, string Body) Parse(string markdown, string fallbackName)
    {
        var match = FrontMatter().Match(markdown ?? "");

        if (!match.Success)
            return (fallbackName, FirstParagraph(markdown ?? ""), (markdown ?? "").Trim());

        var front = match.Groups["front"].Value;
        var body = markdown![match.Length..].Trim();

        var name = Scalar(front, "name");
        var description = Scalar(front, "description");

        return (
            string.IsNullOrWhiteSpace(name) ? fallbackName : name,
            string.IsNullOrWhiteSpace(description) ? FirstParagraph(body) : description,
            body);
    }

    /// <summary>
    /// Reads one scalar. Handles the folded and literal block forms (<c>&gt;-</c>, <c>|</c>)
    /// because skill descriptions are long sentences and authors wrap them.
    /// </summary>
    private static string Scalar(string front, string key)
    {
        var lines = front.ReplaceLineEndings("\n").Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (!line.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase)) continue;

            var inline = line[(key.Length + 1)..].Trim();

            if (inline is not (">" or ">-" or "|" or "|-"))
                return Unquote(inline);

            // A block scalar: take the indented lines that follow.
            var collected = new List<string>();
            for (var j = i + 1; j < lines.Length; j++)
            {
                if (lines[j].Length > 0 && !char.IsWhiteSpace(lines[j][0])) break;
                collected.Add(lines[j].Trim());
            }

            return string.Join(" ", collected.Where(s => s.Length > 0)).Trim();
        }

        return "";
    }

    private static string Unquote(string value)
    {
        value = value.Trim();

        if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            return value[1..^1];

        return value;
    }

    /// <summary>Falls back to the first real paragraph, skipping headings.</summary>
    private static string FirstParagraph(string body)
    {
        foreach (var line in body.ReplaceLineEndings("\n").Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#') || trimmed.StartsWith("---")) continue;
            return trimmed.Length <= 300 ? trimmed : trimmed[..300];
        }

        return "";
    }

    [GeneratedRegex(@"\A---\s*\n(?<front>.*?)\n---\s*\n", RegexOptions.Singleline)]
    private static partial Regex FrontMatter();
}

public interface ISkillStore
{
    IReadOnlyList<Skill> List();
    Skill? Get(string id);

    /// <summary>Installs (or replaces) a skill. Returns what was stored.</summary>
    Skill Install(string markdown, string source, string fallbackName);

    void Remove(string id);

    event Action? Changed;
}

/// <summary>
/// Installed skills, one folder each under AutoWork's data directory.
///
/// The folder is the state — there is no separate index to fall out of step with it, the same
/// choice the knowledge store makes. That directory sits inside AutoWork's protected root, so
/// the agent cannot rewrite its own instructions through the file tools: installing a skill
/// stays a deliberate act in the Skills gallery.
/// </summary>
public sealed class FileSkillStore : ISkillStore
{
    private const string ManifestFile = "SKILL.md";
    private const string SourceFile = ".source";

    private readonly string _directory;
    private readonly Lock _gate = new();

    public FileSkillStore(string? directory = null)
    {
        _directory = directory ?? AppPaths.SkillsDirectory;
        Directory.CreateDirectory(_directory);
    }

    public event Action? Changed;

    public IReadOnlyList<Skill> List()
    {
        lock (_gate)
        {
            if (!Directory.Exists(_directory)) return [];

            return Directory.GetDirectories(_directory)
                .Select(Load)
                .Where(s => s is not null)
                .Select(s => s!)
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public Skill? Get(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        lock (_gate) return Load(Path.Combine(_directory, ToId(id)));
    }

    public Skill Install(string markdown, string source, string fallbackName)
    {
        var (name, description, body) = SkillManifest.Parse(markdown, fallbackName);
        var id = ToId(name);

        lock (_gate)
        {
            var folder = Path.Combine(_directory, id);
            Directory.CreateDirectory(folder);

            File.WriteAllText(Path.Combine(folder, ManifestFile), markdown);
            File.WriteAllText(Path.Combine(folder, SourceFile), source);
        }

        Changed?.Invoke();

        return new Skill { Id = id, Name = name, Description = description, Body = body, Source = source };
    }

    public void Remove(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return;

        lock (_gate)
        {
            var folder = Path.Combine(_directory, ToId(id));

            // Confirm it is one of ours before deleting: ToId already strips separators, so this
            // is belt and braces around a recursive delete.
            if (!folder.StartsWith(_directory, StringComparison.OrdinalIgnoreCase)) return;
            if (!Directory.Exists(folder)) return;

            Directory.Delete(folder, recursive: true);
        }

        Changed?.Invoke();
    }

    private Skill? Load(string folder)
    {
        var manifest = Path.Combine(folder, ManifestFile);
        if (!File.Exists(manifest)) return null;

        try
        {
            var markdown = File.ReadAllText(manifest);
            var id = Path.GetFileName(folder);
            var (name, description, body) = SkillManifest.Parse(markdown, id);

            var sourcePath = Path.Combine(folder, SourceFile);
            var source = File.Exists(sourcePath) ? File.ReadAllText(sourcePath).Trim() : "";

            return new Skill
            {
                Id = id,
                Name = name,
                Description = description,
                Body = body,
                Source = source,
                InstalledAt = Directory.GetCreationTime(folder),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // One unreadable skill must not hide the rest.
            return null;
        }
    }

    /// <summary>
    /// A skill's folder name, derived from its display name. Filesystem-safe by construction:
    /// it can never produce a path separator, a traversal, or an empty name.
    /// </summary>
    public static string ToId(string name)
    {
        var cleaned = new string(name.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray());

        while (cleaned.Contains("--", StringComparison.Ordinal))
            cleaned = cleaned.Replace("--", "-", StringComparison.Ordinal);

        cleaned = cleaned.Trim('-');

        return cleaned.Length == 0 ? "skill" : cleaned[..Math.Min(cleaned.Length, 60)];
    }
}

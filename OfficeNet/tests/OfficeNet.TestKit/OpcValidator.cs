// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.IO.Compression;
using System.Xml.Linq;

namespace OfficeNet.TestKit;

/// <summary>
/// Checks a saved OOXML package the way a consumer that is not this library would.
/// </summary>
/// <remarks>
/// <para>
/// This exists because a .docx that OfficeNet writes and OfficeNet reads proves nothing about
/// whether Word can open it. The validator deliberately shares no code with the library: it reads
/// the zip with <see cref="ZipArchive"/> and the XML with <see cref="XDocument"/>, and it re-derives
/// every rule from the specification rather than from how OfficeNet happens to implement it.
/// </para>
/// <para>
/// What it checks is the set of mistakes that produce a file which unzips cleanly and which Office
/// then reports as needing repair — an undeclared content type, a relationship pointing at a part
/// that is not there, children out of the schema's sequence. Those are invisible to a round trip
/// through the same library and are exactly what a second reader catches.
/// </para>
/// </remarks>
public static class OpcValidator
{
    private static readonly XNamespace ContentTypesNs =
        "http://schemas.openxmlformats.org/package/2006/content-types";

    private static readonly XNamespace RelationshipsNs =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    /// <summary>What the validator found.</summary>
    /// <param name="Problems">One line per defect; empty when the package is sound.</param>
    /// <param name="PartNames">Every zip entry the package holds.</param>
    public readonly record struct Report(IReadOnlyList<string> Problems, IReadOnlyList<string> PartNames)
    {
        /// <summary>True when nothing was wrong.</summary>
        public bool IsValid => Problems.Count == 0;

        /// <summary>A message listing every problem, for an assertion failure.</summary>
        public string Describe() =>
            IsValid
                ? $"package is valid ({PartNames.Count} parts)"
                : $"{Problems.Count} problem(s):\n  - " + string.Join("\n  - ", Problems);
    }

    /// <summary>Validates a package on disk.</summary>
    public static Report Validate(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Validate(File.ReadAllBytes(path));
    }

    /// <summary>Validates a package held in memory.</summary>
    public static Report Validate(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var problems = new List<string>();

        using var stream = new MemoryStream(bytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var entries = archive.Entries
            .Where(e => !e.FullName.EndsWith('/'))
            .ToDictionary(e => e.FullName, e => e, StringComparer.OrdinalIgnoreCase);

        var names = entries.Keys.ToList();

        if (!entries.TryGetValue("[Content_Types].xml", out var manifest))
        {
            problems.Add("[Content_Types].xml is missing, so the package is not a valid OPC container.");
            return new Report(problems, names);
        }

        // The manifest should be first so a streaming consumer meets it before any part.
        if (!string.Equals(archive.Entries[0].FullName, "[Content_Types].xml",
                StringComparison.OrdinalIgnoreCase))
        {
            problems.Add(
                $"[Content_Types].xml is entry {archive.Entries.ToList().FindIndex(e => e.FullName == "[Content_Types].xml") + 1}, " +
                "not the first entry. Consumers that stream rather than seek expect it first.");
        }

        var (defaults, overrides) = ReadContentTypes(Read(manifest), problems);

        CheckEveryPartHasAContentType(names, defaults, overrides, problems);
        CheckOverridesPointAtRealParts(names, overrides, problems);
        CheckRelationships(entries, names, problems);
        CheckXmlIsWellFormed(entries, defaults, overrides, problems);

        return new Report(problems, names);
    }

    private static byte[] Read(ZipArchiveEntry entry)
    {
        using var entryStream = entry.Open();
        using var buffer = new MemoryStream();
        entryStream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static (Dictionary<string, string> Defaults, Dictionary<string, string> Overrides)
        ReadContentTypes(byte[] data, List<string> problems)
    {
        var defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        XDocument document;

        try
        {
            document = XDocument.Load(new MemoryStream(data, writable: false));
        }
        catch (System.Xml.XmlException ex)
        {
            problems.Add($"[Content_Types].xml is not well-formed: {ex.Message}");
            return (defaults, overrides);
        }

        var root = document.Root;

        if (root is null || root.Name != ContentTypesNs + "Types")
        {
            problems.Add("[Content_Types].xml's root must be a Types element in the package content-types namespace.");
            return (defaults, overrides);
        }

        foreach (var element in root.Elements(ContentTypesNs + "Default"))
        {
            var extension = element.Attribute("Extension")?.Value;
            var type = element.Attribute("ContentType")?.Value;

            if (extension is null || type is null)
            {
                problems.Add("A Default entry is missing its Extension or ContentType.");
                continue;
            }

            if (!defaults.TryAdd(extension, type))
            {
                problems.Add($"Extension '{extension}' has more than one Default entry.");
            }
        }

        foreach (var element in root.Elements(ContentTypesNs + "Override"))
        {
            var partName = element.Attribute("PartName")?.Value;
            var type = element.Attribute("ContentType")?.Value;

            if (partName is null || type is null)
            {
                problems.Add("An Override entry is missing its PartName or ContentType.");
                continue;
            }

            if (!partName.StartsWith('/'))
            {
                problems.Add($"Override PartName '{partName}' must be absolute (start with '/').");
            }

            if (!overrides.TryAdd(partName, type))
            {
                problems.Add($"Part '{partName}' has more than one Override entry.");
            }
        }

        return (defaults, overrides);
    }

    private static void CheckEveryPartHasAContentType(IReadOnlyList<string> names,
        Dictionary<string, string> defaults, Dictionary<string, string> overrides, List<string> problems)
    {
        foreach (var name in names)
        {
            if (string.Equals(name, "[Content_Types].xml", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (overrides.ContainsKey("/" + name))
            {
                continue;
            }

            var lastDot = name.LastIndexOf('.');
            var lastSlash = name.LastIndexOf('/');
            var extension = lastDot > lastSlash ? name[(lastDot + 1)..] : string.Empty;

            if (extension.Length > 0 && defaults.ContainsKey(extension))
            {
                continue;
            }

            problems.Add(
                $"Part '{name}' has no content type: no Override for it and no Default for " +
                $"'{extension}'. Office reports this as an unreadable package.");
        }
    }

    private static void CheckOverridesPointAtRealParts(IReadOnlyList<string> names,
        Dictionary<string, string> overrides, List<string> problems)
    {
        var present = new HashSet<string>(names.Select(n => "/" + n), StringComparer.OrdinalIgnoreCase);

        foreach (var partName in overrides.Keys)
        {
            if (!present.Contains(partName))
            {
                problems.Add(
                    $"[Content_Types].xml declares an Override for '{partName}', which the package " +
                    "does not contain.");
            }
        }
    }

    private static void CheckRelationships(Dictionary<string, ZipArchiveEntry> entries,
        IReadOnlyList<string> names, List<string> problems)
    {
        var present = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);

        foreach (var name in names)
        {
            if (!name.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // A relationship part at "word/_rels/document.xml.rels" resolves its relative targets
            // against "word", not against "word/_rels".
            var relsFolder = name[..name.LastIndexOf('/')];
            var ownerFolder = relsFolder.EndsWith("_rels", StringComparison.OrdinalIgnoreCase)
                ? relsFolder[..Math.Max(0, relsFolder.LastIndexOf('/'))]
                : relsFolder;

            XDocument document;

            try
            {
                document = XDocument.Load(new MemoryStream(Read(entries[name]), writable: false));
            }
            catch (System.Xml.XmlException ex)
            {
                problems.Add($"'{name}' is not well-formed: {ex.Message}");
                continue;
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);

            foreach (var element in document.Root?.Elements(RelationshipsNs + "Relationship") ?? [])
            {
                var id = element.Attribute("Id")?.Value;
                var type = element.Attribute("Type")?.Value;
                var target = element.Attribute("Target")?.Value;

                if (id is null || type is null || target is null)
                {
                    problems.Add($"'{name}' has a Relationship missing Id, Type or Target.");
                    continue;
                }

                if (!ids.Add(id))
                {
                    problems.Add($"'{name}' uses relationship id '{id}' more than once.");
                }

                if (string.Equals(element.Attribute("TargetMode")?.Value, "External",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var resolved = Resolve(ownerFolder, target);

                if (!present.Contains(resolved))
                {
                    problems.Add(
                        $"'{name}' relationship '{id}' targets '{target}' (resolving to " +
                        $"'{resolved}'), which the package does not contain. Office reports a " +
                        "dangling relationship as unreadable content.");
                }
            }
        }
    }

    private static string Resolve(string folder, string target)
    {
        if (target.StartsWith('/'))
        {
            return target[1..];
        }

        var segments = new List<string>(
            folder.Split('/', StringSplitOptions.RemoveEmptyEntries));

        foreach (var segment in target.Replace('\\', '/').Split('/'))
        {
            switch (segment)
            {
                case "" or ".":
                    break;
                case "..":
                    if (segments.Count > 0)
                    {
                        segments.RemoveAt(segments.Count - 1);
                    }

                    break;
                default:
                    segments.Add(Uri.UnescapeDataString(segment));
                    break;
            }
        }

        return string.Join('/', segments);
    }

    private static void CheckXmlIsWellFormed(Dictionary<string, ZipArchiveEntry> entries,
        Dictionary<string, string> defaults, Dictionary<string, string> overrides, List<string> problems)
    {
        foreach (var (name, entry) in entries)
        {
            var type = overrides.GetValueOrDefault("/" + name);

            if (type is null)
            {
                var lastDot = name.LastIndexOf('.');
                var lastSlash = name.LastIndexOf('/');
                var extension = lastDot > lastSlash ? name[(lastDot + 1)..] : string.Empty;
                type = defaults.GetValueOrDefault(extension);
            }

            if (type is null || !type.Contains("xml", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                XDocument.Load(new MemoryStream(Read(entry), writable: false));
            }
            catch (System.Xml.XmlException ex)
            {
                problems.Add($"'{name}' declares an XML content type but is not well-formed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Checks that an element's children appear in the order a schema sequence requires.
    /// </summary>
    /// <param name="parent">The element to check.</param>
    /// <param name="order">The complete ordered list of local names the sequence allows.</param>
    /// <returns>A problem description, or <c>null</c> when the order is correct.</returns>
    /// <remarks>
    /// Names not in <paramref name="order"/> are ignored rather than reported, because a real file
    /// legitimately contains extension elements this list does not know about. Only the relative
    /// order of the names it does know is checked.
    /// </remarks>
    public static string? CheckChildOrder(XElement parent, params string[] order)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(order);

        var positions = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var i = 0; i < order.Length; i++)
        {
            positions[order[i]] = i;
        }

        var previous = -1;
        string? previousName = null;

        foreach (var child in parent.Elements())
        {
            if (!positions.TryGetValue(child.Name.LocalName, out var position))
            {
                continue;
            }

            if (position < previous)
            {
                return $"<{parent.Name.LocalName}> has <{child.Name.LocalName}> after " +
                       $"<{previousName}>, but the schema sequence requires the reverse order.";
            }

            previous = position;
            previousName = child.Name.LocalName;
        }

        return null;
    }
}

// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace OfficeNet.Core.Packaging;

/// <summary>
/// An Open Packaging Conventions container: the zip-plus-manifest structure that .docx, .xlsx and
/// .pptx all are.
/// </summary>
/// <remarks>
/// <para>
/// One implementation serves all three formats because the container is genuinely identical — the
/// formats differ only in which parts they put inside it. Everything below the part level
/// (relationship resolution, content-type lookup, media naming, metadata) is therefore written once
/// here and inherited by WordNet, ExcelNet and PowerPointNet.
/// </para>
/// <para>
/// The whole package is read into memory on open. Office documents are small enough for that to be
/// the right trade — it is what allows a part to be renamed, deleted, or rewritten without a
/// second pass, and what makes "save over the file you opened" safe. Parts stay compressed until
/// something asks for their content, so a package of mostly images costs little more than the
/// bytes on disk.
/// </para>
/// </remarks>
public sealed class OpcPackage : IDisposable
{
    private const string ContentTypesPartName = "/[Content_Types].xml";
    private static readonly XNamespace ContentTypesNs =
        "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace RelationshipsNs =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    private readonly Dictionary<OpcPartName, OpcPart> _parts = [];
    private readonly Dictionary<string, string> _defaultContentTypes =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<OpcPartName, string> _overrideContentTypes = [];
    private readonly Dictionary<OpcPartName, byte[]> _rawRelationshipParts = [];
    private List<OpcRelationship>? _rootRelationships;
    private int _nextRootRelationshipId = 1;
    private string? _originalPath;
    private bool _disposed;

    private OpcPackage()
    {
    }

    /// <summary>True when a part has been added, removed or modified since the package was opened.</summary>
    public bool IsDirty { get; private set; }

    /// <summary>Every part in the package, excluding relationship parts and the content-type manifest.</summary>
    public IReadOnlyCollection<OpcPart> Parts => _parts.Values;

    /// <summary>The path the package was opened from, when it was opened from a file.</summary>
    public string? Path => _originalPath;

    // ---- Construction --------------------------------------------------------------------------

    /// <summary>Creates an empty package with the minimum content-type defaults every OOXML file needs.</summary>
    public static OpcPackage Create()
    {
        var package = new OpcPackage();
        package._defaultContentTypes["rels"] = ContentTypes.Relationships;
        package._defaultContentTypes["xml"] = ContentTypes.Xml;
        package._rootRelationships = [];
        package.IsDirty = true;
        return package;
    }

    /// <summary>Opens a package from a file.</summary>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="OfficeNetException">The file is not a readable OPC package.</exception>
    public static OpcPackage Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Package not found: {path}", path);
        }

        // Read the whole file first, then close the handle. Holding the file open for the lifetime
        // of the package is what makes "open, edit, save to the same path" fail on Windows.
        var bytes = File.ReadAllBytes(path);
        using var stream = new MemoryStream(bytes, writable: false);
        var package = Open(stream);
        package._originalPath = System.IO.Path.GetFullPath(path);
        return package;
    }

    /// <summary>Opens a package from a stream. The stream is fully read and not retained.</summary>
    /// <exception cref="OfficeNetException">The stream is not a readable OPC package.</exception>
    public static OpcPackage Open(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var package = new OpcPackage();
        ZipArchive archive;
        try
        {
            archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException ex)
        {
            throw new OfficeNetException(
                "The stream is not a zip container, so it cannot be an OOXML package. A file saved " +
                "in the legacy binary format (.doc, .xls, .ppt) fails here — convert it first.", ex);
        }

        using (archive)
        {
            // Pass 1: the content-type manifest, which every other part's type comes from.
            var manifest = archive.GetEntry("[Content_Types].xml")
                ?? throw new OfficeNetException(
                    "The package has no [Content_Types].xml, so it is not a valid OPC container.");

            package.ReadContentTypes(ReadEntry(manifest));

            // Pass 2: every remaining entry.
            foreach (var entry in archive.Entries)
            {
                if (entry.FullName is "[Content_Types].xml")
                {
                    continue;
                }

                // A directory entry has an empty name after the trailing slash. Zip files written
                // by some producers include them; they are not parts.
                if (entry.FullName.EndsWith('/') || entry.Length == 0 && entry.Name.Length == 0)
                {
                    continue;
                }

                var name = new OpcPartName(entry.FullName);
                var data = ReadEntry(entry);

                if (name.IsRelationshipPart)
                {
                    package._rawRelationshipParts[name] = data;
                    continue;
                }

                var contentType = package.ResolveContentType(name);
                if (contentType is null)
                {
                    // An unregistered extension makes the package invalid per the specification,
                    // but real files contain them (thumbnails written by third-party tools, stray
                    // .DS_Store). Carrying the part through unchanged is friendlier than refusing
                    // to open the document, and the part is re-declared on save.
                    contentType = ContentTypes.ForExtension(name.Extension) ?? "application/octet-stream";
                    package._overrideContentTypes[name] = contentType;
                }

                package._parts[name] = new OpcPart(package, name, contentType, data);
            }
        }

        return package;
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        using var entryStream = entry.Open();
        // entry.Length is the uncompressed size from the central directory and is exact, so the
        // buffer is sized once rather than doubled repeatedly through a growing MemoryStream.
        var buffer = new MemoryStream(entry.Length > 0 && entry.Length < int.MaxValue ? (int)entry.Length : 4096);
        entryStream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private void ReadContentTypes(byte[] data)
    {
        var doc = XDocument.Load(new MemoryStream(data, writable: false), LoadOptions.PreserveWhitespace);
        var root = doc.Root
            ?? throw new OfficeNetException("[Content_Types].xml is empty.");

        foreach (var element in root.Elements(ContentTypesNs + "Default"))
        {
            var extension = element.Attribute("Extension")?.Value;
            var type = element.Attribute("ContentType")?.Value;
            if (extension is not null && type is not null)
            {
                _defaultContentTypes[extension] = type;
            }
        }

        foreach (var element in root.Elements(ContentTypesNs + "Override"))
        {
            var partName = element.Attribute("PartName")?.Value;
            var type = element.Attribute("ContentType")?.Value;
            if (partName is not null && type is not null)
            {
                _overrideContentTypes[new OpcPartName(partName)] = type;
            }
        }
    }

    private string? ResolveContentType(OpcPartName name) =>
        _overrideContentTypes.TryGetValue(name, out var over) ? over
        : _defaultContentTypes.TryGetValue(name.Extension, out var def) ? def
        : null;

    // ---- Parts ---------------------------------------------------------------------------------

    /// <summary>Looks a part up by name.</summary>
    public bool TryGetPart(OpcPartName name, out OpcPart part) => _parts.TryGetValue(name, out part!);

    /// <summary>Gets a part by name.</summary>
    /// <exception cref="OfficeNetException">No such part exists.</exception>
    public OpcPart GetPart(OpcPartName name) =>
        _parts.TryGetValue(name, out var part)
            ? part
            : throw new OfficeNetException($"The package has no part named '{name}'.");

    /// <summary>Gets a part by name, or <c>null</c>.</summary>
    public OpcPart? FindPart(OpcPartName name) => _parts.GetValueOrDefault(name);

    /// <summary>True when the package contains a part with this name.</summary>
    public bool ContainsPart(OpcPartName name) => _parts.ContainsKey(name);

    /// <summary>Every part with the given content type.</summary>
    public IEnumerable<OpcPart> PartsByContentType(string contentType) =>
        _parts.Values.Where(p => p.ContentType == contentType);

    /// <summary>Adds a part, registering its content type in the manifest.</summary>
    /// <exception cref="OfficeNetException">A part with that name already exists.</exception>
    public OpcPart AddPart(OpcPartName name, string contentType, byte[]? data = null)
    {
        if (_parts.ContainsKey(name))
        {
            throw new OfficeNetException($"The package already has a part named '{name}'.");
        }

        var part = new OpcPart(this, name, contentType, data ?? []);
        _parts[name] = part;
        RegisterContentType(name, contentType);
        IsDirty = true;
        return part;
    }

    /// <summary>Adds an XML part.</summary>
    public OpcPart AddXmlPart(OpcPartName name, string contentType, XDocument document)
    {
        var part = AddPart(name, contentType);
        part.Xml = document;
        return part;
    }

    /// <summary>
    /// Removes a part, its relationship part, and every relationship anywhere in the package that
    /// pointed at it.
    /// </summary>
    /// <remarks>
    /// Removing the dangling relationships is not optional tidiness. A relationship whose target
    /// part is missing is exactly the condition Word reports as "unreadable content", so deleting
    /// an image part without deleting its <c>r:embed</c> source breaks the document.
    /// </remarks>
    public bool RemovePart(OpcPartName name)
    {
        if (!_parts.Remove(name))
        {
            return false;
        }

        _overrideContentTypes.Remove(name);
        _rawRelationshipParts.Remove(name.RelationshipPartName);

        foreach (var part in _parts.Values)
        {
            var stale = part.RelationshipsForWriting()
                .Where(r => r.TargetMode == TargetMode.Internal && r.TargetPartName == name)
                .Select(r => r.Id)
                .ToList();

            foreach (var id in stale)
            {
                part.RemoveRelationship(id);
            }
        }

        var rootStale = RootRelationships()
            .Where(r => r.TargetMode == TargetMode.Internal && r.TargetPartName == name)
            .Select(r => r.Id)
            .ToList();

        foreach (var id in rootStale)
        {
            RemoveRootRelationship(id);
        }

        IsDirty = true;
        return true;
    }

    /// <summary>
    /// The first unused part name matching a template containing <c>{0}</c>, for example
    /// <c>/word/media/image{0}.png</c>.
    /// </summary>
    public OpcPartName NextPartName(string template)
    {
        for (var i = 1; ; i++)
        {
            var candidate = new OpcPartName(string.Format(template, i));
            if (!_parts.ContainsKey(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>Declares a content type for a part, as a Default when the extension allows it.</summary>
    public void RegisterContentType(OpcPartName name, string contentType)
    {
        var extension = name.Extension;

        // A Default entry covers every part with that extension and is what Office writes for
        // media. An Override is needed whenever two parts share an extension but not a type —
        // which .xml always does, so .xml never becomes a Default beyond the generic one.
        if (extension.Length > 0 && !extension.Equals("xml", StringComparison.OrdinalIgnoreCase))
        {
            if (_defaultContentTypes.TryGetValue(extension, out var existing))
            {
                if (existing == contentType)
                {
                    _overrideContentTypes.Remove(name);
                    return;
                }
            }
            else
            {
                _defaultContentTypes[extension] = contentType;
                _overrideContentTypes.Remove(name);
                return;
            }
        }

        _overrideContentTypes[name] = contentType;
    }

    // ---- Relationships -------------------------------------------------------------------------

    /// <summary>The package-level relationships, held in <c>/_rels/.rels</c>.</summary>
    public IReadOnlyList<OpcRelationship> RootRelationships()
    {
        if (_rootRelationships is not null)
        {
            return _rootRelationships;
        }

        _rootRelationships = ReadRelationshipsFor(new OpcPartName("/.rels-root-placeholder"), isRoot: true);
        foreach (var rel in _rootRelationships)
        {
            if (rel.Id.StartsWith("rId", StringComparison.Ordinal) &&
                int.TryParse(rel.Id.AsSpan(3), out var n) && n >= _nextRootRelationshipId)
            {
                _nextRootRelationshipId = n + 1;
            }
        }

        return _rootRelationships;
    }

    /// <summary>Adds a package-level relationship.</summary>
    public OpcRelationship AddRootRelationship(OpcPart target, string type)
    {
        RootRelationships();
        var id = "rId" + _nextRootRelationshipId++;
        var rel = new OpcRelationship(new OpcPartName("/x"), id, type, target.Name.Value.TrimStart('/'));
        _rootRelationships!.Add(rel);
        IsDirty = true;
        return rel;
    }

    /// <summary>Removes a package-level relationship by id.</summary>
    public bool RemoveRootRelationship(string id)
    {
        RootRelationships();
        var index = _rootRelationships!.FindIndex(r => r.Id == id);
        if (index < 0)
        {
            return false;
        }

        _rootRelationships.RemoveAt(index);
        IsDirty = true;
        return true;
    }

    /// <summary>The part a package-level relationship of the given type points at.</summary>
    public OpcPart? RootRelatedPart(string type)
    {
        var rel = RootRelationships().FirstOrDefault(r => r.Type == type);
        if (rel is null || rel.TargetMode == TargetMode.External)
        {
            return null;
        }

        return FindPart(rel.TargetPartName);
    }

    /// <summary>
    /// The main document part — <c>word/document.xml</c>, <c>xl/workbook.xml</c> or
    /// <c>ppt/presentation.xml</c> — found through the package relationship rather than by path.
    /// </summary>
    /// <remarks>
    /// Looking it up by relationship is not pedantry: a .docx produced by Google Docs names it
    /// <c>/word/document.xml</c>, one produced by some LibreOffice versions has been seen at other
    /// paths, and a .dotx names it the same but declares a different content type. The relationship
    /// is the only stable route.
    /// </remarks>
    public OpcPart? MainDocumentPart => RootRelatedPart(RelationshipTypes.OfficeDocument);

    internal List<OpcRelationship> ReadRelationshipsFor(OpcPartName partName, bool isRoot = false)
    {
        var relsName = isRoot ? new OpcPartName("/_rels/.rels") : partName.RelationshipPartName;
        var result = new List<OpcRelationship>();

        if (!_rawRelationshipParts.TryGetValue(relsName, out var data))
        {
            return result;
        }

        var doc = XDocument.Load(new MemoryStream(data, writable: false));
        var root = doc.Root;
        if (root is null)
        {
            return result;
        }

        // The source used for resolving relative targets is the owning part's directory. For root
        // relationships that is the package root, which OpcPartName models as a part sitting at "/".
        var source = isRoot ? new OpcPartName("/_root_") : partName;

        foreach (var element in root.Elements(RelationshipsNs + "Relationship"))
        {
            var id = element.Attribute("Id")?.Value;
            var type = element.Attribute("Type")?.Value;
            var target = element.Attribute("Target")?.Value;
            if (id is null || type is null || target is null)
            {
                continue;
            }

            var mode = string.Equals(element.Attribute("TargetMode")?.Value, "External",
                StringComparison.OrdinalIgnoreCase)
                ? TargetMode.External
                : TargetMode.Internal;

            result.Add(new OpcRelationship(source, id, type, target, mode));
        }

        return result;
    }

    /// <summary>
    /// Marks the package as changed.
    /// </summary>
    /// <remarks>
    /// Public because the format libraries live in their own assemblies and edit the XML trees
    /// in place; a document that mutated a part without marking the package would report
    /// <see cref="IsDirty"/> false with unsaved changes pending.
    /// </remarks>
    public void MarkDirty() => IsDirty = true;

    // ---- Saving --------------------------------------------------------------------------------

    /// <summary>Saves back to the file the package was opened from.</summary>
    /// <exception cref="InvalidOperationException">The package was not opened from a file.</exception>
    public void Save()
    {
        if (_originalPath is null)
        {
            throw new InvalidOperationException(
                "This package was not opened from a file; call Save(path) or Save(stream).");
        }

        Save(_originalPath);
    }

    /// <summary>Saves to a file.</summary>
    public void Save(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var full = System.IO.Path.GetFullPath(path);
        var directory = System.IO.Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(directory))
        {
            System.IO.Directory.CreateDirectory(directory);
        }

        // Write to a sibling temp file and move into place. A crash halfway through a direct write
        // leaves a truncated document where the original used to be, and the original is often the
        // only copy.
        var temp = full + ".officenet-tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                Save(stream);
            }

            File.Move(temp, full, overwrite: true);
        }
        catch
        {
            if (File.Exists(temp))
            {
                try
                {
                    File.Delete(temp);
                }
                catch (IOException)
                {
                    // The original write already failed; a failure to clean up is not the error
                    // worth reporting.
                }
            }

            throw;
        }

        _originalPath = full;
        IsDirty = false;
    }

    /// <summary>Saves to a stream.</summary>
    public void Save(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);

        // [Content_Types].xml must be the first entry for the package to be readable by consumers
        // that stream rather than seek — including some PDF-conversion services and older Office
        // for Mac builds. It costs nothing to guarantee.
        WriteEntry(archive, "[Content_Types].xml", BuildContentTypesXml(), CompressionLevel.Optimal);

        if (_rootRelationships is { Count: > 0 } || _rawRelationshipParts.ContainsKey("/_rels/.rels"))
        {
            var rels = _rootRelationships is not null
                ? BuildRelationshipsXml(_rootRelationships)
                : _rawRelationshipParts["/_rels/.rels"];
            WriteEntry(archive, "_rels/.rels", rels, CompressionLevel.Optimal);
        }

        foreach (var part in _parts.Values.OrderBy(p => p.Name.Value, StringComparer.Ordinal))
        {
            // Media is already compressed. Re-deflating a PNG or JPEG costs CPU proportional to
            // the file and reliably makes it a few bytes larger, so those parts are stored.
            var level = IsAlreadyCompressed(part.ContentType)
                ? CompressionLevel.NoCompression
                : CompressionLevel.Optimal;

            WriteEntry(archive, part.Name.ZipEntryName, part.GetBytes(), level);

            var relsName = part.Name.RelationshipPartName;
            byte[]? relsBytes = null;

            if (part.HasLoadedRelationships)
            {
                var relationships = part.RelationshipsForWriting();
                if (relationships.Count > 0)
                {
                    relsBytes = BuildRelationshipsXml(relationships);
                }
            }
            else if (_rawRelationshipParts.TryGetValue(relsName, out var raw))
            {
                relsBytes = raw;
            }

            if (relsBytes is not null)
            {
                WriteEntry(archive, relsName.ZipEntryName, relsBytes, CompressionLevel.Optimal);
            }
        }
    }

    private static bool IsAlreadyCompressed(string contentType) =>
        contentType.StartsWith("image/", StringComparison.Ordinal) &&
            !contentType.Contains("bmp", StringComparison.Ordinal) &&
            !contentType.Contains("svg", StringComparison.Ordinal) ||
        contentType.StartsWith("video/", StringComparison.Ordinal) ||
        contentType.StartsWith("audio/", StringComparison.Ordinal);

    private static void WriteEntry(ZipArchive archive, string name, byte[] data, CompressionLevel level)
    {
        var entry = archive.CreateEntry(name, level);
        // A fixed timestamp is not used: Office writes the real time and some validators warn on
        // the zip epoch. DateTimeOffset.Now matches what the applications do.
        using var entryStream = entry.Open();
        entryStream.Write(data, 0, data.Length);
    }

    private byte[] BuildContentTypesXml()
    {
        var root = new XElement(ContentTypesNs + "Types");

        foreach (var (extension, type) in _defaultContentTypes.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            root.Add(new XElement(ContentTypesNs + "Default",
                new XAttribute("Extension", extension),
                new XAttribute("ContentType", type)));
        }

        // Only declare an Override for a part that is actually present, otherwise the manifest
        // describes parts the package does not contain and strict readers reject it.
        foreach (var (name, type) in _overrideContentTypes
                     .Where(kv => _parts.ContainsKey(kv.Key))
                     .OrderBy(kv => kv.Key.Value, StringComparer.Ordinal))
        {
            root.Add(new XElement(ContentTypesNs + "Override",
                new XAttribute("PartName", name.Value),
                new XAttribute("ContentType", type)));
        }

        // Any part whose type is not covered by a Default needs an Override; xml parts always do.
        foreach (var part in _parts.Values.OrderBy(p => p.Name.Value, StringComparer.Ordinal))
        {
            if (_overrideContentTypes.ContainsKey(part.Name))
            {
                continue;
            }

            if (_defaultContentTypes.TryGetValue(part.Name.Extension, out var def) && def == part.ContentType)
            {
                continue;
            }

            root.Add(new XElement(ContentTypesNs + "Override",
                new XAttribute("PartName", part.Name.Value),
                new XAttribute("ContentType", part.ContentType)));
        }

        var doc = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), root);
        return OpcPart.SerializeXml(doc);
    }

    private static byte[] BuildRelationshipsXml(IReadOnlyList<OpcRelationship> relationships)
    {
        var root = new XElement(RelationshipsNs + "Relationships");

        foreach (var rel in relationships)
        {
            var element = new XElement(RelationshipsNs + "Relationship",
                new XAttribute("Id", rel.Id),
                new XAttribute("Type", rel.Type),
                new XAttribute("Target", rel.Target));

            if (rel.TargetMode == TargetMode.External)
            {
                element.Add(new XAttribute("TargetMode", "External"));
            }

            root.Add(element);
        }

        var doc = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), root);
        return OpcPart.SerializeXml(doc);
    }

    /// <summary>Serialises the package to a byte array.</summary>
    public byte[] ToArray()
    {
        using var buffer = new MemoryStream();
        Save(buffer);
        return buffer.ToArray();
    }

    /// <summary>Releases the package's in-memory content.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _parts.Clear();
        _rawRelationshipParts.Clear();
        _disposed = true;
    }

    public override string ToString() =>
        $"OpcPackage({_parts.Count} parts{(_originalPath is null ? "" : ", " + System.IO.Path.GetFileName(_originalPath))})";
}

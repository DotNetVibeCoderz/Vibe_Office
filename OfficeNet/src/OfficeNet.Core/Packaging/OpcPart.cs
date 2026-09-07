// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace OfficeNet.Core.Packaging;

/// <summary>
/// One stream inside an <see cref="OpcPackage"/>, together with its content type and the
/// relationships that start from it.
/// </summary>
/// <remarks>
/// <para>
/// A part holds either bytes or a parsed <see cref="XDocument"/>, never both as the source of
/// truth. <see cref="Xml"/> parses on first touch and from then on the tree is authoritative; the
/// byte buffer is dropped so a caller cannot mutate the tree and then serialise a stale buffer.
/// That single rule is what makes "open, edit one paragraph, save" preserve every part the library
/// does not understand — untouched parts keep their original bytes and are copied through
/// verbatim, including whatever namespace prefixes and extension elements the producer wrote.
/// </para>
/// </remarks>
public sealed class OpcPart
{
    private readonly OpcPackage _package;
    private byte[]? _bytes;
    private XDocument? _xml;
    private List<OpcRelationship>? _relationships;
    private int _nextRelationshipId = 1;

    internal OpcPart(OpcPackage package, OpcPartName name, string contentType, byte[] bytes)
    {
        _package = package;
        Name = name;
        ContentType = contentType;
        _bytes = bytes;
    }

    /// <summary>The absolute part name.</summary>
    public OpcPartName Name { get; internal set; }

    /// <summary>The declared MIME content type.</summary>
    public string ContentType { get; set; }

    /// <summary>The package this part belongs to.</summary>
    public OpcPackage Package => _package;

    /// <summary>True once the part has been parsed as XML, so saving must re-serialise it.</summary>
    public bool IsXmlLoaded => _xml is not null;

    /// <summary>
    /// The part's content as XML. Parsing is deferred to the first access and the resulting tree
    /// becomes the part's content — mutate it in place and the change is written on save.
    /// </summary>
    /// <exception cref="OfficeNetException">The part is not well-formed XML.</exception>
    public XDocument Xml
    {
        get
        {
            if (_xml is null)
            {
                var bytes = _bytes ?? [];
                try
                {
                    // Preserve whitespace: w:t and a:t are whitespace-significant, and dropping
                    // insignificant whitespace also drops the significant kind because the reader
                    // cannot tell them apart without the schema.
                    using var stream = new MemoryStream(bytes, writable: false);
                    _xml = XDocument.Load(stream, LoadOptions.PreserveWhitespace);
                }
                catch (XmlException ex)
                {
                    throw new OfficeNetException($"Part '{Name}' is not well-formed XML.", ex);
                }

                _bytes = null;
            }

            return _xml;
        }
        set
        {
            _xml = value ?? throw new ArgumentNullException(nameof(value));
            _bytes = null;
        }
    }

    /// <summary>The part's raw bytes, re-serialising the XML tree first when one is loaded.</summary>
    public byte[] GetBytes()
    {
        if (_xml is not null)
        {
            return SerializeXml(_xml);
        }

        return _bytes ?? [];
    }

    /// <summary>Replaces the part's content, discarding any parsed XML tree.</summary>
    public void SetBytes(byte[] bytes)
    {
        _bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));
        _xml = null;
    }

    /// <summary>Opens a read-only stream over the part's current content.</summary>
    public Stream OpenRead() => new MemoryStream(GetBytes(), writable: false);

    /// <summary>The part's content decoded as UTF-8 text.</summary>
    public string GetText() => Encoding.UTF8.GetString(GetBytes());

    // ---- Relationships -------------------------------------------------------------------------

    /// <summary>The relationships that start from this part.</summary>
    public IReadOnlyList<OpcRelationship> Relationships => LoadRelationships();

    /// <summary>The relationships of a given type that start from this part.</summary>
    public IEnumerable<OpcRelationship> RelationshipsByType(string type) =>
        LoadRelationships().Where(r => r.Type == type);

    /// <summary>The single relationship of a given type, or <c>null</c> when there is none.</summary>
    public OpcRelationship? RelationshipByType(string type) =>
        LoadRelationships().FirstOrDefault(r => r.Type == type);

    /// <summary>Looks a relationship up by id.</summary>
    public OpcRelationship? RelationshipById(string id) =>
        LoadRelationships().FirstOrDefault(r => r.Id == id);

    /// <summary>The part a relationship id points at, or <c>null</c> when unresolvable.</summary>
    public OpcPart? RelatedPart(string id)
    {
        var rel = RelationshipById(id);
        if (rel is null || rel.TargetMode == TargetMode.External)
        {
            return null;
        }

        return _package.TryGetPart(rel.TargetPartName, out var part) ? part : null;
    }

    /// <summary>The single related part of a given type, or <c>null</c>.</summary>
    public OpcPart? RelatedPartByType(string type)
    {
        var rel = RelationshipByType(type);
        return rel is null ? null : RelatedPart(rel.Id);
    }

    /// <summary>All related parts of a given type, in relationship order.</summary>
    public IEnumerable<OpcPart> RelatedPartsByType(string type)
    {
        foreach (var rel in RelationshipsByType(type))
        {
            if (rel.TargetMode == TargetMode.Internal &&
                _package.TryGetPart(rel.TargetPartName, out var part))
            {
                yield return part;
            }
        }
    }

    /// <summary>
    /// Adds a relationship to another part in the package, allocating the next free id.
    /// </summary>
    public OpcRelationship AddRelationship(OpcPart target, string type) =>
        AddRelationship(type, Name.RelativeTo(target.Name));

    /// <summary>Adds a relationship with an explicit target.</summary>
    public OpcRelationship AddRelationship(string type, string target, TargetMode mode = TargetMode.Internal)
    {
        var relationships = LoadRelationships();
        var id = NextRelationshipId(relationships);
        var rel = new OpcRelationship(Name, id, type, target, mode);
        relationships.Add(rel);
        _package.MarkDirty();
        return rel;
    }

    /// <summary>
    /// Adds an external relationship — a hyperlink, or a linked (not embedded) image.
    /// </summary>
    public OpcRelationship AddExternalRelationship(string type, string uri) =>
        AddRelationship(type, uri, TargetMode.External);

    /// <summary>Removes a relationship by id. Returns false when no such relationship exists.</summary>
    public bool RemoveRelationship(string id)
    {
        var relationships = LoadRelationships();
        var index = relationships.FindIndex(r => r.Id == id);
        if (index < 0)
        {
            return false;
        }

        relationships.RemoveAt(index);
        _package.MarkDirty();
        return true;
    }

    private string NextRelationshipId(List<OpcRelationship> relationships)
    {
        // Ids must be unique within the part but need not be dense or ordered. Scanning for the
        // highest existing rIdN and continuing from there is what keeps a re-saved document's ids
        // stable, which matters because ids appear inside document.xml as r:id attributes that this
        // method's caller is about to write.
        while (true)
        {
            var candidate = "rId" + _nextRelationshipId++;
            if (!relationships.Any(r => r.Id == candidate))
            {
                return candidate;
            }
        }
    }

    private List<OpcRelationship> LoadRelationships()
    {
        if (_relationships is not null)
        {
            return _relationships;
        }

        _relationships = _package.ReadRelationshipsFor(Name);
        foreach (var rel in _relationships)
        {
            if (rel.Id.StartsWith("rId", StringComparison.Ordinal) &&
                int.TryParse(rel.Id.AsSpan(3), out var n) && n >= _nextRelationshipId)
            {
                _nextRelationshipId = n + 1;
            }
        }

        return _relationships;
    }

    internal bool HasLoadedRelationships => _relationships is not null;

    internal List<OpcRelationship> RelationshipsForWriting() => LoadRelationships();

    internal static byte[] SerializeXml(XDocument document)
    {
        var settings = new XmlWriterSettings
        {
            // OOXML parts are UTF-8 with a BOM and a standalone declaration. Word reads a
            // BOM-less part, but Excel's stricter reader rejects some parts without one, and
            // matching what the Office applications write keeps byte-level diffs meaningful.
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            Indent = false,
            OmitXmlDeclaration = false,
            NewLineHandling = NewLineHandling.None,
            CloseOutput = false,
        };

        using var buffer = new MemoryStream();
        using (var writer = XmlWriter.Create(buffer, settings))
        {
            document.Save(writer);
        }

        return buffer.ToArray();
    }

    public override string ToString() => $"{Name} [{ContentType}]";
}

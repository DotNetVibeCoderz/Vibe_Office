// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Globalization;
using System.Xml.Linq;
using OfficeNet.Core.Packaging;
using OfficeNet.Core.Xml;

namespace OfficeNet.Core.Metadata;

/// <summary>
/// The Dublin Core document properties every OOXML package carries in <c>docProps/core.xml</c> —
/// title, author, dates, keywords.
/// </summary>
/// <remarks>
/// This part is identical across .docx, .xlsx and .pptx, which is why it lives in Core rather than
/// being written three times. Properties are read and written straight against the XML tree rather
/// than cached in fields, so a caller that edits the part directly and one that uses this class
/// cannot disagree about what the document says.
/// </remarks>
public sealed class CoreProperties
{
    private static readonly OpcPartName PartName = "/docProps/core.xml";

    private readonly OpcPart _part;

    private CoreProperties(OpcPart part) => _part = part;

    /// <summary>
    /// Opens the package's core properties, creating the part and its package relationship when the
    /// package does not have one yet.
    /// </summary>
    public static CoreProperties Open(OpcPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        var existing = package.RootRelatedPart(RelationshipTypes.CoreProperties)
            ?? package.FindPart(PartName);

        if (existing is not null)
        {
            return new CoreProperties(existing);
        }

        var root = new XElement(Ns.CoreProps + "coreProperties",
            new XAttribute(XNamespace.Xmlns + "cp", Ns.CoreProps.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "dc", Ns.Dc.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "dcterms", Ns.DcTerms.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "xsi", Ns.Xsi.NamespaceName));

        var part = package.AddXmlPart(PartName, ContentTypes.CoreProperties, XmlUtil.NewDocument(root));
        package.AddRootRelationship(part, RelationshipTypes.CoreProperties);
        return new CoreProperties(part);
    }

    private XElement Root => _part.Xml.Root!;

    private string? Get(XName name) => Root.Element(name)?.Value;

    private void Set(XName name, string? value)
    {
        var element = Root.Element(name);

        if (string.IsNullOrEmpty(value))
        {
            element?.Remove();
            _part.Package.MarkDirty();
            return;
        }

        if (element is null)
        {
            Root.Add(new XElement(name, value));
        }
        else
        {
            element.Value = value;
        }

        _part.Package.MarkDirty();
    }

    private DateTime? GetDate(XName name)
    {
        var raw = Root.Element(name)?.Value;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        // W3CDTF, which is ISO 8601 restricted to a form always ending in Z or an explicit offset.
        return DateTime.TryParse(raw, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var value)
            ? value
            : null;
    }

    private void SetDate(XName name, DateTime? value)
    {
        var element = Root.Element(name);

        if (value is null)
        {
            element?.Remove();
            _part.Package.MarkDirty();
            return;
        }

        var text = value.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        if (element is null)
        {
            element = new XElement(name, text);
            Root.Add(element);
        }
        else
        {
            element.Value = text;
        }

        // The xsi:type is not decoration: Office rejects dcterms:created without it.
        element.SetAttributeValue(Ns.Xsi + "type", "dcterms:W3CDTF");
        _part.Package.MarkDirty();
    }

    /// <summary>The document title.</summary>
    public string? Title
    {
        get => Get(Ns.Dc + "title");
        set => Set(Ns.Dc + "title", value);
    }

    /// <summary>The subject.</summary>
    public string? Subject
    {
        get => Get(Ns.Dc + "subject");
        set => Set(Ns.Dc + "subject", value);
    }

    /// <summary>The author.</summary>
    public string? Creator
    {
        get => Get(Ns.Dc + "creator");
        set => Set(Ns.Dc + "creator", value);
    }

    /// <summary>Alias for <see cref="Creator"/>, which is what the Office UI calls "Author".</summary>
    public string? Author
    {
        get => Creator;
        set => Creator = value;
    }

    /// <summary>Keywords, conventionally separated by semicolons or commas.</summary>
    public string? Keywords
    {
        get => Get(Ns.CoreProps + "keywords");
        set => Set(Ns.CoreProps + "keywords", value);
    }

    /// <summary>The free-text description ("Comments" in the Office UI).</summary>
    public string? Description
    {
        get => Get(Ns.Dc + "description");
        set => Set(Ns.Dc + "description", value);
    }

    /// <summary>Who saved the document last.</summary>
    public string? LastModifiedBy
    {
        get => Get(Ns.CoreProps + "lastModifiedBy");
        set => Set(Ns.CoreProps + "lastModifiedBy", value);
    }

    /// <summary>The revision number, incremented by Office on each save.</summary>
    public string? Revision
    {
        get => Get(Ns.CoreProps + "revision");
        set => Set(Ns.CoreProps + "revision", value);
    }

    /// <summary>The category.</summary>
    public string? Category
    {
        get => Get(Ns.CoreProps + "category");
        set => Set(Ns.CoreProps + "category", value);
    }

    /// <summary>The content status ("Draft", "Final", ...).</summary>
    public string? ContentStatus
    {
        get => Get(Ns.CoreProps + "contentStatus");
        set => Set(Ns.CoreProps + "contentStatus", value);
    }

    /// <summary>The document language tag.</summary>
    public string? Language
    {
        get => Get(Ns.Dc + "language");
        set => Set(Ns.Dc + "language", value);
    }

    /// <summary>The identifier.</summary>
    public string? Identifier
    {
        get => Get(Ns.Dc + "identifier");
        set => Set(Ns.Dc + "identifier", value);
    }

    /// <summary>The document version.</summary>
    public string? Version
    {
        get => Get(Ns.CoreProps + "version");
        set => Set(Ns.CoreProps + "version", value);
    }

    /// <summary>When the document was created, in UTC.</summary>
    public DateTime? Created
    {
        get => GetDate(Ns.DcTerms + "created");
        set => SetDate(Ns.DcTerms + "created", value);
    }

    /// <summary>When the document was last saved, in UTC.</summary>
    public DateTime? Modified
    {
        get => GetDate(Ns.DcTerms + "modified");
        set => SetDate(Ns.DcTerms + "modified", value);
    }

    /// <summary>When the document was last printed, in UTC.</summary>
    public DateTime? LastPrinted
    {
        get => GetDate(Ns.CoreProps + "lastPrinted");
        set => SetDate(Ns.CoreProps + "lastPrinted", value);
    }

    /// <summary>
    /// Stamps <see cref="Modified"/> to now and, when they are unset, fills in
    /// <see cref="Created"/> and the Gravicode Studios attribution.
    /// </summary>
    public void StampSave(string? modifiedBy = null)
    {
        var now = DateTime.UtcNow;
        Created ??= now;
        Modified = now;

        if (modifiedBy is not null)
        {
            LastModifiedBy = modifiedBy;
        }

        Creator ??= "OfficeNet by Gravicode Studios";

        Revision = int.TryParse(Revision, out var revision)
            ? (revision + 1).ToString(CultureInfo.InvariantCulture)
            : "1";
    }
}

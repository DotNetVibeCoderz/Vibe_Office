// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Globalization;
using System.Xml.Linq;
using OfficeNet.Core.Packaging;
using OfficeNet.Core.Xml;

namespace OfficeNet.Core.Metadata;

/// <summary>
/// The application-level properties in <c>docProps/app.xml</c>: which application wrote the file,
/// page and word counts, the company, and the document's heading/title outline.
/// </summary>
/// <remarks>
/// The counts here are a cache, not a computation — Office writes what it last calculated and does
/// not recompute on open. Writing a stale value is therefore harmless but misleading, so OfficeNet
/// updates them only when it has actually counted, and leaves them absent otherwise.
/// </remarks>
public sealed class ExtendedProperties
{
    private static readonly OpcPartName PartName = "/docProps/app.xml";

    private readonly OpcPart _part;

    private ExtendedProperties(OpcPart part) => _part = part;

    /// <summary>Opens the package's extended properties, creating the part when there is none.</summary>
    public static ExtendedProperties Open(OpcPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        var existing = package.RootRelatedPart(RelationshipTypes.ExtendedProperties)
            ?? package.FindPart(PartName);

        if (existing is not null)
        {
            return new ExtendedProperties(existing);
        }

        var root = new XElement(Ns.Ep + "Properties",
            new XAttribute(XNamespace.Xmlns + "vt", Ns.Vt.NamespaceName),
            new XElement(Ns.Ep + "Application", "OfficeNet by Gravicode Studios"),
            new XElement(Ns.Ep + "AppVersion", "1.0000"));

        var part = package.AddXmlPart(PartName, ContentTypes.ExtendedProperties, XmlUtil.NewDocument(root));
        package.AddRootRelationship(part, RelationshipTypes.ExtendedProperties);
        return new ExtendedProperties(part);
    }

    private XElement Root => _part.Xml.Root!;

    private string? Get(string name) => Root.Element(Ns.Ep + name)?.Value;

    private void Set(string name, string? value)
    {
        var element = Root.Element(Ns.Ep + name);

        if (value is null)
        {
            element?.Remove();
        }
        else if (element is null)
        {
            Root.Add(new XElement(Ns.Ep + name, value));
        }
        else
        {
            element.Value = value;
        }

        _part.Package.MarkDirty();
    }

    private int? GetInt(string name) =>
        int.TryParse(Get(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    private void SetInt(string name, int? value) =>
        Set(name, value?.ToString(CultureInfo.InvariantCulture));

    /// <summary>The application that wrote the file.</summary>
    public string? Application
    {
        get => Get("Application");
        set => Set("Application", value);
    }

    /// <summary>The application version, in the <c>major.minorminorminorminor</c> form Office uses.</summary>
    public string? AppVersion
    {
        get => Get("AppVersion");
        set => Set("AppVersion", value);
    }

    /// <summary>The company name.</summary>
    public string? Company
    {
        get => Get("Company");
        set => Set("Company", value);
    }

    /// <summary>The manager name.</summary>
    public string? Manager
    {
        get => Get("Manager");
        set => Set("Manager", value);
    }

    /// <summary>The document template the file was created from.</summary>
    public string? Template
    {
        get => Get("Template");
        set => Set("Template", value);
    }

    /// <summary>Total editing time in minutes.</summary>
    public int? TotalTime
    {
        get => GetInt("TotalTime");
        set => SetInt("TotalTime", value);
    }

    /// <summary>Cached page count.</summary>
    public int? Pages
    {
        get => GetInt("Pages");
        set => SetInt("Pages", value);
    }

    /// <summary>Cached word count.</summary>
    public int? Words
    {
        get => GetInt("Words");
        set => SetInt("Words", value);
    }

    /// <summary>Cached character count, excluding spaces.</summary>
    public int? Characters
    {
        get => GetInt("Characters");
        set => SetInt("Characters", value);
    }

    /// <summary>Cached character count, including spaces.</summary>
    public int? CharactersWithSpaces
    {
        get => GetInt("CharactersWithSpaces");
        set => SetInt("CharactersWithSpaces", value);
    }

    /// <summary>Cached paragraph count.</summary>
    public int? Paragraphs
    {
        get => GetInt("Paragraphs");
        set => SetInt("Paragraphs", value);
    }

    /// <summary>Cached line count.</summary>
    public int? Lines
    {
        get => GetInt("Lines");
        set => SetInt("Lines", value);
    }

    /// <summary>Cached slide count, for presentations.</summary>
    public int? Slides
    {
        get => GetInt("Slides");
        set => SetInt("Slides", value);
    }

    /// <summary>Cached note-slide count, for presentations.</summary>
    public int? Notes
    {
        get => GetInt("Notes");
        set => SetInt("Notes", value);
    }

    /// <summary>Cached hidden-slide count.</summary>
    public int? HiddenSlides
    {
        get => GetInt("HiddenSlides");
        set => SetInt("HiddenSlides", value);
    }

    /// <summary>Whether the document is marked to scale its cropped thumbnail.</summary>
    public bool? ScaleCrop
    {
        get => XmlUtil.OoxmlBool(Get("ScaleCrop"));
        set => Set("ScaleCrop", value is null ? null : value.Value ? "true" : "false");
    }

    /// <summary>Whether hyperlinks in the document have been changed since last save.</summary>
    public bool? LinksUpToDate
    {
        get => XmlUtil.OoxmlBool(Get("LinksUpToDate"));
        set => Set("LinksUpToDate", value is null ? null : value.Value ? "true" : "false");
    }

    /// <summary>Marks the file as produced by OfficeNet, preserving an existing company name.</summary>
    public void StampProducer()
    {
        Application = "OfficeNet by Gravicode Studios";
        AppVersion ??= "1.0000";
        Company ??= "Gravicode Studios";
    }
}

// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Collections;
using System.Xml.Linq;
using OfficeNet.Core;
using OfficeNet.Core.Xml;

namespace WordNet.Styles;

/// <summary>What a style can be applied to.</summary>
public enum StyleType
{
    /// <summary>A paragraph style.</summary>
    Paragraph,

    /// <summary>A character style, applied to runs.</summary>
    Character,

    /// <summary>A table style.</summary>
    Table,

    /// <summary>A numbering style.</summary>
    Numbering,
}

/// <summary>One style definition from <c>styles.xml</c>.</summary>
public sealed class Style
{
    private readonly WordDocument _document;

    internal Style(WordDocument document, XElement element)
    {
        _document = document;
        Element = element;

        ParagraphFormat = new ParagraphFormat(
            () => XmlUtil.GetOrCreate(element, Ns.W + "pPr", StyleOrder),
            document.Touch);

        RunFormat = new RunFormat(
            () => XmlUtil.GetOrCreate(element, Ns.W + "rPr", StyleOrder),
            document.Touch);
    }

    /// <summary>The order <c>w:style</c>'s children must appear in.</summary>
    internal static readonly XName[] StyleOrder =
    [
        Ns.W + "name", Ns.W + "aliases", Ns.W + "basedOn", Ns.W + "next", Ns.W + "link",
        Ns.W + "autoRedefine", Ns.W + "hidden", Ns.W + "uiPriority", Ns.W + "semiHidden",
        Ns.W + "unhideWhenUsed", Ns.W + "qFormat", Ns.W + "locked", Ns.W + "personal",
        Ns.W + "personalCompose", Ns.W + "personalReply", Ns.W + "rsid", Ns.W + "pPr",
        Ns.W + "rPr", Ns.W + "tblPr", Ns.W + "trPr", Ns.W + "tcPr", Ns.W + "tblStylePr",
    ];

    /// <summary>The underlying <c>w:style</c> element.</summary>
    public XElement Element { get; }

    /// <summary>The style id, which is what documents reference.</summary>
    public string StyleId => Element.Attribute(Ns.W + "styleId")?.Value ?? string.Empty;

    /// <summary>The display name shown in Word's style gallery.</summary>
    public string? Name
    {
        get => Element.Val(Ns.W + "name");
        set
        {
            XmlUtil.SetOrdered(Element, Ns.W + "name",
                value is null ? null : XmlUtil.ValElement(Ns.W + "name", value), StyleOrder);
            _document.Touch();
        }
    }

    /// <summary>What the style applies to.</summary>
    public StyleType Type => Element.Attribute(Ns.W + "type")?.Value switch
    {
        "character" => StyleType.Character,
        "table" => StyleType.Table,
        "numbering" => StyleType.Numbering,
        _ => StyleType.Paragraph,
    };

    /// <summary>The style this one inherits from.</summary>
    public string? BasedOn
    {
        get => Element.Val(Ns.W + "basedOn");
        set
        {
            XmlUtil.SetOrdered(Element, Ns.W + "basedOn",
                value is null ? null : XmlUtil.ValElement(Ns.W + "basedOn", value), StyleOrder);
            _document.Touch();
        }
    }

    /// <summary>The style applied to the paragraph typed after one in this style.</summary>
    public string? NextStyle
    {
        get => Element.Val(Ns.W + "next");
        set
        {
            XmlUtil.SetOrdered(Element, Ns.W + "next",
                value is null ? null : XmlUtil.ValElement(Ns.W + "next", value), StyleOrder);
            _document.Touch();
        }
    }

    /// <summary>True when the style is the default for its type.</summary>
    public bool IsDefault => XmlUtil.OoxmlBool(Element.Attribute(Ns.W + "default")?.Value) == true;

    /// <summary>True when the style appears in Word's gallery.</summary>
    public bool IsQuickStyle
    {
        get => Element.Element(Ns.W + "qFormat") is not null;
        set
        {
            XmlUtil.SetOrdered(Element, Ns.W + "qFormat",
                value ? new XElement(Ns.W + "qFormat") : null, StyleOrder);
            _document.Touch();
        }
    }

    /// <summary>The style's paragraph formatting.</summary>
    public ParagraphFormat ParagraphFormat { get; }

    /// <summary>The style's character formatting.</summary>
    public RunFormat RunFormat { get; }

    /// <summary>Removes the style definition.</summary>
    public void Remove()
    {
        Element.Remove();
        _document.Touch();
    }

    public override string ToString() => $"{StyleId} ({Type}){(Name is null ? "" : $" \"{Name}\"")}";
}

/// <summary>
/// A document's style definitions.
/// </summary>
/// <remarks>
/// <para>
/// A style referenced by a paragraph but not defined in <c>styles.xml</c> is silently ignored by
/// Word — the paragraph renders as Normal and the caller sees no error. That is why every method
/// here that applies a style also ensures it exists, and why <see cref="Add"/> is the only way to
/// introduce a new one.
/// </para>
/// </remarks>
public sealed class StyleCollection : IReadOnlyCollection<Style>
{
    private readonly WordDocument _document;
    private readonly XElement _root;

    internal StyleCollection(WordDocument document, XElement root)
    {
        _document = document;
        _root = root;
    }

    /// <inheritdoc />
    public int Count => _root.Elements(Ns.W + "style").Count();

    /// <summary>Every style definition.</summary>
    public IEnumerable<Style> All =>
        _root.Elements(Ns.W + "style").Select(e => new Style(_document, e));

    /// <summary>Finds a style by id, or <c>null</c>.</summary>
    public Style? this[string styleId] =>
        _root.Elements(Ns.W + "style")
            .Where(e => string.Equals(e.Attribute(Ns.W + "styleId")?.Value, styleId,
                StringComparison.OrdinalIgnoreCase))
            .Select(e => new Style(_document, e))
            .FirstOrDefault();

    /// <summary>Finds a style by its display name, or <c>null</c>.</summary>
    public Style? ByName(string name) =>
        _root.Elements(Ns.W + "style")
            .Where(e => string.Equals(e.Val(Ns.W + "name"), name, StringComparison.OrdinalIgnoreCase))
            .Select(e => new Style(_document, e))
            .FirstOrDefault();

    /// <summary>True when a style with this id exists.</summary>
    public bool Contains(string styleId) => this[styleId] is not null;

    /// <summary>
    /// Adds a style definition.
    /// </summary>
    /// <param name="styleId">The id documents will reference.</param>
    /// <param name="name">The display name; defaults to the id.</param>
    /// <param name="type">What the style applies to.</param>
    /// <param name="basedOn">The style to inherit from.</param>
    /// <exception cref="OfficeNetException">A style with that id already exists.</exception>
    public Style Add(string styleId, string? name = null, StyleType type = StyleType.Paragraph,
        string? basedOn = "Normal")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(styleId);

        if (Contains(styleId))
        {
            throw new OfficeNetException(
                $"A style with id '{styleId}' already exists. Use the indexer to modify it.");
        }

        var element = new XElement(Ns.W + "style",
            new XAttribute(Ns.W + "type", type switch
            {
                StyleType.Character => "character",
                StyleType.Table => "table",
                StyleType.Numbering => "numbering",
                _ => "paragraph",
            }),
            new XAttribute(Ns.W + "styleId", styleId),
            XmlUtil.ValElement(Ns.W + "name", name ?? styleId));

        if (basedOn is not null && Contains(basedOn))
        {
            element.Add(XmlUtil.ValElement(Ns.W + "basedOn", basedOn));
        }

        element.Add(new XElement(Ns.W + "qFormat"));

        _root.Add(element);
        _document.Touch();
        return new Style(_document, element);
    }

    /// <summary>Returns an existing style, or adds it when it is missing.</summary>
    public Style GetOrAdd(string styleId, string? name = null, StyleType type = StyleType.Paragraph,
        string? basedOn = "Normal") =>
        this[styleId] ?? Add(styleId, name, type, basedOn);

    /// <summary>
    /// Makes sure the Hyperlink character style exists.
    /// </summary>
    /// <remarks>
    /// Word's UI creates this on demand and a template usually has it, but a document built from
    /// scratch does not — and a hyperlink whose <c>w:rStyle</c> names a missing style renders as
    /// ordinary black text, which is indistinguishable from a bug.
    /// </remarks>
    internal void EnsureHyperlinkStyle()
    {
        if (Contains("Hyperlink"))
        {
            return;
        }

        var element = new XElement(Ns.W + "style",
            new XAttribute(Ns.W + "type", "character"),
            new XAttribute(Ns.W + "styleId", "Hyperlink"),
            XmlUtil.ValElement(Ns.W + "name", "Hyperlink"),
            XmlUtil.ValElement(Ns.W + "basedOn", "DefaultParagraphFont"),
            new XElement(Ns.W + "uiPriority", new XAttribute(Ns.W + "val", "99")),
            new XElement(Ns.W + "unhideWhenUsed"),
            new XElement(Ns.W + "rPr",
                XmlUtil.ValElement(Ns.W + "color", "0563C1"),
                XmlUtil.ValElement(Ns.W + "u", "single")));

        _root.Add(element);
        _document.Touch();
    }

    /// <summary>Makes sure the ListParagraph style exists.</summary>
    internal void EnsureListParagraphStyle()
    {
        if (Contains("ListParagraph"))
        {
            return;
        }

        var element = new XElement(Ns.W + "style",
            new XAttribute(Ns.W + "type", "paragraph"),
            new XAttribute(Ns.W + "styleId", "ListParagraph"),
            XmlUtil.ValElement(Ns.W + "name", "List Paragraph"),
            XmlUtil.ValElement(Ns.W + "basedOn", "Normal"),
            new XElement(Ns.W + "uiPriority", new XAttribute(Ns.W + "val", "34")),
            new XElement(Ns.W + "qFormat"),
            new XElement(Ns.W + "pPr",
                new XElement(Ns.W + "ind", new XAttribute(Ns.W + "left", "720")),
                XmlUtil.ValElement(Ns.W + "contextualSpacing", "1")));

        _root.Add(element);
        _document.Touch();
    }

    /// <inheritdoc />
    public IEnumerator<Style> GetEnumerator() => All.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

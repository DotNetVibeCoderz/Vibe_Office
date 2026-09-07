// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Xml.Linq;
using OfficeNet.Core;
using OfficeNet.Core.Packaging;
using OfficeNet.Core.Xml;

namespace WordNet.Sections;

/// <summary>Page orientation.</summary>
public enum PageOrientation
{
    /// <summary>Taller than wide.</summary>
    Portrait,

    /// <summary>Wider than tall.</summary>
    Landscape,
}

/// <summary>Where a new section begins.</summary>
public enum SectionStart
{
    /// <summary>On the next page.</summary>
    NextPage,

    /// <summary>Immediately, without a page break.</summary>
    Continuous,

    /// <summary>On the next even-numbered page.</summary>
    EvenPage,

    /// <summary>On the next odd-numbered page.</summary>
    OddPage,

    /// <summary>In the next column.</summary>
    NextColumn,
}

/// <summary>Which pages a header or footer applies to.</summary>
public enum HeaderFooterKind
{
    /// <summary>Every page the section covers, unless a more specific one exists.</summary>
    Default,

    /// <summary>The section's first page only.</summary>
    First,

    /// <summary>Even-numbered pages.</summary>
    Even,
}

/// <summary>A header or footer part's content.</summary>
public sealed class HeaderFooter
{
    private readonly WordDocument _document;

    internal HeaderFooter(WordDocument document, OpcPart part, XElement root, bool isHeader)
    {
        _document = document;
        Part = part;
        Root = root;
        IsHeader = isHeader;
    }

    /// <summary>The underlying part.</summary>
    public OpcPart Part { get; }

    /// <summary>The <c>w:hdr</c> or <c>w:ftr</c> root element.</summary>
    public XElement Root { get; }

    /// <summary>True for a header, false for a footer.</summary>
    public bool IsHeader { get; }

    /// <summary>The paragraphs in the header or footer.</summary>
    public IReadOnlyList<Paragraph> Paragraphs =>
        [.. Root.Elements(Ns.W + "p").Select(e => new Paragraph(_document, e))];

    /// <summary>The tables in the header or footer.</summary>
    public IReadOnlyList<WordNet.Tables.Table> Tables =>
        [.. Root.Elements(Ns.W + "tbl").Select(e => new WordNet.Tables.Table(_document, e))];

    /// <summary>Appends a paragraph.</summary>
    public Paragraph AddParagraph(string text = "", string? styleId = null)
    {
        var element = new XElement(Ns.W + "p");
        Root.Add(element);

        var paragraph = new Paragraph(_document, element);

        if (styleId is not null)
        {
            paragraph.StyleId = styleId;
        }

        if (text.Length > 0)
        {
            paragraph.AddRun(text);
        }

        _document.Touch();
        return paragraph;
    }

    /// <summary>Appends a table.</summary>
    public WordNet.Tables.Table AddTable(int rows, int columns)
    {
        var table = WordNet.Tables.Table.Create(_document, rows, columns);
        Root.Add(table.Element);

        // A header ending in a table needs a trailing paragraph, exactly as a cell does.
        Root.Add(new XElement(Ns.W + "p"));

        _document.Touch();
        return table;
    }

    /// <summary>Removes everything and leaves a single empty paragraph.</summary>
    public void Clear()
    {
        Root.RemoveNodes();
        Root.Add(new XElement(Ns.W + "p"));
        _document.Touch();
    }

    /// <summary>The header's or footer's text, paragraphs joined by newlines.</summary>
    public string Text => string.Join('\n', Paragraphs.Select(p => p.Text));

    public override string ToString() => $"{(IsHeader ? "Header" : "Footer")}: {Text}";
}

/// <summary>
/// A section: page size, margins, orientation, and the headers and footers that apply to it.
/// </summary>
/// <remarks>
/// <para>
/// A document's sections are defined by <c>w:sectPr</c> elements, and their placement is the part
/// that surprises: the <em>last</em> section's properties are the last child of <c>w:body</c>,
/// while every earlier section's properties sit inside the <c>w:pPr</c> of the last paragraph
/// belonging to that section. So adding a section means moving properties, not just appending them.
/// </para>
/// </remarks>
public sealed class Section
{
    private readonly WordDocument _document;

    internal Section(WordDocument document, XElement properties)
    {
        _document = document;
        Properties = properties;
    }

    private static readonly XName[] SectionOrder =
    [
        Ns.W + "footnotePr", Ns.W + "endnotePr", Ns.W + "type", Ns.W + "pgSz", Ns.W + "pgMar",
        Ns.W + "paperSrc", Ns.W + "pgBorders", Ns.W + "lnNumType", Ns.W + "pgNumType",
        Ns.W + "cols", Ns.W + "formProt", Ns.W + "vAlign", Ns.W + "noEndnote",
        Ns.W + "titlePg", Ns.W + "textDirection", Ns.W + "bidi", Ns.W + "rtlGutter",
        Ns.W + "docGrid", Ns.W + "printerSettings",
    ];

    // headerReference and footerReference must precede everything else in w:sectPr.
    private static readonly XName[] ReferenceOrder =
    [
        Ns.W + "headerReference", Ns.W + "footerReference", .. SectionOrder,
    ];

    /// <summary>The underlying <c>w:sectPr</c> element.</summary>
    public XElement Properties { get; }

    private XElement PageSize => XmlUtil.GetOrCreate(Properties, Ns.W + "pgSz", ReferenceOrder);

    private XElement PageMargins => XmlUtil.GetOrCreate(Properties, Ns.W + "pgMar", ReferenceOrder);

    private Length Dimension(XElement element, string attribute, Length fallback)
    {
        var raw = element.Attribute(Ns.W + attribute)?.Value;
        return raw is not null && double.TryParse(raw, out var twips) ? Length.FromTwips(twips) : fallback;
    }

    /// <summary>The page width.</summary>
    public Length PageWidth
    {
        get => Dimension(PageSize, "w", Units.Cm(21));
        set
        {
            PageSize.SetAttributeValue(Ns.W + "w", XmlUtil.Num(value.Twips));
            _document.Touch();
        }
    }

    /// <summary>The page height.</summary>
    public Length PageHeight
    {
        get => Dimension(PageSize, "h", Units.Cm(29.7));
        set
        {
            PageSize.SetAttributeValue(Ns.W + "h", XmlUtil.Num(value.Twips));
            _document.Touch();
        }
    }

    /// <summary>
    /// The page orientation. Setting it swaps the width and height as well as the attribute.
    /// </summary>
    /// <remarks>
    /// <c>w:orient</c> is metadata: it tells Word which way to feed the paper, and it does not
    /// change the page dimensions. A section with <c>orient="landscape"</c> and a portrait
    /// <c>w:pgSz</c> lays out portrait and prints rotated, which is almost never what was meant.
    /// </remarks>
    public PageOrientation Orientation
    {
        get => PageSize.Attribute(Ns.W + "orient")?.Value == "landscape"
            ? PageOrientation.Landscape
            : PageOrientation.Portrait;
        set
        {
            var isLandscape = value == PageOrientation.Landscape;
            var currentlyLandscape = Orientation == PageOrientation.Landscape;

            if (isLandscape != currentlyLandscape)
            {
                var width = PageWidth;
                var height = PageHeight;
                PageWidth = height;
                PageHeight = width;
            }

            if (isLandscape)
            {
                PageSize.SetAttributeValue(Ns.W + "orient", "landscape");
            }
            else
            {
                PageSize.Attribute(Ns.W + "orient")?.Remove();
            }

            _document.Touch();
        }
    }

    /// <summary>The top margin.</summary>
    public Length TopMargin
    {
        get => Dimension(PageMargins, "top", Units.Cm(2.54));
        set
        {
            PageMargins.SetAttributeValue(Ns.W + "top", XmlUtil.Num(value.Twips));
            _document.Touch();
        }
    }

    /// <summary>The bottom margin.</summary>
    public Length BottomMargin
    {
        get => Dimension(PageMargins, "bottom", Units.Cm(2.54));
        set
        {
            PageMargins.SetAttributeValue(Ns.W + "bottom", XmlUtil.Num(value.Twips));
            _document.Touch();
        }
    }

    /// <summary>The left margin.</summary>
    public Length LeftMargin
    {
        get => Dimension(PageMargins, "left", Units.Cm(2.54));
        set
        {
            PageMargins.SetAttributeValue(Ns.W + "left", XmlUtil.Num(value.Twips));
            _document.Touch();
        }
    }

    /// <summary>The right margin.</summary>
    public Length RightMargin
    {
        get => Dimension(PageMargins, "right", Units.Cm(2.54));
        set
        {
            PageMargins.SetAttributeValue(Ns.W + "right", XmlUtil.Num(value.Twips));
            _document.Touch();
        }
    }

    /// <summary>The distance from the page top to the header.</summary>
    public Length HeaderDistance
    {
        get => Dimension(PageMargins, "header", Units.Cm(1.25));
        set
        {
            PageMargins.SetAttributeValue(Ns.W + "header", XmlUtil.Num(value.Twips));
            _document.Touch();
        }
    }

    /// <summary>The distance from the page bottom to the footer.</summary>
    public Length FooterDistance
    {
        get => Dimension(PageMargins, "footer", Units.Cm(1.25));
        set
        {
            PageMargins.SetAttributeValue(Ns.W + "footer", XmlUtil.Num(value.Twips));
            _document.Touch();
        }
    }

    /// <summary>The usable width between the left and right margins.</summary>
    public Length ContentWidth => PageWidth - LeftMargin - RightMargin;

    /// <summary>The usable height between the top and bottom margins.</summary>
    public Length ContentHeight => PageHeight - TopMargin - BottomMargin;

    /// <summary>Sets every margin at once.</summary>
    public Section SetMargins(Length all) => SetMargins(all, all, all, all);

    /// <summary>Sets the four margins.</summary>
    public Section SetMargins(Length top, Length right, Length bottom, Length left)
    {
        TopMargin = top;
        RightMargin = right;
        BottomMargin = bottom;
        LeftMargin = left;
        return this;
    }

    /// <summary>Sets the page size from a named standard, for example <c>A4</c> or <c>F4</c>.</summary>
    /// <exception cref="ArgumentException">The name is not a standard page size.</exception>
    public Section SetPageSize(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var (width, height) = name.Trim().ToUpperInvariant() switch
        {
            "A3" => (Units.Mm(297), Units.Mm(420)),
            "A4" => (Units.Mm(210), Units.Mm(297)),
            "A5" => (Units.Mm(148), Units.Mm(210)),
            "LETTER" => (Units.Inches(8.5), Units.Inches(11)),
            "LEGAL" => (Units.Inches(8.5), Units.Inches(14)),
            // F4 (Folio) is the standard office paper size in Indonesia and is not one of the
            // sizes Word offers by default.
            "F4" or "FOLIO" => (Units.Mm(215), Units.Mm(330)),
            _ => throw new ArgumentException(
                $"'{name}' is not a known page size. Use A3, A4, A5, Letter, Legal or F4.", nameof(name)),
        };

        var landscape = Orientation == PageOrientation.Landscape;
        PageWidth = landscape ? height : width;
        PageHeight = landscape ? width : height;
        return this;
    }

    /// <summary>Where the section starts.</summary>
    public SectionStart Start
    {
        get => Properties.Val(Ns.W + "type") switch
        {
            "continuous" => SectionStart.Continuous,
            "evenPage" => SectionStart.EvenPage,
            "oddPage" => SectionStart.OddPage,
            "nextColumn" => SectionStart.NextColumn,
            _ => SectionStart.NextPage,
        };
        set
        {
            XmlUtil.SetOrdered(Properties, Ns.W + "type",
                XmlUtil.ValElement(Ns.W + "type", value switch
                {
                    SectionStart.Continuous => "continuous",
                    SectionStart.EvenPage => "evenPage",
                    SectionStart.OddPage => "oddPage",
                    SectionStart.NextColumn => "nextColumn",
                    _ => "nextPage",
                }), ReferenceOrder);
            _document.Touch();
        }
    }

    /// <summary>The number of text columns.</summary>
    public int ColumnCount
    {
        get
        {
            var raw = Properties.Element(Ns.W + "cols")?.Attribute(Ns.W + "num")?.Value;
            return raw is not null && int.TryParse(raw, out var count) ? count : 1;
        }
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);

            var element = new XElement(Ns.W + "cols",
                new XAttribute(Ns.W + "space", "708"));

            if (value > 1)
            {
                element.SetAttributeValue(Ns.W + "num", value);
                element.SetAttributeValue(Ns.W + "equalWidth", "1");
            }

            XmlUtil.SetOrdered(Properties, Ns.W + "cols", element, ReferenceOrder);
            _document.Touch();
        }
    }

    /// <summary>
    /// True when the section's first page uses its own header and footer.
    /// </summary>
    /// <remarks>
    /// Setting a first-page header does nothing unless this is on: Word looks for a
    /// <c>w:titlePg</c> before it consults the <c>first</c> reference, so the header exists in the
    /// package and never appears.
    /// </remarks>
    public bool DifferentFirstPage
    {
        get => Properties.Element(Ns.W + "titlePg") is not null;
        set
        {
            XmlUtil.SetOrdered(Properties, Ns.W + "titlePg",
                value ? new XElement(Ns.W + "titlePg") : null, ReferenceOrder);
            _document.Touch();
        }
    }

    /// <summary>The page number the section restarts at, or <c>null</c> to continue.</summary>
    public int? PageNumberStart
    {
        get
        {
            var raw = Properties.Element(Ns.W + "pgNumType")?.Attribute(Ns.W + "start")?.Value;
            return raw is not null && int.TryParse(raw, out var start) ? start : null;
        }
        set
        {
            XmlUtil.SetOrdered(Properties, Ns.W + "pgNumType", value is null
                ? null
                : new XElement(Ns.W + "pgNumType",
                    new XAttribute(Ns.W + "start", XmlUtil.Num(value.Value))), ReferenceOrder);
            _document.Touch();
        }
    }

    /// <summary>
    /// Gets the section's header, creating the part when it does not exist.
    /// </summary>
    public HeaderFooter GetHeader(HeaderFooterKind kind = HeaderFooterKind.Default) =>
        _document.GetOrCreateHeaderFooter(this, kind, isHeader: true);

    /// <summary>Gets the section's footer, creating the part when it does not exist.</summary>
    public HeaderFooter GetFooter(HeaderFooterKind kind = HeaderFooterKind.Default) =>
        _document.GetOrCreateHeaderFooter(this, kind, isHeader: false);

    /// <summary>Removes a header or footer reference and, when unused, its part.</summary>
    public void RemoveHeaderFooter(HeaderFooterKind kind, bool isHeader) =>
        _document.RemoveHeaderFooter(this, kind, isHeader);

    internal static string KindName(HeaderFooterKind kind) => kind switch
    {
        HeaderFooterKind.First => "first",
        HeaderFooterKind.Even => "even",
        _ => "default",
    };

    public override string ToString() =>
        $"{PageWidth.Centimeters:0.#}x{PageHeight.Centimeters:0.#} cm, {Orientation}";
}

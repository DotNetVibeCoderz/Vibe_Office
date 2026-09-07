// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Xml.Linq;
using OfficeNet.Core.Xml;

namespace WordNet;

/// <summary>
/// The parts a new .docx needs before it contains anything.
/// </summary>
/// <remarks>
/// <para>
/// A .docx is not valid with only <c>document.xml</c>. Word opens a package with no
/// <c>styles.xml</c>, but every paragraph then renders in the compiled-in default and the
/// heading styles a caller asks for silently do nothing — so the styles part is created up front
/// with the built-in styles defined rather than left to be added later.
/// </para>
/// <para>
/// The namespace declarations on <c>w:document</c> matter more than they look. Word validates
/// <c>mc:Ignorable</c> against the prefixes actually declared, and a document declaring a prefix
/// in <c>Ignorable</c> that it never binds is rejected before any content is read.
/// </para>
/// </remarks>
internal static class DefaultParts
{
    internal static XDocument Document()
    {
        var root = new XElement(Ns.W + "document",
            new XAttribute(XNamespace.Xmlns + "w", Ns.W.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "r", Ns.R.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "wp", Ns.Wp.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "a", Ns.A.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "pic", Ns.Pic.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "m", "http://schemas.openxmlformats.org/officeDocument/2006/math"),
            new XElement(Ns.W + "body",
                DefaultSection()));

        return XmlUtil.NewDocument(root);
    }

    /// <summary>
    /// The final section properties: A4 portrait with 2.54 cm margins.
    /// </summary>
    /// <remarks>
    /// The body's last child is a <c>w:sectPr</c> describing the last (usually only) section. A
    /// body without one opens, and Word then invents a page size from the printer — so the same
    /// document lays out differently on two machines.
    /// </remarks>
    internal static XElement DefaultSection()
    {
        return new XElement(Ns.W + "sectPr",
            new XElement(Ns.W + "pgSz",
                // A4 in twips: 11906 x 16838.
                new XAttribute(Ns.W + "w", "11906"),
                new XAttribute(Ns.W + "h", "16838")),
            new XElement(Ns.W + "pgMar",
                new XAttribute(Ns.W + "top", "1440"),
                new XAttribute(Ns.W + "right", "1440"),
                new XAttribute(Ns.W + "bottom", "1440"),
                new XAttribute(Ns.W + "left", "1440"),
                new XAttribute(Ns.W + "header", "708"),
                new XAttribute(Ns.W + "footer", "708"),
                new XAttribute(Ns.W + "gutter", "0")),
            new XElement(Ns.W + "cols", new XAttribute(Ns.W + "space", "708")),
            new XElement(Ns.W + "docGrid", new XAttribute(Ns.W + "linePitch", "360")));
    }

    internal static XDocument Styles()
    {
        var root = new XElement(Ns.W + "styles",
            new XAttribute(XNamespace.Xmlns + "w", Ns.W.NamespaceName),
            DocDefaults(),
            NormalStyle(),
            DefaultParagraphFontStyle(),
            TableNormalStyle(),
            NoListStyle(),
            TitleStyle(),
            SubtitleStyle(),
            QuoteStyle(),
            IntenseQuoteStyle(),
            CaptionStyle(),
            CodeStyle(),
            TableGridStyle());

        for (var level = 1; level <= 9; level++)
        {
            root.Add(HeadingStyle(level));
        }

        return XmlUtil.NewDocument(root);
    }

    private static XElement DocDefaults() =>
        new(Ns.W + "docDefaults",
            new XElement(Ns.W + "rPrDefault",
                new XElement(Ns.W + "rPr",
                    new XElement(Ns.W + "rFonts",
                        new XAttribute(Ns.W + "ascii", "Calibri"),
                        new XAttribute(Ns.W + "hAnsi", "Calibri"),
                        new XAttribute(Ns.W + "eastAsia", "Calibri"),
                        new XAttribute(Ns.W + "cs", "Times New Roman")),
                    XmlUtil.ValElement(Ns.W + "sz", "22"),
                    XmlUtil.ValElement(Ns.W + "szCs", "22"),
                    new XElement(Ns.W + "lang",
                        new XAttribute(Ns.W + "val", "en-US"),
                        new XAttribute(Ns.W + "eastAsia", "en-US"),
                        new XAttribute(Ns.W + "bidi", "ar-SA")))),
            new XElement(Ns.W + "pPrDefault",
                new XElement(Ns.W + "pPr",
                    new XElement(Ns.W + "spacing",
                        new XAttribute(Ns.W + "after", "160"),
                        new XAttribute(Ns.W + "line", "259"),
                        new XAttribute(Ns.W + "lineRule", "auto")))));

    private static XElement NormalStyle() =>
        new(Ns.W + "style",
            new XAttribute(Ns.W + "type", "paragraph"),
            new XAttribute(Ns.W + "default", "1"),
            new XAttribute(Ns.W + "styleId", "Normal"),
            XmlUtil.ValElement(Ns.W + "name", "Normal"),
            new XElement(Ns.W + "qFormat"));

    private static XElement DefaultParagraphFontStyle() =>
        new(Ns.W + "style",
            new XAttribute(Ns.W + "type", "character"),
            new XAttribute(Ns.W + "default", "1"),
            new XAttribute(Ns.W + "styleId", "DefaultParagraphFont"),
            XmlUtil.ValElement(Ns.W + "name", "Default Paragraph Font"),
            new XElement(Ns.W + "uiPriority", new XAttribute(Ns.W + "val", "1")),
            new XElement(Ns.W + "semiHidden"),
            new XElement(Ns.W + "unhideWhenUsed"));

    private static XElement TableNormalStyle() =>
        new(Ns.W + "style",
            new XAttribute(Ns.W + "type", "table"),
            new XAttribute(Ns.W + "default", "1"),
            new XAttribute(Ns.W + "styleId", "TableNormal"),
            XmlUtil.ValElement(Ns.W + "name", "Normal Table"),
            new XElement(Ns.W + "semiHidden"),
            new XElement(Ns.W + "unhideWhenUsed"),
            new XElement(Ns.W + "tblPr",
                new XElement(Ns.W + "tblInd",
                    new XAttribute(Ns.W + "w", "0"),
                    new XAttribute(Ns.W + "type", "dxa")),
                new XElement(Ns.W + "tblCellMar",
                    CellMargin("top", 0),
                    CellMargin("left", 108),
                    CellMargin("bottom", 0),
                    CellMargin("right", 108))));

    private static XElement CellMargin(string name, int twips) =>
        new(Ns.W + name,
            new XAttribute(Ns.W + "w", twips),
            new XAttribute(Ns.W + "type", "dxa"));

    private static XElement NoListStyle() =>
        new(Ns.W + "style",
            new XAttribute(Ns.W + "type", "numbering"),
            new XAttribute(Ns.W + "default", "1"),
            new XAttribute(Ns.W + "styleId", "NoList"),
            XmlUtil.ValElement(Ns.W + "name", "No List"),
            new XElement(Ns.W + "semiHidden"),
            new XElement(Ns.W + "unhideWhenUsed"));

    private static XElement HeadingStyle(int level)
    {
        // The sizes step down from 16 pt at level 1 to the body size, which is the proportion
        // Word's own Office theme uses.
        var sizeHalfPoints = level switch
        {
            1 => 32,
            2 => 26,
            3 => 24,
            4 => 22,
            _ => 22,
        };

        var color = level switch
        {
            1 => "1F3864",
            2 => "2E5496",
            3 => "1F4E79",
            _ => "2E5496",
        };

        var paragraphProperties = new XElement(Ns.W + "pPr",
            XmlUtil.ValElement(Ns.W + "keepNext", "1"),
            XmlUtil.ValElement(Ns.W + "keepLines", "1"),
            new XElement(Ns.W + "spacing",
                new XAttribute(Ns.W + "before", level == 1 ? "240" : "160"),
                new XAttribute(Ns.W + "after", "80")),
            XmlUtil.ValElement(Ns.W + "outlineLvl", (level - 1).ToString()));

        var runProperties = new XElement(Ns.W + "rPr",
            XmlUtil.ValElement(Ns.W + "color", color),
            XmlUtil.ValElement(Ns.W + "sz", sizeHalfPoints.ToString()),
            XmlUtil.ValElement(Ns.W + "szCs", sizeHalfPoints.ToString()));

        if (level <= 4)
        {
            runProperties.AddFirst(new XElement(Ns.W + "b"));
        }
        else
        {
            runProperties.AddFirst(new XElement(Ns.W + "i"));
        }

        return new XElement(Ns.W + "style",
            new XAttribute(Ns.W + "type", "paragraph"),
            new XAttribute(Ns.W + "styleId", $"Heading{level}"),
            // The NAME must be "heading N" with a space and lower case: that exact string is what
            // Word matches to populate the navigation pane and a TOC. "Heading1" as a name works
            // as a style but produces an empty table of contents.
            XmlUtil.ValElement(Ns.W + "name", $"heading {level}"),
            XmlUtil.ValElement(Ns.W + "basedOn", "Normal"),
            XmlUtil.ValElement(Ns.W + "next", "Normal"),
            new XElement(Ns.W + "uiPriority", new XAttribute(Ns.W + "val", (level + 8).ToString())),
            new XElement(Ns.W + "qFormat"),
            paragraphProperties,
            runProperties);
    }

    private static XElement TitleStyle() =>
        new(Ns.W + "style",
            new XAttribute(Ns.W + "type", "paragraph"),
            new XAttribute(Ns.W + "styleId", "Title"),
            XmlUtil.ValElement(Ns.W + "name", "Title"),
            XmlUtil.ValElement(Ns.W + "basedOn", "Normal"),
            XmlUtil.ValElement(Ns.W + "next", "Normal"),
            new XElement(Ns.W + "qFormat"),
            new XElement(Ns.W + "pPr",
                new XElement(Ns.W + "spacing",
                    new XAttribute(Ns.W + "after", "80"),
                    new XAttribute(Ns.W + "line", "240"),
                    new XAttribute(Ns.W + "lineRule", "auto")),
                XmlUtil.ValElement(Ns.W + "contextualSpacing", "1")),
            new XElement(Ns.W + "rPr",
                new XElement(Ns.W + "rFonts",
                    new XAttribute(Ns.W + "asciiTheme", "majorHAnsi"),
                    new XAttribute(Ns.W + "hAnsiTheme", "majorHAnsi")),
                new XElement(Ns.W + "spacing", new XAttribute(Ns.W + "val", "-10")),
                XmlUtil.ValElement(Ns.W + "kern", "28"),
                XmlUtil.ValElement(Ns.W + "sz", "56"),
                XmlUtil.ValElement(Ns.W + "szCs", "56")));

    private static XElement SubtitleStyle() =>
        new(Ns.W + "style",
            new XAttribute(Ns.W + "type", "paragraph"),
            new XAttribute(Ns.W + "styleId", "Subtitle"),
            XmlUtil.ValElement(Ns.W + "name", "Subtitle"),
            XmlUtil.ValElement(Ns.W + "basedOn", "Normal"),
            XmlUtil.ValElement(Ns.W + "next", "Normal"),
            new XElement(Ns.W + "qFormat"),
            new XElement(Ns.W + "pPr",
                new XElement(Ns.W + "spacing", new XAttribute(Ns.W + "after", "160")),
                XmlUtil.ValElement(Ns.W + "contextualSpacing", "1")),
            new XElement(Ns.W + "rPr",
                XmlUtil.ValElement(Ns.W + "color", "595959"),
                new XElement(Ns.W + "spacing", new XAttribute(Ns.W + "val", "15")),
                XmlUtil.ValElement(Ns.W + "sz", "28"),
                XmlUtil.ValElement(Ns.W + "szCs", "28")));

    private static XElement QuoteStyle() =>
        new(Ns.W + "style",
            new XAttribute(Ns.W + "type", "paragraph"),
            new XAttribute(Ns.W + "styleId", "Quote"),
            XmlUtil.ValElement(Ns.W + "name", "Quote"),
            XmlUtil.ValElement(Ns.W + "basedOn", "Normal"),
            XmlUtil.ValElement(Ns.W + "next", "Normal"),
            new XElement(Ns.W + "qFormat"),
            new XElement(Ns.W + "pPr",
                new XElement(Ns.W + "spacing",
                    new XAttribute(Ns.W + "before", "160"),
                    new XAttribute(Ns.W + "after", "160")),
                new XElement(Ns.W + "ind",
                    new XAttribute(Ns.W + "left", "720"),
                    new XAttribute(Ns.W + "right", "720"))),
            new XElement(Ns.W + "rPr",
                new XElement(Ns.W + "i"),
                XmlUtil.ValElement(Ns.W + "color", "404040")));

    private static XElement IntenseQuoteStyle() =>
        new(Ns.W + "style",
            new XAttribute(Ns.W + "type", "paragraph"),
            new XAttribute(Ns.W + "styleId", "IntenseQuote"),
            XmlUtil.ValElement(Ns.W + "name", "Intense Quote"),
            XmlUtil.ValElement(Ns.W + "basedOn", "Normal"),
            XmlUtil.ValElement(Ns.W + "next", "Normal"),
            new XElement(Ns.W + "qFormat"),
            new XElement(Ns.W + "pPr",
                new XElement(Ns.W + "pBdr",
                    ParagraphFormat.BorderElement(Ns.W + "left", BorderStyle.Thick,
                        OfficeNet.Core.Drawing.OfficeColor.FromRgb(0x2E, 0x54, 0x96),
                        OfficeNet.Core.Units.Pt(3))),
                new XElement(Ns.W + "spacing",
                    new XAttribute(Ns.W + "before", "160"),
                    new XAttribute(Ns.W + "after", "160")),
                new XElement(Ns.W + "ind", new XAttribute(Ns.W + "left", "360"))),
            new XElement(Ns.W + "rPr",
                new XElement(Ns.W + "i"),
                XmlUtil.ValElement(Ns.W + "color", "2E5496")));

    private static XElement CaptionStyle() =>
        new(Ns.W + "style",
            new XAttribute(Ns.W + "type", "paragraph"),
            new XAttribute(Ns.W + "styleId", "Caption"),
            XmlUtil.ValElement(Ns.W + "name", "caption"),
            XmlUtil.ValElement(Ns.W + "basedOn", "Normal"),
            XmlUtil.ValElement(Ns.W + "next", "Normal"),
            new XElement(Ns.W + "qFormat"),
            new XElement(Ns.W + "pPr",
                new XElement(Ns.W + "spacing",
                    new XAttribute(Ns.W + "after", "200")),
                XmlUtil.ValElement(Ns.W + "jc", "center")),
            new XElement(Ns.W + "rPr",
                new XElement(Ns.W + "i"),
                XmlUtil.ValElement(Ns.W + "color", "595959"),
                XmlUtil.ValElement(Ns.W + "sz", "18"),
                XmlUtil.ValElement(Ns.W + "szCs", "18")));

    private static XElement CodeStyle() =>
        new(Ns.W + "style",
            new XAttribute(Ns.W + "type", "paragraph"),
            new XAttribute(Ns.W + "styleId", "Code"),
            XmlUtil.ValElement(Ns.W + "name", "Code"),
            XmlUtil.ValElement(Ns.W + "basedOn", "Normal"),
            XmlUtil.ValElement(Ns.W + "next", "Normal"),
            new XElement(Ns.W + "qFormat"),
            new XElement(Ns.W + "pPr",
                new XElement(Ns.W + "shd",
                    new XAttribute(Ns.W + "val", "clear"),
                    new XAttribute(Ns.W + "color", "auto"),
                    new XAttribute(Ns.W + "fill", "F4F4F6")),
                new XElement(Ns.W + "spacing",
                    new XAttribute(Ns.W + "before", "60"),
                    new XAttribute(Ns.W + "after", "60"),
                    new XAttribute(Ns.W + "line", "240"),
                    new XAttribute(Ns.W + "lineRule", "auto")),
                new XElement(Ns.W + "ind", new XAttribute(Ns.W + "left", "240"))),
            new XElement(Ns.W + "rPr",
                new XElement(Ns.W + "rFonts",
                    new XAttribute(Ns.W + "ascii", "Consolas"),
                    new XAttribute(Ns.W + "hAnsi", "Consolas"),
                    new XAttribute(Ns.W + "cs", "Consolas")),
                XmlUtil.ValElement(Ns.W + "sz", "18"),
                XmlUtil.ValElement(Ns.W + "szCs", "18")));

    private static XElement TableGridStyle() =>
        new(Ns.W + "style",
            new XAttribute(Ns.W + "type", "table"),
            new XAttribute(Ns.W + "styleId", "TableGrid"),
            XmlUtil.ValElement(Ns.W + "name", "Table Grid"),
            XmlUtil.ValElement(Ns.W + "basedOn", "TableNormal"),
            new XElement(Ns.W + "uiPriority", new XAttribute(Ns.W + "val", "39")),
            new XElement(Ns.W + "tblPr",
                new XElement(Ns.W + "tblBorders",
                    TableBorder("top"), TableBorder("left"), TableBorder("bottom"),
                    TableBorder("right"), TableBorder("insideH"), TableBorder("insideV"))));

    private static XElement TableBorder(string name) =>
        new(Ns.W + name,
            new XAttribute(Ns.W + "val", "single"),
            new XAttribute(Ns.W + "sz", "4"),
            new XAttribute(Ns.W + "space", "0"),
            new XAttribute(Ns.W + "color", "auto"));

    internal static XDocument Settings()
    {
        var root = new XElement(Ns.W + "settings",
            new XAttribute(XNamespace.Xmlns + "w", Ns.W.NamespaceName),
            new XElement(Ns.W + "zoom", new XAttribute(Ns.W + "percent", "100")),
            new XElement(Ns.W + "defaultTabStop", new XAttribute(Ns.W + "val", "720")),
            new XElement(Ns.W + "characterSpacingControl",
                new XAttribute(Ns.W + "val", "doNotCompress")),
            new XElement(Ns.W + "compat",
                new XElement(Ns.W + "compatSetting",
                    new XAttribute(Ns.W + "name", "compatibilityMode"),
                    new XAttribute(Ns.W + "uri", "http://schemas.microsoft.com/office/word"),
                    // 15 is Word 2013 and later, which is what makes the modern layout engine
                    // apply. An absent or lower value puts Word into a legacy compatibility mode
                    // where spacing and table widths differ visibly.
                    new XAttribute(Ns.W + "val", "15"))));

        return XmlUtil.NewDocument(root);
    }

    internal static XDocument FontTable()
    {
        var root = new XElement(Ns.W + "fonts",
            new XAttribute(XNamespace.Xmlns + "w", Ns.W.NamespaceName),
            Font("Calibri", "swiss", "variable", "020F0502020204030204"),
            Font("Times New Roman", "roman", "variable", "02020603050405020304"),
            Font("Consolas", "modern", "fixed", "020B0609020204030204"));

        return XmlUtil.NewDocument(root);
    }

    private static XElement Font(string name, string family, string pitch, string panose) =>
        new(Ns.W + "font",
            new XAttribute(Ns.W + "name", name),
            new XElement(Ns.W + "panose1", new XAttribute(Ns.W + "val", panose)),
            new XElement(Ns.W + "family", new XAttribute(Ns.W + "val", family)),
            new XElement(Ns.W + "pitch", new XAttribute(Ns.W + "val", pitch)));

    internal static XDocument Numbering()
    {
        var root = new XElement(Ns.W + "numbering",
            new XAttribute(XNamespace.Xmlns + "w", Ns.W.NamespaceName));

        return XmlUtil.NewDocument(root);
    }

    /// <summary>
    /// An empty header or footer part.
    /// </summary>
    /// <remarks>
    /// Deliberately created with no paragraph. Word writes one, but so does the first
    /// <c>AddParagraph</c> call — and a template that ships one produces a stray blank line above
    /// every header a caller writes. The invariant that the part must not be empty is restored on
    /// save by <c>WordDocument.FlushToPackage</c>.
    /// </remarks>
    internal static XDocument HeaderOrFooter(bool isHeader)
    {
        var root = new XElement(Ns.W + (isHeader ? "hdr" : "ftr"),
            new XAttribute(XNamespace.Xmlns + "w", Ns.W.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "r", Ns.R.NamespaceName));

        return XmlUtil.NewDocument(root);
    }
}

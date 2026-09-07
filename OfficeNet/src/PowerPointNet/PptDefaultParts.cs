// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Xml.Linq;
using OfficeNet.Core;
using OfficeNet.Core.Xml;

namespace PowerPointNet;

/// <summary>
/// The parts a new .pptx needs before it holds a single slide.
/// </summary>
/// <remarks>
/// <para>
/// A presentation has a far higher floor than a document or a workbook. PowerPoint refuses to open
/// a package without a slide master, a slide layout the master points at, and a theme the master
/// references — three parts that exist only to make the fourth (a slide) legal. That is why this
/// file is the largest of the three default-part builders.
/// </para>
/// <para>
/// The theme is not decoration either. Every colour in a deck is written as a theme slot
/// (<c>accent1</c>, <c>dk1</c>, …) and resolved through <c>theme1.xml</c> at render time; a master
/// whose <c>clrMap</c> names a slot the theme does not define makes PowerPoint report the file as
/// needing repair.
/// </para>
/// </remarks>
internal static class PptDefaultParts
{
    /// <summary>Widescreen 16:9, the modern default: 13.333 x 7.5 inches.</summary>
    internal static readonly Length DefaultSlideWidth = Units.Inches(13.333);

    /// <summary>Widescreen 16:9 height.</summary>
    internal static readonly Length DefaultSlideHeight = Units.Inches(7.5);

    internal static XDocument Presentation(Length width, Length height)
    {
        var root = new XElement(Ns.P + "presentation",
            new XAttribute(XNamespace.Xmlns + "a", Ns.A.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "r", Ns.R.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "p", Ns.P.NamespaceName),
            new XAttribute("saveSubsetFonts", "1"),
            new XElement(Ns.P + "sldMasterIdLst"),
            new XElement(Ns.P + "sldIdLst"),
            new XElement(Ns.P + "sldSz",
                new XAttribute("cx", width.Emu),
                new XAttribute("cy", height.Emu)),
            // The notes page is portrait even when the slide is landscape; PowerPoint writes these
            // exact values and a mismatch shows up as a distorted notes layout.
            new XElement(Ns.P + "notesSz",
                new XAttribute("cx", Units.Inches(6.8).Emu),
                new XAttribute("cy", Units.Inches(9.14).Emu)),
            DefaultTextStyles());

        return XmlUtil.NewDocument(root);
    }

    private static XElement DefaultTextStyles()
    {
        var styles = new XElement(Ns.P + "defaultTextStyle");

        // defPPr plus nine indent levels is what PowerPoint writes. A body placeholder inherits
        // its bullet indents from here when its own list style says nothing.
        styles.Add(new XElement(Ns.A + "defPPr",
            new XElement(Ns.A + "defRPr", new XAttribute("lang", "en-US"))));

        for (var level = 1; level <= 9; level++)
        {
            styles.Add(new XElement(Ns.A + $"lvl{level}pPr",
                new XAttribute("marL", Units.Inches(0.5).Emu * (level - 1)),
                new XAttribute("algn", "l"),
                new XAttribute("defTabSz", Units.Inches(0.5).Emu),
                new XAttribute("rtl", "0"),
                new XAttribute("eaLnBrk", "1"),
                new XAttribute("latinLnBrk", "0"),
                new XAttribute("hangingPunct", "1"),
                new XElement(Ns.A + "defRPr",
                    new XAttribute("sz", "1800"),
                    new XAttribute("kern", "1200"),
                    new XElement(Ns.A + "solidFill",
                        new XElement(Ns.A + "schemeClr", new XAttribute("val", "tx1"))),
                    new XElement(Ns.A + "latin", new XAttribute("typeface", "+mn-lt")))));
        }

        return styles;
    }

    internal static XDocument SlideMaster()
    {
        var root = new XElement(Ns.P + "sldMaster",
            new XAttribute(XNamespace.Xmlns + "a", Ns.A.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "r", Ns.R.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "p", Ns.P.NamespaceName),
            new XElement(Ns.P + "cSld",
                new XElement(Ns.P + "bg",
                    new XElement(Ns.P + "bgRef",
                        new XAttribute("idx", "1001"),
                        new XElement(Ns.A + "schemeClr", new XAttribute("val", "bg1")))),
                EmptyShapeTree()),
            // The colour map binds the master's roles to the theme's slots. Getting dk1/lt1
            // backwards renders white text on white.
            new XElement(Ns.P + "clrMap",
                new XAttribute("bg1", "lt1"),
                new XAttribute("tx1", "dk1"),
                new XAttribute("bg2", "lt2"),
                new XAttribute("tx2", "dk2"),
                new XAttribute("accent1", "accent1"),
                new XAttribute("accent2", "accent2"),
                new XAttribute("accent3", "accent3"),
                new XAttribute("accent4", "accent4"),
                new XAttribute("accent5", "accent5"),
                new XAttribute("accent6", "accent6"),
                new XAttribute("hlink", "hlink"),
                new XAttribute("folHlink", "folHlink")),
            new XElement(Ns.P + "sldLayoutIdLst"),
            MasterTextStyles());

        return XmlUtil.NewDocument(root);
    }

    private static XElement MasterTextStyles()
    {
        var styles = new XElement(Ns.P + "txStyles");

        styles.Add(BuildStyle("titleStyle", 4400, bold: false, bullet: false));
        styles.Add(BuildStyle("bodyStyle", 2800, bold: false, bullet: true));
        styles.Add(BuildStyle("otherStyle", 1800, bold: false, bullet: false));

        return styles;
    }

    private static XElement BuildStyle(string name, int firstLevelSize, bool bold, bool bullet)
    {
        var style = new XElement(Ns.P + name);

        for (var level = 1; level <= 9; level++)
        {
            // Sizes step down 400 hundredths (4 pt) per level, floored so deep levels stay legible.
            var size = Math.Max(1200, firstLevelSize - (level - 1) * 400);

            var paragraph = new XElement(Ns.A + $"lvl{level}pPr",
                new XAttribute("marL", bullet ? Units.Inches(0.34).Emu * level : 0),
                new XAttribute("indent", bullet ? -Units.Inches(0.34).Emu : 0),
                new XAttribute("algn", "l"),
                new XAttribute("defTabSz", Units.Inches(0.5).Emu),
                new XAttribute("rtl", "0"),
                new XAttribute("eaLnBrk", "1"),
                new XAttribute("latinLnBrk", "0"),
                new XAttribute("hangingPunct", "1"),
                new XElement(Ns.A + "lnSpc",
                    new XElement(Ns.A + "spcPct", new XAttribute("val", "90000"))),
                new XElement(Ns.A + "spcBef",
                    new XElement(Ns.A + "spcPts", new XAttribute("val", "1000"))));

            if (bullet)
            {
                paragraph.Add(new XElement(Ns.A + "buFont", new XAttribute("typeface", "Arial")));
                paragraph.Add(new XElement(Ns.A + "buChar", new XAttribute("char", "•")));
            }
            else
            {
                paragraph.Add(new XElement(Ns.A + "buNone"));
            }

            var run = new XElement(Ns.A + "defRPr",
                new XAttribute("sz", size),
                new XAttribute("kern", "1200"));

            if (bold)
            {
                run.SetAttributeValue("b", "1");
            }

            run.Add(new XElement(Ns.A + "solidFill",
                new XElement(Ns.A + "schemeClr", new XAttribute("val", "tx1"))));

            // "+mj-lt" is the theme's major (heading) latin font, "+mn-lt" the minor (body) one.
            run.Add(new XElement(Ns.A + "latin",
                new XAttribute("typeface", name == "titleStyle" ? "+mj-lt" : "+mn-lt")));

            paragraph.Add(run);
            style.Add(paragraph);
        }

        return style;
    }

    /// <summary>The layout kinds a new presentation ships with.</summary>
    internal static readonly (string Type, string Name)[] LayoutKinds =
    [
        ("title", "Title Slide"),
        ("obj", "Title and Content"),
        ("titleOnly", "Title Only"),
        ("blank", "Blank"),
        ("twoObj", "Two Content"),
        ("secHead", "Section Header"),
    ];

    internal static XDocument SlideLayout(string type, string name, Length width, Length height)
    {
        var tree = EmptyShapeTree();

        switch (type)
        {
            case "title":
                tree.Add(Placeholder("ctrTitle", null, 2,
                    Units.Inches(1.2), height * 0.32, width - Units.Inches(2.4), Units.Inches(1.6)));
                tree.Add(Placeholder("subTitle", "1", 3,
                    Units.Inches(1.8), height * 0.32 + Units.Inches(1.8),
                    width - Units.Inches(3.6), Units.Inches(1.4)));
                break;

            case "obj":
                tree.Add(Placeholder("title", null, 2,
                    Units.Inches(0.7), Units.Inches(0.5), width - Units.Inches(1.4), Units.Inches(1.1)));
                tree.Add(Placeholder("body", "1", 3,
                    Units.Inches(0.7), Units.Inches(1.8),
                    width - Units.Inches(1.4), height - Units.Inches(2.5)));
                break;

            case "twoObj":
                tree.Add(Placeholder("title", null, 2,
                    Units.Inches(0.7), Units.Inches(0.5), width - Units.Inches(1.4), Units.Inches(1.1)));
                tree.Add(Placeholder("body", "1", 3,
                    Units.Inches(0.7), Units.Inches(1.8),
                    (width - Units.Inches(1.7)) / 2, height - Units.Inches(2.5)));
                tree.Add(Placeholder("body", "2", 4,
                    Units.Inches(0.7) + (width - Units.Inches(1.7)) / 2 + Units.Inches(0.3),
                    Units.Inches(1.8), (width - Units.Inches(1.7)) / 2, height - Units.Inches(2.5)));
                break;

            case "secHead":
                tree.Add(Placeholder("title", null, 2,
                    Units.Inches(0.9), height * 0.4, width - Units.Inches(1.8), Units.Inches(1.4)));
                tree.Add(Placeholder("body", "1", 3,
                    Units.Inches(0.9), height * 0.4 + Units.Inches(1.5),
                    width - Units.Inches(1.8), Units.Inches(1.0)));
                break;

            case "titleOnly":
                tree.Add(Placeholder("title", null, 2,
                    Units.Inches(0.7), Units.Inches(0.5), width - Units.Inches(1.4), Units.Inches(1.1)));
                break;
        }

        var root = new XElement(Ns.P + "sldLayout",
            new XAttribute(XNamespace.Xmlns + "a", Ns.A.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "r", Ns.R.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "p", Ns.P.NamespaceName),
            new XAttribute("type", type),
            new XAttribute("preserve", "1"),
            new XElement(Ns.P + "cSld",
                new XAttribute("name", name),
                tree));

        return XmlUtil.NewDocument(root);
    }

    /// <summary>Builds a placeholder shape for a layout.</summary>
    private static XElement Placeholder(string type, string? index, uint id,
        Length x, Length y, Length width, Length height)
    {
        var placeholder = new XElement(Ns.P + "ph", new XAttribute("type", type));

        // idx binds a slide's placeholder to the layout's. Two placeholders sharing an idx makes
        // PowerPoint pick one arbitrarily and the other loses its position.
        if (index is not null)
        {
            placeholder.SetAttributeValue("idx", index);
        }

        var alignment = type is "ctrTitle" or "subTitle" ? "ctr" : "l";

        return new XElement(Ns.P + "sp",
            new XElement(Ns.P + "nvSpPr",
                new XElement(Ns.P + "cNvPr",
                    new XAttribute("id", id),
                    new XAttribute("name", $"{type} Placeholder {id}")),
                new XElement(Ns.P + "cNvSpPr",
                    new XElement(Ns.A + "spLocks", new XAttribute("noGrp", "1"))),
                new XElement(Ns.P + "nvPr", placeholder)),
            new XElement(Ns.P + "spPr",
                new XElement(Ns.A + "xfrm",
                    new XElement(Ns.A + "off", new XAttribute("x", x.Emu), new XAttribute("y", y.Emu)),
                    new XElement(Ns.A + "ext", new XAttribute("cx", width.Emu),
                        new XAttribute("cy", height.Emu)))),
            new XElement(Ns.P + "txBody",
                new XElement(Ns.A + "bodyPr"),
                new XElement(Ns.A + "lstStyle"),
                new XElement(Ns.A + "p",
                    new XElement(Ns.A + "pPr", new XAttribute("algn", alignment)),
                    new XElement(Ns.A + "endParaRPr", new XAttribute("lang", "en-US")))));
    }

    internal static XDocument Slide()
    {
        var root = new XElement(Ns.P + "sld",
            new XAttribute(XNamespace.Xmlns + "a", Ns.A.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "r", Ns.R.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "p", Ns.P.NamespaceName),
            new XElement(Ns.P + "cSld", EmptyShapeTree()));

        return XmlUtil.NewDocument(root);
    }

    /// <summary>
    /// An empty shape tree.
    /// </summary>
    /// <remarks>
    /// The two mandatory children are not optional boilerplate. Every <c>p:spTree</c> must open
    /// with a non-visual group properties element and a group shape properties element describing
    /// the tree's own transform, and PowerPoint rejects a slide missing either.
    /// </remarks>
    internal static XElement EmptyShapeTree() =>
        new(Ns.P + "spTree",
            new XElement(Ns.P + "nvGrpSpPr",
                new XElement(Ns.P + "cNvPr",
                    new XAttribute("id", "1"),
                    new XAttribute("name", "")),
                new XElement(Ns.P + "cNvGrpSpPr"),
                new XElement(Ns.P + "nvPr")),
            new XElement(Ns.P + "grpSpPr",
                new XElement(Ns.A + "xfrm",
                    new XElement(Ns.A + "off", new XAttribute("x", "0"), new XAttribute("y", "0")),
                    new XElement(Ns.A + "ext", new XAttribute("cx", "0"), new XAttribute("cy", "0")),
                    new XElement(Ns.A + "chOff", new XAttribute("x", "0"), new XAttribute("y", "0")),
                    new XElement(Ns.A + "chExt", new XAttribute("cx", "0"),
                        new XAttribute("cy", "0")))));

    internal static XDocument NotesSlide()
    {
        var tree = EmptyShapeTree();

        tree.Add(new XElement(Ns.P + "sp",
            new XElement(Ns.P + "nvSpPr",
                new XElement(Ns.P + "cNvPr",
                    new XAttribute("id", "2"),
                    new XAttribute("name", "Notes Placeholder 2")),
                new XElement(Ns.P + "cNvSpPr",
                    new XElement(Ns.A + "spLocks", new XAttribute("noGrp", "1"))),
                new XElement(Ns.P + "nvPr",
                    new XElement(Ns.P + "ph",
                        new XAttribute("type", "body"),
                        new XAttribute("idx", "1")))),
            new XElement(Ns.P + "spPr"),
            new XElement(Ns.P + "txBody",
                new XElement(Ns.A + "bodyPr"),
                new XElement(Ns.A + "lstStyle"),
                new XElement(Ns.A + "p"))));

        var root = new XElement(Ns.P + "notes",
            new XAttribute(XNamespace.Xmlns + "a", Ns.A.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "r", Ns.R.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "p", Ns.P.NamespaceName),
            new XElement(Ns.P + "cSld", tree));

        return XmlUtil.NewDocument(root);
    }

    internal static XDocument Theme(string name = "OfficeNet")
    {
        var scheme = new XElement(Ns.A + "clrScheme", new XAttribute("name", name));

        // dk1 and lt1 are system colours in every Office theme; the rest are literal. Writing dk1
        // as an srgbClr works but loses the high-contrast accessibility behaviour.
        scheme.Add(new XElement(Ns.A + "dk1",
            new XElement(Ns.A + "sysClr",
                new XAttribute("val", "windowText"),
                new XAttribute("lastClr", "000000"))));

        scheme.Add(new XElement(Ns.A + "lt1",
            new XElement(Ns.A + "sysClr",
                new XAttribute("val", "window"),
                new XAttribute("lastClr", "FFFFFF"))));

        scheme.Add(Color("dk2", "1F3864"));
        scheme.Add(Color("lt2", "F2F5FA"));
        scheme.Add(Color("accent1", "2E5496"));
        scheme.Add(Color("accent2", "C55A11"));
        scheme.Add(Color("accent3", "548235"));
        scheme.Add(Color("accent4", "BF9000"));
        scheme.Add(Color("accent5", "7030A0"));
        scheme.Add(Color("accent6", "C00000"));
        scheme.Add(Color("hlink", "0563C1"));
        scheme.Add(Color("folHlink", "954F72"));

        var fontScheme = new XElement(Ns.A + "fontScheme",
            new XAttribute("name", name),
            new XElement(Ns.A + "majorFont",
                new XElement(Ns.A + "latin", new XAttribute("typeface", "Calibri Light")),
                new XElement(Ns.A + "ea", new XAttribute("typeface", "")),
                new XElement(Ns.A + "cs", new XAttribute("typeface", ""))),
            new XElement(Ns.A + "minorFont",
                new XElement(Ns.A + "latin", new XAttribute("typeface", "Calibri")),
                new XElement(Ns.A + "ea", new XAttribute("typeface", "")),
                new XElement(Ns.A + "cs", new XAttribute("typeface", ""))));

        var root = new XElement(Ns.A + "theme",
            new XAttribute(XNamespace.Xmlns + "a", Ns.A.NamespaceName),
            new XAttribute("name", name),
            new XElement(Ns.A + "themeElements",
                scheme,
                fontScheme,
                FormatScheme()),
            new XElement(Ns.A + "objectDefaults"),
            new XElement(Ns.A + "extraClrSchemeLst"));

        return XmlUtil.NewDocument(root);
    }

    private static XElement Color(string slot, string hex) =>
        new(Ns.A + slot, new XElement(Ns.A + "srgbClr", new XAttribute("val", hex)));

    /// <summary>
    /// The theme's fill, line and effect styles.
    /// </summary>
    /// <remarks>
    /// Each list must hold exactly three entries — subtle, moderate, intense — and a shape's
    /// <c>idx</c> is a one-based index into them. A list with two entries makes every shape using
    /// index 3 fall back to no fill, which looks like the shapes vanished.
    /// </remarks>
    private static XElement FormatScheme()
    {
        static XElement PhaseFill(int shade) =>
            new(Ns.A + "solidFill",
                new XElement(Ns.A + "schemeClr",
                    new XAttribute("val", "phClr"),
                    shade == 0
                        ? null
                        : new XElement(Ns.A + "shade", new XAttribute("val", shade.ToString()))));

        static XElement Line(int width) =>
            new(Ns.A + "ln",
                new XAttribute("w", width),
                new XAttribute("cap", "flat"),
                new XAttribute("cmpd", "sng"),
                new XAttribute("algn", "ctr"),
                new XElement(Ns.A + "solidFill",
                    new XElement(Ns.A + "schemeClr", new XAttribute("val", "phClr"))),
                new XElement(Ns.A + "prstDash", new XAttribute("val", "solid")));

        return new XElement(Ns.A + "fmtScheme",
            new XAttribute("name", "OfficeNet"),
            new XElement(Ns.A + "fillStyleLst",
                PhaseFill(0), PhaseFill(0), PhaseFill(0)),
            new XElement(Ns.A + "lnStyleLst",
                Line(6350), Line(12700), Line(19050)),
            new XElement(Ns.A + "effectStyleLst",
                new XElement(Ns.A + "effectStyle", new XElement(Ns.A + "effectLst")),
                new XElement(Ns.A + "effectStyle", new XElement(Ns.A + "effectLst")),
                new XElement(Ns.A + "effectStyle", new XElement(Ns.A + "effectLst"))),
            new XElement(Ns.A + "bgFillStyleLst",
                PhaseFill(0), PhaseFill(0), PhaseFill(0)));
    }

    internal static XDocument PresentationProperties() =>
        XmlUtil.NewDocument(new XElement(Ns.P + "presentationPr",
            new XAttribute(XNamespace.Xmlns + "a", Ns.A.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "r", Ns.R.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "p", Ns.P.NamespaceName)));

    internal static XDocument ViewProperties() =>
        XmlUtil.NewDocument(new XElement(Ns.P + "viewPr",
            new XAttribute(XNamespace.Xmlns + "a", Ns.A.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "r", Ns.R.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "p", Ns.P.NamespaceName)));

    internal static XDocument TableStyles() =>
        XmlUtil.NewDocument(new XElement(Ns.A + "tblStyleLst",
            new XAttribute(XNamespace.Xmlns + "a", Ns.A.NamespaceName),
            // The default style GUID is fixed by the format; a table referencing an undefined
            // style renders unformatted.
            new XAttribute("def", "{5C22544A-7EE6-4342-B048-85BDC9FD1C3A}")));
}

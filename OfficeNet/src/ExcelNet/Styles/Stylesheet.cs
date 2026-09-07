// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Globalization;
using System.Xml.Linq;
using OfficeNet.Core.Drawing;
using OfficeNet.Core.Xml;

namespace ExcelNet.Styles;

/// <summary>
/// The workbook's format table: <c>xl/styles.xml</c> in and out of <see cref="CellStyle"/> values.
/// </summary>
/// <remarks>
/// <para>
/// SpreadsheetML's style model is four parallel tables — fonts, fills, borders, number formats —
/// plus a fifth (<c>cellXfs</c>) whose entries combine one index from each. A cell carries only the
/// <c>cellXfs</c> index. This class hides all five behind a value type, deduplicating as it goes so
/// that formatting ten thousand cells identically costs one entry rather than ten thousand.
/// </para>
/// <para>
/// Two entries are reserved and must exist before anything else. Fill index 0 must be
/// <c>none</c> and index 1 must be <c>gray125</c> — Excel hard-codes both, and a stylesheet whose
/// first fill is a real colour renders every unfilled cell in that colour.
/// </para>
/// </remarks>
public sealed class Stylesheet
{
    private static readonly XNamespace S = Ns.S;
    private const int FirstCustomNumberFormatId = 164;

    private readonly List<CellFont> _fonts = [];
    private readonly List<(OfficeColor? Color, FillPattern Pattern)> _fills = [];
    private readonly List<CellBorder> _borders = [];
    private readonly List<string> _customFormats = [];
    private readonly List<CellStyle> _cellFormats = [];
    private readonly Dictionary<CellStyle, int> _index = [];

    private Stylesheet()
    {
    }

    /// <summary>The number of distinct cell formats.</summary>
    public int Count => _cellFormats.Count;

    /// <summary>Creates a stylesheet holding only the mandatory defaults.</summary>
    public static Stylesheet CreateDefault()
    {
        var sheet = new Stylesheet();

        sheet._fonts.Add(CellFont.Default);

        // Reserved: index 0 must be "none" and index 1 "gray125".
        sheet._fills.Add((null, FillPattern.None));
        sheet._fills.Add((null, FillPattern.Gray125));

        sheet._borders.Add(CellBorder.None);

        // cellXfs index 0 is the default format every unstyled cell points at.
        sheet._cellFormats.Add(CellStyle.Default);
        sheet._index[CellStyle.Default] = 0;

        return sheet;
    }

    /// <summary>Reads an existing <c>xl/styles.xml</c>.</summary>
    public static Stylesheet Read(XElement root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var sheet = new Stylesheet();

        // Custom number formats are keyed by id, not by position, and the ids are not dense.
        var formatCodes = new Dictionary<int, string>();

        foreach (var element in root.Element(S + "numFmts")?.Elements(S + "numFmt") ?? [])
        {
            var id = element.IntAttr("numFmtId", -1);
            var code = element.Attr("formatCode");

            if (id >= 0 && code is not null)
            {
                formatCodes[id] = code;
            }
        }

        foreach (var element in root.Element(S + "fonts")?.Elements(S + "font") ?? [])
        {
            sheet._fonts.Add(ReadFont(element));
        }

        if (sheet._fonts.Count == 0)
        {
            sheet._fonts.Add(CellFont.Default);
        }

        foreach (var element in root.Element(S + "fills")?.Elements(S + "fill") ?? [])
        {
            sheet._fills.Add(ReadFill(element));
        }

        while (sheet._fills.Count < 2)
        {
            sheet._fills.Add(sheet._fills.Count == 0 ? (null, FillPattern.None) : (null, FillPattern.Gray125));
        }

        foreach (var element in root.Element(S + "borders")?.Elements(S + "border") ?? [])
        {
            sheet._borders.Add(ReadBorder(element));
        }

        if (sheet._borders.Count == 0)
        {
            sheet._borders.Add(CellBorder.None);
        }

        foreach (var element in root.Element(S + "cellXfs")?.Elements(S + "xf") ?? [])
        {
            sheet._cellFormats.Add(sheet.ReadCellFormat(element, formatCodes));
        }

        if (sheet._cellFormats.Count == 0)
        {
            sheet._cellFormats.Add(CellStyle.Default);
        }

        // Later duplicates keep their own index because cells already point at them; only the
        // first occurrence is registered for deduplicating new styles.
        for (var i = 0; i < sheet._cellFormats.Count; i++)
        {
            sheet._index.TryAdd(sheet._cellFormats[i], i);
        }

        sheet._customFormats.AddRange(formatCodes
            .Where(kv => kv.Key >= FirstCustomNumberFormatId)
            .OrderBy(kv => kv.Key)
            .Select(kv => kv.Value));

        return sheet;
    }

    /// <summary>Returns the index for a style, adding it when it is new.</summary>
    public int IndexOf(CellStyle style)
    {
        if (_index.TryGetValue(style, out var existing))
        {
            return existing;
        }

        if (!_fonts.Contains(style.Font))
        {
            _fonts.Add(style.Font);
        }

        var fill = (style.BackgroundColor, style.BackgroundColor is null
            ? FillPattern.None
            : style.Pattern);

        if (!_fills.Contains(fill))
        {
            _fills.Add(fill);
        }

        if (!_borders.Contains(style.Border))
        {
            _borders.Add(style.Border);
        }

        if (style.NumberFormat is { Length: > 0 } code &&
            NumberFormats.BuiltInId(code) is null && !_customFormats.Contains(code))
        {
            _customFormats.Add(code);
        }

        var index = _cellFormats.Count;
        _cellFormats.Add(style);
        _index[style] = index;
        return index;
    }

    /// <summary>Returns the style at an index, or the default when the index is unknown.</summary>
    public CellStyle StyleAt(int index) =>
        index >= 0 && index < _cellFormats.Count ? _cellFormats[index] : CellStyle.Default;

    /// <summary>True when a style index makes its cells display as dates.</summary>
    public bool IsDateStyle(int index) => NumberFormats.IsDateFormat(StyleAt(index).NumberFormat);

    private int NumberFormatId(string? code)
    {
        if (string.IsNullOrEmpty(code))
        {
            return 0;
        }

        if (NumberFormats.BuiltInId(code) is { } builtIn)
        {
            return builtIn;
        }

        var custom = _customFormats.IndexOf(code);
        return custom < 0 ? 0 : FirstCustomNumberFormatId + custom;
    }

    // ---- Reading -------------------------------------------------------------------------------

    private static CellFont ReadFont(XElement element)
    {
        var name = element.Element(S + "name")?.Attr("val")
                   ?? element.Element(S + "rFont")?.Attr("val")
                   ?? "Calibri";

        var size = element.Element(S + "sz")?.Attr("val") is { } sizeText &&
                   double.TryParse(sizeText, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 11;

        // A boolean style element is on when present unless it says val="0"; <b/> means bold.
        static bool Toggle(XElement parent, string name) =>
            parent.Element(S + name) is { } child &&
            (child.Attr("val") is null || XmlUtil.OoxmlBool(child.Attr("val")) == true);

        return new CellFont(
            name,
            size,
            Toggle(element, "b"),
            Toggle(element, "i"),
            element.Element(S + "u") is not null,
            Toggle(element, "strike"),
            ReadColor(element.Element(S + "color")));
    }

    private static (OfficeColor?, FillPattern) ReadFill(XElement element)
    {
        var pattern = element.Element(S + "patternFill");
        var type = pattern?.Attr("patternType") ?? "none";

        var color = ReadColor(pattern?.Element(S + "fgColor"));

        return (color, type switch
        {
            "solid" => FillPattern.Solid,
            "gray125" => FillPattern.Gray125,
            "gray0625" => FillPattern.Gray0625,
            _ => FillPattern.None,
        });
    }

    private static CellBorder ReadBorder(XElement element)
    {
        static BorderLineStyle Edge(XElement? parent, string name) =>
            parent?.Element(S + name)?.Attr("style") switch
            {
                "hair" => BorderLineStyle.Hair,
                "thin" => BorderLineStyle.Thin,
                "medium" => BorderLineStyle.Medium,
                "thick" => BorderLineStyle.Thick,
                "double" => BorderLineStyle.Double,
                "dotted" => BorderLineStyle.Dotted,
                "dashed" => BorderLineStyle.Dashed,
                "dashDot" => BorderLineStyle.DashDot,
                "dashDotDot" => BorderLineStyle.DashDotDot,
                _ => BorderLineStyle.None,
            };

        var color = ReadColor(element.Element(S + "left")?.Element(S + "color"))
                    ?? ReadColor(element.Element(S + "top")?.Element(S + "color"));

        return new CellBorder(
            Edge(element, "left"),
            Edge(element, "right"),
            Edge(element, "top"),
            Edge(element, "bottom"),
            color);
    }

    private static OfficeColor? ReadColor(XElement? element)
    {
        if (element is null)
        {
            return null;
        }

        var rgb = element.Attr("rgb");

        if (rgb is { Length: 8 })
        {
            // An ARGB value; the alpha byte is ignored because cells are opaque.
            return OfficeColor.TryParse(rgb[2..], out var color) ? color : null;
        }

        if (rgb is not null && OfficeColor.TryParse(rgb, out var plain))
        {
            return plain;
        }

        // A theme colour needs the theme part to resolve. Returning null means "inherit", which
        // renders correctly; guessing a literal colour would not.
        return null;
    }

    private CellStyle ReadCellFormat(XElement element, Dictionary<int, string> formatCodes)
    {
        var fontId = element.IntAttr("fontId");
        var fillId = element.IntAttr("fillId");
        var borderId = element.IntAttr("borderId");
        var numberFormatId = element.IntAttr("numFmtId");

        var (fillColor, pattern) = fillId >= 0 && fillId < _fills.Count
            ? _fills[fillId]
            : (null, FillPattern.None);

        var alignment = element.Element(S + "alignment");

        return new CellStyle
        {
            Font = fontId >= 0 && fontId < _fonts.Count ? _fonts[fontId] : CellFont.Default,
            BackgroundColor = pattern == FillPattern.None ? null : fillColor,
            Pattern = pattern,
            Border = borderId >= 0 && borderId < _borders.Count ? _borders[borderId] : CellBorder.None,
            NumberFormat = formatCodes.TryGetValue(numberFormatId, out var custom)
                ? custom
                : BuiltInCode(numberFormatId),
            Horizontal = alignment?.Attr("horizontal") switch
            {
                "left" => HorizontalAlignment.Left,
                "center" => HorizontalAlignment.Center,
                "right" => HorizontalAlignment.Right,
                "fill" => HorizontalAlignment.Fill,
                "justify" => HorizontalAlignment.Justify,
                "centerContinuous" => HorizontalAlignment.CenterContinuous,
                "distributed" => HorizontalAlignment.Distributed,
                _ => HorizontalAlignment.General,
            },
            Vertical = alignment?.Attr("vertical") switch
            {
                "top" => VerticalAlignment.Top,
                "center" => VerticalAlignment.Center,
                "justify" => VerticalAlignment.Justify,
                "distributed" => VerticalAlignment.Distributed,
                _ => VerticalAlignment.Bottom,
            },
            WrapText = XmlUtil.OoxmlBool(alignment?.Attr("wrapText")) == true,
            ShrinkToFit = XmlUtil.OoxmlBool(alignment?.Attr("shrinkToFit")) == true,
            TextRotation = alignment.IntAttr("textRotation"),
            Indent = alignment.IntAttr("indent"),
            Locked = XmlUtil.OoxmlBool(element.Element(S + "protection")?.Attr("locked")) != false,
        };
    }

    private static string? BuiltInCode(int id) => id switch
    {
        0 => null,
        1 => "0",
        2 => "0.00",
        3 => "#,##0",
        4 => "#,##0.00",
        9 => "0%",
        10 => "0.00%",
        11 => "0.00E+00",
        14 => "mm-dd-yy",
        15 => "d-mmm-yy",
        16 => "d-mmm",
        17 => "mmm-yy",
        18 => "h:mm AM/PM",
        19 => "h:mm:ss AM/PM",
        20 => "h:mm",
        21 => "h:mm:ss",
        22 => "m/d/yy h:mm",
        45 => "mm:ss",
        46 => "[h]:mm:ss",
        47 => "mmss.0",
        49 => "@",
        _ => null,
    };

    // ---- Writing -------------------------------------------------------------------------------

    /// <summary>Serialises the stylesheet as an <c>xl/styles.xml</c> document.</summary>
    public XDocument ToXml()
    {
        var root = new XElement(S + "styleSheet",
            new XAttribute("xmlns", S.NamespaceName));

        if (_customFormats.Count > 0)
        {
            var formats = new XElement(S + "numFmts",
                new XAttribute("count", _customFormats.Count));

            for (var i = 0; i < _customFormats.Count; i++)
            {
                formats.Add(new XElement(S + "numFmt",
                    new XAttribute("numFmtId", FirstCustomNumberFormatId + i),
                    new XAttribute("formatCode", _customFormats[i])));
            }

            root.Add(formats);
        }

        root.Add(new XElement(S + "fonts",
            new XAttribute("count", _fonts.Count),
            _fonts.Select(WriteFont)));

        root.Add(new XElement(S + "fills",
            new XAttribute("count", _fills.Count),
            _fills.Select(f => WriteFill(f.Color, f.Pattern))));

        root.Add(new XElement(S + "borders",
            new XAttribute("count", _borders.Count),
            _borders.Select(WriteBorder)));

        // cellStyleXfs must exist and hold at least the one entry that cellXfs entries inherit
        // from. Omitting it makes Excel repair the file on open.
        root.Add(new XElement(S + "cellStyleXfs",
            new XAttribute("count", 1),
            new XElement(S + "xf",
                new XAttribute("numFmtId", 0),
                new XAttribute("fontId", 0),
                new XAttribute("fillId", 0),
                new XAttribute("borderId", 0))));

        root.Add(new XElement(S + "cellXfs",
            new XAttribute("count", _cellFormats.Count),
            _cellFormats.Select(WriteCellFormat)));

        root.Add(new XElement(S + "cellStyles",
            new XAttribute("count", 1),
            new XElement(S + "cellStyle",
                new XAttribute("name", "Normal"),
                new XAttribute("xfId", 0),
                new XAttribute("builtinId", 0))));

        return XmlUtil.NewDocument(root);
    }

    private static XElement WriteFont(CellFont font)
    {
        var element = new XElement(S + "font");

        if (font.Bold)
        {
            element.Add(new XElement(S + "b"));
        }

        if (font.Italic)
        {
            element.Add(new XElement(S + "i"));
        }

        if (font.Strike)
        {
            element.Add(new XElement(S + "strike"));
        }

        if (font.Underline)
        {
            element.Add(new XElement(S + "u"));
        }

        element.Add(new XElement(S + "sz",
            new XAttribute("val", font.SizePoints.ToString("0.##", CultureInfo.InvariantCulture))));

        if (font.Color is { } color)
        {
            element.Add(new XElement(S + "color", new XAttribute("rgb", "FF" + color.ToHex())));
        }

        element.Add(new XElement(S + "name", new XAttribute("val", font.Name)));
        element.Add(new XElement(S + "family", new XAttribute("val", 2)));

        return element;
    }

    private static XElement WriteFill(OfficeColor? color, FillPattern pattern)
    {
        var patternType = pattern switch
        {
            FillPattern.Solid => "solid",
            FillPattern.Gray125 => "gray125",
            FillPattern.Gray0625 => "gray0625",
            _ => "none",
        };

        var fill = new XElement(S + "patternFill", new XAttribute("patternType", patternType));

        if (color is { } value && pattern == FillPattern.Solid)
        {
            // Excel reads the foreground colour for a solid fill, not the background one — a solid
            // fill whose colour is written as bgColor renders white.
            fill.Add(new XElement(S + "fgColor", new XAttribute("rgb", "FF" + value.ToHex())));
            fill.Add(new XElement(S + "bgColor", new XAttribute("indexed", 64)));
        }

        return new XElement(S + "fill", fill);
    }

    private static XElement WriteBorder(CellBorder border)
    {
        var element = new XElement(S + "border");

        // The edge elements must appear in this order, and all five must be present even when
        // empty; Excel repairs a border missing its diagonal element.
        element.Add(WriteEdge("left", border.Left, border.Color));
        element.Add(WriteEdge("right", border.Right, border.Color));
        element.Add(WriteEdge("top", border.Top, border.Color));
        element.Add(WriteEdge("bottom", border.Bottom, border.Color));
        element.Add(new XElement(S + "diagonal"));

        return element;
    }

    private static XElement WriteEdge(string name, BorderLineStyle style, OfficeColor? color)
    {
        var element = new XElement(S + name);

        if (style == BorderLineStyle.None)
        {
            return element;
        }

        element.Add(new XAttribute("style", style switch
        {
            BorderLineStyle.Hair => "hair",
            BorderLineStyle.Medium => "medium",
            BorderLineStyle.Thick => "thick",
            BorderLineStyle.Double => "double",
            BorderLineStyle.Dotted => "dotted",
            BorderLineStyle.Dashed => "dashed",
            BorderLineStyle.DashDot => "dashDot",
            BorderLineStyle.DashDotDot => "dashDotDot",
            _ => "thin",
        }));

        element.Add(new XElement(S + "color",
            color is { } value
                ? new XAttribute("rgb", "FF" + value.ToHex())
                : new XAttribute("indexed", 64)));

        return element;
    }

    private XElement WriteCellFormat(CellStyle style)
    {
        var fontId = Math.Max(0, _fonts.IndexOf(style.Font));

        var fill = (style.BackgroundColor, style.BackgroundColor is null
            ? FillPattern.None
            : style.Pattern);

        var fillId = Math.Max(0, _fills.IndexOf(fill));
        var borderId = Math.Max(0, _borders.IndexOf(style.Border));
        var numberFormatId = NumberFormatId(style.NumberFormat);

        var element = new XElement(S + "xf",
            new XAttribute("numFmtId", numberFormatId),
            new XAttribute("fontId", fontId),
            new XAttribute("fillId", fillId),
            new XAttribute("borderId", borderId),
            new XAttribute("xfId", 0));

        // Excel ignores a font, fill, border or format unless the matching applyX flag is set.
        // This is the single most common reason hand-written styles "do nothing".
        if (numberFormatId != 0)
        {
            element.Add(new XAttribute("applyNumberFormat", 1));
        }

        if (fontId != 0)
        {
            element.Add(new XAttribute("applyFont", 1));
        }

        if (fillId > 1)
        {
            element.Add(new XAttribute("applyFill", 1));
        }

        if (borderId != 0)
        {
            element.Add(new XAttribute("applyBorder", 1));
        }

        var needsAlignment = style.Horizontal != HorizontalAlignment.General ||
                             style.Vertical != VerticalAlignment.Bottom ||
                             style.WrapText || style.ShrinkToFit ||
                             style.TextRotation != 0 || style.Indent != 0;

        if (needsAlignment)
        {
            var alignment = new XElement(S + "alignment");

            if (style.Horizontal != HorizontalAlignment.General)
            {
                alignment.Add(new XAttribute("horizontal", style.Horizontal switch
                {
                    HorizontalAlignment.Left => "left",
                    HorizontalAlignment.Center => "center",
                    HorizontalAlignment.Right => "right",
                    HorizontalAlignment.Fill => "fill",
                    HorizontalAlignment.Justify => "justify",
                    HorizontalAlignment.CenterContinuous => "centerContinuous",
                    _ => "distributed",
                }));
            }

            if (style.Vertical != VerticalAlignment.Bottom)
            {
                alignment.Add(new XAttribute("vertical", style.Vertical switch
                {
                    VerticalAlignment.Top => "top",
                    VerticalAlignment.Center => "center",
                    VerticalAlignment.Justify => "justify",
                    _ => "distributed",
                }));
            }

            if (style.WrapText)
            {
                alignment.Add(new XAttribute("wrapText", 1));
            }

            if (style.ShrinkToFit)
            {
                alignment.Add(new XAttribute("shrinkToFit", 1));
            }

            if (style.TextRotation != 0)
            {
                // Negative rotations are encoded as 90 plus the magnitude: -45 degrees is 135.
                var rotation = style.TextRotation < 0 ? 90 - style.TextRotation : style.TextRotation;
                alignment.Add(new XAttribute("textRotation", rotation));
            }

            if (style.Indent != 0)
            {
                alignment.Add(new XAttribute("indent", style.Indent));
            }

            element.Add(new XAttribute("applyAlignment", 1));
            element.Add(alignment);
        }

        if (!style.Locked)
        {
            element.Add(new XAttribute("applyProtection", 1));
            element.Add(new XElement(S + "protection", new XAttribute("locked", 0)));
        }

        return element;
    }
}

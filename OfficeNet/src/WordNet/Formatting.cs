// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Xml.Linq;
using OfficeNet.Core;
using OfficeNet.Core.Drawing;
using OfficeNet.Core.Xml;

namespace WordNet;

/// <summary>How a paragraph's lines are aligned between the margins.</summary>
public enum ParagraphAlignment
{
    /// <summary>Aligned to the left margin (or the right, in a right-to-left paragraph).</summary>
    Left,

    /// <summary>Centred.</summary>
    Center,

    /// <summary>Aligned to the right margin.</summary>
    Right,

    /// <summary>Stretched to both margins.</summary>
    Justify,

    /// <summary>Stretched to both margins including the last line.</summary>
    Distribute,
}

/// <summary>How the space between lines is measured.</summary>
public enum LineSpacingRule
{
    /// <summary>A multiple of single spacing, growing when a line contains larger text.</summary>
    Multiple,

    /// <summary>An exact height; larger text is clipped.</summary>
    Exact,

    /// <summary>A minimum height that grows for larger text.</summary>
    AtLeast,
}

/// <summary>The underline styles WordprocessingML defines.</summary>
public enum UnderlineStyle
{
    /// <summary>No underline.</summary>
    None,

    /// <summary>A single line.</summary>
    Single,

    /// <summary>A double line.</summary>
    Double,

    /// <summary>A thick line.</summary>
    Thick,

    /// <summary>A dotted line.</summary>
    Dotted,

    /// <summary>A dashed line.</summary>
    Dash,

    /// <summary>A wavy line.</summary>
    Wave,

    /// <summary>A line under words but not under the spaces between them.</summary>
    Words,
}

/// <summary>Vertical position of a run relative to the baseline.</summary>
public enum VerticalAlignment
{
    /// <summary>On the baseline.</summary>
    Baseline,

    /// <summary>Raised and reduced.</summary>
    Superscript,

    /// <summary>Lowered and reduced.</summary>
    Subscript,
}

/// <summary>A paragraph or table border edge.</summary>
public enum BorderStyle
{
    /// <summary>No border.</summary>
    None,

    /// <summary>A solid line.</summary>
    Single,

    /// <summary>A thick solid line.</summary>
    Thick,

    /// <summary>Two parallel lines.</summary>
    Double,

    /// <summary>A dotted line.</summary>
    Dotted,

    /// <summary>A dashed line.</summary>
    Dashed,

    /// <summary>A wavy line.</summary>
    Wave,

    /// <summary>A shadowed line.</summary>
    Shadow,
}

/// <summary>
/// Character formatting: the properties that live in a run's <c>w:rPr</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every property is nullable, and <c>null</c> means "inherit", not "off". That distinction is the
/// whole point: a run inside a Heading 1 with <c>Bold = null</c> is bold because the style says so,
/// and the same run with <c>Bold = false</c> is not. Collapsing the two — which a non-nullable
/// <see cref="bool"/> forces — makes it impossible to write an unbolded word inside a bold heading.
/// </para>
/// </remarks>
public sealed class RunFormat
{
    private readonly Func<XElement> _properties;
    private readonly Action _touch;

    internal RunFormat(Func<XElement> properties, Action touch)
    {
        _properties = properties;
        _touch = touch;
    }

    /// <summary>
    /// The order <c>w:rPr</c>'s children must appear in.
    /// </summary>
    /// <remarks>
    /// This is not advisory. WordprocessingML content models are sequences, and Word reports a
    /// document whose <c>w:rPr</c> lists <c>w:sz</c> before <c>w:b</c> as unreadable content — it
    /// does not reorder or ignore, it refuses.
    /// </remarks>
    internal static readonly XName[] Order =
    [
        Ns.W + "rStyle", Ns.W + "rFonts", Ns.W + "b", Ns.W + "bCs", Ns.W + "i", Ns.W + "iCs",
        Ns.W + "caps", Ns.W + "smallCaps", Ns.W + "strike", Ns.W + "dstrike", Ns.W + "outline",
        Ns.W + "shadow", Ns.W + "emboss", Ns.W + "imprint", Ns.W + "noProof", Ns.W + "snapToGrid",
        Ns.W + "vanish", Ns.W + "webHidden", Ns.W + "color", Ns.W + "spacing", Ns.W + "w",
        Ns.W + "kern", Ns.W + "position", Ns.W + "sz", Ns.W + "szCs", Ns.W + "highlight",
        Ns.W + "u", Ns.W + "effect", Ns.W + "bdr", Ns.W + "shd", Ns.W + "fitText",
        Ns.W + "vertAlign", Ns.W + "rtl", Ns.W + "cs", Ns.W + "em", Ns.W + "lang",
        Ns.W + "eastAsianLayout", Ns.W + "specVanish", Ns.W + "oMath",
    ];

    private XElement Properties => _properties();

    private void Set(XName name, XElement? child)
    {
        XmlUtil.SetOrdered(Properties, name, child, Order);
        _touch();
    }

    private bool? Toggle(XName name) => Properties.ToggleValue(name);

    private void SetToggle(XName name, bool? value) =>
        Set(name, value is null ? null : XmlUtil.Toggle(name, value.Value));

    /// <summary>Bold. <c>null</c> inherits from the style.</summary>
    public bool? Bold
    {
        get => Toggle(Ns.W + "b");
        set
        {
            SetToggle(Ns.W + "b", value);
            // w:bCs is the complex-script counterpart. Word applies it to Arabic, Hebrew and the
            // Indic scripts, and a document that sets only w:b renders those runs unbolded.
            SetToggle(Ns.W + "bCs", value);
        }
    }

    /// <summary>Italic. <c>null</c> inherits.</summary>
    public bool? Italic
    {
        get => Toggle(Ns.W + "i");
        set
        {
            SetToggle(Ns.W + "i", value);
            SetToggle(Ns.W + "iCs", value);
        }
    }

    /// <summary>Small capitals.</summary>
    public bool? SmallCaps
    {
        get => Toggle(Ns.W + "smallCaps");
        set => SetToggle(Ns.W + "smallCaps", value);
    }

    /// <summary>All capitals.</summary>
    public bool? AllCaps
    {
        get => Toggle(Ns.W + "caps");
        set => SetToggle(Ns.W + "caps", value);
    }

    /// <summary>Single strikethrough.</summary>
    public bool? Strike
    {
        get => Toggle(Ns.W + "strike");
        set => SetToggle(Ns.W + "strike", value);
    }

    /// <summary>Double strikethrough.</summary>
    public bool? DoubleStrike
    {
        get => Toggle(Ns.W + "dstrike");
        set => SetToggle(Ns.W + "dstrike", value);
    }

    /// <summary>Hidden text, which is not printed unless Word is told to.</summary>
    public bool? Hidden
    {
        get => Toggle(Ns.W + "vanish");
        set => SetToggle(Ns.W + "vanish", value);
    }

    /// <summary>Right-to-left text direction for this run.</summary>
    public bool? RightToLeft
    {
        get => Toggle(Ns.W + "rtl");
        set => SetToggle(Ns.W + "rtl", value);
    }

    /// <summary>The underline style.</summary>
    public UnderlineStyle? Underline
    {
        get => Properties.Val(Ns.W + "u") switch
        {
            null => null,
            "none" => UnderlineStyle.None,
            "single" => UnderlineStyle.Single,
            "double" => UnderlineStyle.Double,
            "thick" => UnderlineStyle.Thick,
            "dotted" => UnderlineStyle.Dotted,
            "dash" => UnderlineStyle.Dash,
            "wave" => UnderlineStyle.Wave,
            "words" => UnderlineStyle.Words,
            _ => UnderlineStyle.Single,
        };
        set => Set(Ns.W + "u", value is null
            ? null
            : XmlUtil.ValElement(Ns.W + "u", value.Value switch
            {
                UnderlineStyle.None => "none",
                UnderlineStyle.Double => "double",
                UnderlineStyle.Thick => "thick",
                UnderlineStyle.Dotted => "dotted",
                UnderlineStyle.Dash => "dash",
                UnderlineStyle.Wave => "wave",
                UnderlineStyle.Words => "words",
                _ => "single",
            }));
    }

    /// <summary>The font size.</summary>
    public Length? FontSize
    {
        get
        {
            var raw = Properties.Val(Ns.W + "sz");
            // w:sz is in half-points, which is why 12 pt text is stored as 24.
            return raw is not null && double.TryParse(raw, out var halfPoints)
                ? Length.FromPoints(halfPoints / 2)
                : null;
        }
        set
        {
            var text = value is null ? null : XmlUtil.Num(value.Value.HalfPoints);
            Set(Ns.W + "sz", text is null ? null : XmlUtil.ValElement(Ns.W + "sz", text));
            Set(Ns.W + "szCs", text is null ? null : XmlUtil.ValElement(Ns.W + "szCs", text));
        }
    }

    /// <summary>The font family name.</summary>
    public string? FontName
    {
        get => Properties.Element(Ns.W + "rFonts")?.Attribute(Ns.W + "ascii")?.Value;
        set
        {
            if (value is null)
            {
                Set(Ns.W + "rFonts", null);
                return;
            }

            // All four script slots are set. Setting only w:ascii leaves accented Latin text
            // (which falls in the hAnsi range) in whatever the style said, so a document ends up
            // with two fonts inside one word.
            var element = new XElement(Ns.W + "rFonts",
                new XAttribute(Ns.W + "ascii", value),
                new XAttribute(Ns.W + "hAnsi", value),
                new XAttribute(Ns.W + "cs", value),
                new XAttribute(Ns.W + "eastAsia", value));

            Set(Ns.W + "rFonts", element);
        }
    }

    /// <summary>The text colour.</summary>
    public OfficeColor? Color
    {
        get
        {
            var raw = Properties.Val(Ns.W + "color");
            return raw is not null && OfficeColor.TryParse(raw, out var color) ? color : null;
        }
        set => Set(Ns.W + "color", value is null
            ? null
            : XmlUtil.ValElement(Ns.W + "color", value.Value.ToHex()));
    }

    /// <summary>The highlight colour, from Word's fixed palette of named highlights.</summary>
    public string? Highlight
    {
        get => Properties.Val(Ns.W + "highlight");
        set => Set(Ns.W + "highlight", value is null
            ? null
            : XmlUtil.ValElement(Ns.W + "highlight", value));
    }

    /// <summary>The shading (background) colour behind the text.</summary>
    public OfficeColor? Shading
    {
        get
        {
            var raw = Properties.Element(Ns.W + "shd")?.Attribute(Ns.W + "fill")?.Value;
            return raw is not null && OfficeColor.TryParse(raw, out var color) ? color : null;
        }
        set => Set(Ns.W + "shd", value is null
            ? null
            : new XElement(Ns.W + "shd",
                new XAttribute(Ns.W + "val", "clear"),
                new XAttribute(Ns.W + "color", "auto"),
                new XAttribute(Ns.W + "fill", value.Value.ToHex())));
    }

    /// <summary>Superscript or subscript.</summary>
    public VerticalAlignment? VerticalAlignment
    {
        get => Properties.Val(Ns.W + "vertAlign") switch
        {
            "superscript" => WordNet.VerticalAlignment.Superscript,
            "subscript" => WordNet.VerticalAlignment.Subscript,
            "baseline" => WordNet.VerticalAlignment.Baseline,
            _ => null,
        };
        set => Set(Ns.W + "vertAlign", value is null
            ? null
            : XmlUtil.ValElement(Ns.W + "vertAlign", value.Value switch
            {
                WordNet.VerticalAlignment.Superscript => "superscript",
                WordNet.VerticalAlignment.Subscript => "subscript",
                _ => "baseline",
            }));
    }

    /// <summary>Extra space between characters; negative values tighten.</summary>
    public Length? CharacterSpacing
    {
        get
        {
            var raw = Properties.Val(Ns.W + "spacing");
            return raw is not null && double.TryParse(raw, out var twips)
                ? Length.FromTwips(twips)
                : null;
        }
        set => Set(Ns.W + "spacing", value is null
            ? null
            : XmlUtil.ValElement(Ns.W + "spacing", XmlUtil.Num(value.Value.Twips)));
    }

    /// <summary>The character style applied to the run.</summary>
    public string? StyleId
    {
        get => Properties.Val(Ns.W + "rStyle");
        set => Set(Ns.W + "rStyle", value is null
            ? null
            : XmlUtil.ValElement(Ns.W + "rStyle", value));
    }

    /// <summary>The language tag, which drives spell checking.</summary>
    public string? Language
    {
        get => Properties.Element(Ns.W + "lang")?.Attribute(Ns.W + "val")?.Value;
        set => Set(Ns.W + "lang", value is null
            ? null
            : new XElement(Ns.W + "lang",
                new XAttribute(Ns.W + "val", value),
                new XAttribute(Ns.W + "eastAsia", value)));
    }

    /// <summary>Copies every set property from another format.</summary>
    public void CopyFrom(RunFormat other)
    {
        ArgumentNullException.ThrowIfNull(other);

        var source = other.Properties;
        var target = Properties;

        target.RemoveNodes();
        foreach (var element in source.Elements())
        {
            target.Add(new XElement(element));
        }

        _touch();
    }
}

/// <summary>
/// Paragraph formatting: the properties that live in <c>w:pPr</c>.
/// </summary>
public sealed class ParagraphFormat
{
    private readonly Func<XElement> _properties;
    private readonly Action _touch;

    internal ParagraphFormat(Func<XElement> properties, Action touch)
    {
        _properties = properties;
        _touch = touch;
    }

    /// <summary>The order <c>w:pPr</c>'s children must appear in.</summary>
    internal static readonly XName[] Order =
    [
        Ns.W + "pStyle", Ns.W + "keepNext", Ns.W + "keepLines", Ns.W + "pageBreakBefore",
        Ns.W + "framePr", Ns.W + "widowControl", Ns.W + "numPr", Ns.W + "suppressLineNumbers",
        Ns.W + "pBdr", Ns.W + "shd", Ns.W + "tabs", Ns.W + "suppressAutoHyphens",
        Ns.W + "kinsoku", Ns.W + "wordWrap", Ns.W + "overflowPunct", Ns.W + "topLinePunct",
        Ns.W + "autoSpaceDE", Ns.W + "autoSpaceDN", Ns.W + "bidi", Ns.W + "adjustRightInd",
        Ns.W + "snapToGrid", Ns.W + "spacing", Ns.W + "ind", Ns.W + "contextualSpacing",
        Ns.W + "mirrorIndents", Ns.W + "suppressOverlap", Ns.W + "jc", Ns.W + "textDirection",
        Ns.W + "textAlignment", Ns.W + "textboxTightWrap", Ns.W + "outlineLvl",
        Ns.W + "divId", Ns.W + "cnfStyle", Ns.W + "rPr", Ns.W + "sectPr", Ns.W + "pPrChange",
    ];

    private XElement Properties => _properties();

    private void Set(XName name, XElement? child)
    {
        XmlUtil.SetOrdered(Properties, name, child, Order);
        _touch();
    }

    /// <summary>The paragraph style id.</summary>
    public string? StyleId
    {
        get => Properties.Val(Ns.W + "pStyle");
        set => Set(Ns.W + "pStyle", value is null ? null : XmlUtil.ValElement(Ns.W + "pStyle", value));
    }

    /// <summary>Horizontal alignment.</summary>
    public ParagraphAlignment? Alignment
    {
        get => Properties.Val(Ns.W + "jc") switch
        {
            "left" or "start" => ParagraphAlignment.Left,
            "center" => ParagraphAlignment.Center,
            "right" or "end" => ParagraphAlignment.Right,
            "both" => ParagraphAlignment.Justify,
            "distribute" => ParagraphAlignment.Distribute,
            _ => null,
        };
        // "both" for justify, not "justify" — the latter is not a legal value and Word drops it.
        set => Set(Ns.W + "jc", value is null
            ? null
            : XmlUtil.ValElement(Ns.W + "jc", value.Value switch
            {
                ParagraphAlignment.Center => "center",
                ParagraphAlignment.Right => "right",
                ParagraphAlignment.Justify => "both",
                ParagraphAlignment.Distribute => "distribute",
                _ => "left",
            }));
    }

    private XElement? Indentation => Properties.Element(Ns.W + "ind");

    private Length? Indent(string attribute)
    {
        var raw = Indentation?.Attribute(Ns.W + attribute)?.Value;
        return raw is not null && double.TryParse(raw, out var twips) ? Length.FromTwips(twips) : null;
    }

    private void SetIndent(string attribute, Length? value)
    {
        var element = XmlUtil.GetOrCreate(Properties, Ns.W + "ind", Order);

        if (value is null)
        {
            element.Attribute(Ns.W + attribute)?.Remove();

            if (!element.HasAttributes)
            {
                element.Remove();
            }
        }
        else
        {
            element.SetAttributeValue(Ns.W + attribute, XmlUtil.Num(value.Value.Twips));
        }

        _touch();
    }

    /// <summary>Indent from the left margin.</summary>
    public Length? LeftIndent
    {
        get => Indent("left") ?? Indent("start");
        set => SetIndent("left", value);
    }

    /// <summary>Indent from the right margin.</summary>
    public Length? RightIndent
    {
        get => Indent("right") ?? Indent("end");
        set => SetIndent("right", value);
    }

    /// <summary>
    /// Extra indent applied to the first line only; a negative value makes a hanging indent.
    /// </summary>
    public Length? FirstLineIndent
    {
        get
        {
            var first = Indent("firstLine");
            if (first is not null)
            {
                return first;
            }

            // A hanging indent is stored as a positive w:hanging, which is the negation of what a
            // first-line indent means. Reporting it unsigned makes a hanging list look like an
            // indented one.
            var hanging = Indent("hanging");
            return hanging is null ? null : -hanging.Value;
        }
        set
        {
            var element = XmlUtil.GetOrCreate(Properties, Ns.W + "ind", Order);
            element.Attribute(Ns.W + "firstLine")?.Remove();
            element.Attribute(Ns.W + "hanging")?.Remove();

            if (value is null)
            {
                if (!element.HasAttributes)
                {
                    element.Remove();
                }
            }
            else if (value.Value.Emu < 0)
            {
                element.SetAttributeValue(Ns.W + "hanging", XmlUtil.Num((-value.Value).Twips));
            }
            else
            {
                element.SetAttributeValue(Ns.W + "firstLine", XmlUtil.Num(value.Value.Twips));
            }

            _touch();
        }
    }

    private XElement? Spacing => Properties.Element(Ns.W + "spacing");

    private void SetSpacingAttribute(string attribute, string? value)
    {
        var element = XmlUtil.GetOrCreate(Properties, Ns.W + "spacing", Order);

        if (value is null)
        {
            element.Attribute(Ns.W + attribute)?.Remove();

            if (!element.HasAttributes)
            {
                element.Remove();
            }
        }
        else
        {
            element.SetAttributeValue(Ns.W + attribute, value);
        }

        _touch();
    }

    /// <summary>Space above the paragraph.</summary>
    public Length? SpaceBefore
    {
        get
        {
            var raw = Spacing?.Attribute(Ns.W + "before")?.Value;
            return raw is not null && double.TryParse(raw, out var twips) ? Length.FromTwips(twips) : null;
        }
        set
        {
            SetSpacingAttribute("before", value is null ? null : XmlUtil.Num(value.Value.Twips));
            // w:beforeAutospacing overrides the explicit value, so it has to go when one is set.
            if (value is not null)
            {
                SetSpacingAttribute("beforeAutospacing", "0");
            }
        }
    }

    /// <summary>Space below the paragraph.</summary>
    public Length? SpaceAfter
    {
        get
        {
            var raw = Spacing?.Attribute(Ns.W + "after")?.Value;
            return raw is not null && double.TryParse(raw, out var twips) ? Length.FromTwips(twips) : null;
        }
        set
        {
            SetSpacingAttribute("after", value is null ? null : XmlUtil.Num(value.Value.Twips));
            if (value is not null)
            {
                SetSpacingAttribute("afterAutospacing", "0");
            }
        }
    }

    /// <summary>
    /// Line spacing as a multiple of single spacing, when the rule is
    /// <see cref="LineSpacingRule.Multiple"/>.
    /// </summary>
    public double? LineSpacing
    {
        get
        {
            var raw = Spacing?.Attribute(Ns.W + "line")?.Value;
            if (raw is null || !double.TryParse(raw, out var value))
            {
                return null;
            }

            // w:line is in 240ths of a line for the Multiple rule and in twips for the others.
            return LineSpacingRule == WordNet.LineSpacingRule.Multiple
                ? value / 240.0
                : Length.FromTwips(value).Points;
        }
        set
        {
            if (value is null)
            {
                SetSpacingAttribute("line", null);
                SetSpacingAttribute("lineRule", null);
                return;
            }

            var rule = LineSpacingRule ?? WordNet.LineSpacingRule.Multiple;

            SetSpacingAttribute("line", rule == WordNet.LineSpacingRule.Multiple
                ? XmlUtil.Num((long)Math.Round(value.Value * 240))
                : XmlUtil.Num(Length.FromPoints(value.Value).Twips));

            SetSpacingAttribute("lineRule", rule switch
            {
                WordNet.LineSpacingRule.Exact => "exact",
                WordNet.LineSpacingRule.AtLeast => "atLeast",
                _ => "auto",
            });
        }
    }

    /// <summary>How <see cref="LineSpacing"/> is interpreted.</summary>
    public LineSpacingRule? LineSpacingRule
    {
        get => Spacing?.Attribute(Ns.W + "lineRule")?.Value switch
        {
            "exact" => WordNet.LineSpacingRule.Exact,
            "atLeast" => WordNet.LineSpacingRule.AtLeast,
            "auto" => WordNet.LineSpacingRule.Multiple,
            _ => null,
        };
        set => SetSpacingAttribute("lineRule", value switch
        {
            WordNet.LineSpacingRule.Exact => "exact",
            WordNet.LineSpacingRule.AtLeast => "atLeast",
            WordNet.LineSpacingRule.Multiple => "auto",
            _ => null,
        });
    }

    /// <summary>Keeps the paragraph on the same page as the one after it.</summary>
    public bool? KeepWithNext
    {
        get => Properties.ToggleValue(Ns.W + "keepNext");
        set => Set(Ns.W + "keepNext", value is null ? null : XmlUtil.Toggle(Ns.W + "keepNext", value.Value));
    }

    /// <summary>Keeps all of the paragraph's lines on one page.</summary>
    public bool? KeepTogether
    {
        get => Properties.ToggleValue(Ns.W + "keepLines");
        set => Set(Ns.W + "keepLines", value is null ? null : XmlUtil.Toggle(Ns.W + "keepLines", value.Value));
    }

    /// <summary>Starts the paragraph on a new page.</summary>
    public bool? PageBreakBefore
    {
        get => Properties.ToggleValue(Ns.W + "pageBreakBefore");
        set => Set(Ns.W + "pageBreakBefore",
            value is null ? null : XmlUtil.Toggle(Ns.W + "pageBreakBefore", value.Value));
    }

    /// <summary>Prevents a single line being stranded at the top or bottom of a page.</summary>
    public bool? WidowControl
    {
        get => Properties.ToggleValue(Ns.W + "widowControl");
        set => Set(Ns.W + "widowControl",
            value is null ? null : XmlUtil.Toggle(Ns.W + "widowControl", value.Value));
    }

    /// <summary>
    /// Suppresses space between paragraphs of the same style, as list items expect.
    /// </summary>
    public bool? ContextualSpacing
    {
        get => Properties.ToggleValue(Ns.W + "contextualSpacing");
        set => Set(Ns.W + "contextualSpacing",
            value is null ? null : XmlUtil.Toggle(Ns.W + "contextualSpacing", value.Value));
    }

    /// <summary>Right-to-left paragraph direction.</summary>
    public bool? RightToLeft
    {
        get => Properties.ToggleValue(Ns.W + "bidi");
        set => Set(Ns.W + "bidi", value is null ? null : XmlUtil.Toggle(Ns.W + "bidi", value.Value));
    }

    /// <summary>The shading (background) colour behind the paragraph.</summary>
    public OfficeColor? Shading
    {
        get
        {
            var raw = Properties.Element(Ns.W + "shd")?.Attribute(Ns.W + "fill")?.Value;
            return raw is not null && OfficeColor.TryParse(raw, out var color) ? color : null;
        }
        set => Set(Ns.W + "shd", value is null
            ? null
            : new XElement(Ns.W + "shd",
                new XAttribute(Ns.W + "val", "clear"),
                new XAttribute(Ns.W + "color", "auto"),
                new XAttribute(Ns.W + "fill", value.Value.ToHex())));
    }

    /// <summary>
    /// The outline level, 0-8 for a heading and 9 for body text — what the navigation pane and a
    /// table of contents read.
    /// </summary>
    public int? OutlineLevel
    {
        get => int.TryParse(Properties.Val(Ns.W + "outlineLvl"), out var level) ? level : null;
        set => Set(Ns.W + "outlineLvl", value is null
            ? null
            : XmlUtil.ValElement(Ns.W + "outlineLvl", XmlUtil.Num(value.Value)));
    }

    /// <summary>Sets a border on every edge of the paragraph.</summary>
    public void SetBorder(BorderStyle style, OfficeColor? color = null, Length? width = null)
    {
        if (style == BorderStyle.None)
        {
            Set(Ns.W + "pBdr", null);
            return;
        }

        var borders = new XElement(Ns.W + "pBdr");

        // w:pBdr's children are ordered too, and the order is not alphabetical.
        foreach (var edge in (string[])["top", "left", "bottom", "right"])
        {
            borders.Add(BorderElement(Ns.W + edge, style, color, width));
        }

        Set(Ns.W + "pBdr", borders);
    }

    internal static XElement BorderElement(XName name, BorderStyle style, OfficeColor? color, Length? width)
    {
        var element = new XElement(name,
            new XAttribute(Ns.W + "val", style switch
            {
                BorderStyle.None => "none",
                BorderStyle.Double => "double",
                BorderStyle.Thick => "thick",
                BorderStyle.Dotted => "dotted",
                BorderStyle.Dashed => "dashed",
                BorderStyle.Wave => "wave",
                BorderStyle.Shadow => "single",
                _ => "single",
            }),
            // w:sz on a border is in EIGHTHS of a point, unlike w:sz on a run which is half-points.
            // The same attribute name meaning two different units is a genuine trap in the format.
            new XAttribute(Ns.W + "sz", XmlUtil.Num((long)Math.Max(2, Math.Round((width ?? Units.Pt(0.5)).Points * 8)))),
            new XAttribute(Ns.W + "space", "0"),
            new XAttribute(Ns.W + "color", (color ?? OfficeColor.Automatic).ToHex()));

        if (style == BorderStyle.Shadow)
        {
            element.SetAttributeValue(Ns.W + "shadow", "1");
        }

        return element;
    }

    /// <summary>Copies every set property from another format.</summary>
    public void CopyFrom(ParagraphFormat other)
    {
        ArgumentNullException.ThrowIfNull(other);

        var source = other.Properties;
        var target = Properties;

        // w:rPr inside w:pPr is the paragraph mark's own formatting and belongs to this paragraph,
        // not to the one being copied from.
        var mark = target.Element(Ns.W + "rPr");

        target.RemoveNodes();
        foreach (var element in source.Elements().Where(e => e.Name != Ns.W + "rPr"))
        {
            target.Add(new XElement(element));
        }

        if (mark is not null)
        {
            XmlUtil.SetOrdered(target, Ns.W + "rPr", mark, Order);
        }

        _touch();
    }
}

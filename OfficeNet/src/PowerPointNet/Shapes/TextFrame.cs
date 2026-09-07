// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Text;
using System.Xml.Linq;
using OfficeNet.Core;
using OfficeNet.Core.Drawing;
using OfficeNet.Core.Xml;

namespace PowerPointNet.Shapes;

/// <summary>How a paragraph is aligned in its text frame.</summary>
public enum TextAlignment
{
    /// <summary>Left.</summary>
    Left,

    /// <summary>Centred.</summary>
    Center,

    /// <summary>Right.</summary>
    Right,

    /// <summary>Stretched to both edges.</summary>
    Justify,
}

/// <summary>How text sits vertically in its frame.</summary>
public enum TextAnchor
{
    /// <summary>Against the top.</summary>
    Top,

    /// <summary>Centred.</summary>
    Middle,

    /// <summary>Against the bottom.</summary>
    Bottom,
}

/// <summary>How a text frame reacts when its text does not fit.</summary>
public enum AutoFitMode
{
    /// <summary>The text overflows.</summary>
    None,

    /// <summary>The text shrinks.</summary>
    ShrinkText,

    /// <summary>The shape grows.</summary>
    ResizeShape,
}

/// <summary>A run: a span of text with one set of character formatting.</summary>
public sealed class TextRun
{
    private readonly Presentation _presentation;

    internal TextRun(Presentation presentation, XElement element)
    {
        _presentation = presentation;
        Element = element;
    }

    /// <summary>The underlying <c>a:r</c> element.</summary>
    public XElement Element { get; }

    private XElement Properties =>
        // a:rPr must be the FIRST child of a:r, before a:t. DrawingML enforces the order.
        Element.Element(Ns.A + "rPr") ?? CreateProperties();

    private XElement CreateProperties()
    {
        var properties = new XElement(Ns.A + "rPr", new XAttribute("lang", "en-US"));
        Element.AddFirst(properties);
        _presentation.Touch();
        return properties;
    }

    /// <summary>The run's text.</summary>
    public string Text
    {
        get => Element.Element(Ns.A + "t")?.Value ?? string.Empty;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            var text = Element.Element(Ns.A + "t");

            if (text is null)
            {
                Element.Add(XmlUtil.TextElement(Ns.A + "t", value));
            }
            else
            {
                text.Value = value;

                if (value.Length > 0 && (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1])))
                {
                    text.SetAttributeValue(XNamespace.Xml + "space", "preserve");
                }
            }

            _presentation.Touch();
        }
    }

    /// <summary>Bold; <c>null</c> inherits from the placeholder's style.</summary>
    public bool? Bold
    {
        get => XmlUtil.OoxmlBool(Element.Element(Ns.A + "rPr")?.Attr("b"));
        set
        {
            Properties.SetAttributeValue("b", value is null ? null : value.Value ? "1" : "0");
            _presentation.Touch();
        }
    }

    /// <summary>Italic.</summary>
    public bool? Italic
    {
        get => XmlUtil.OoxmlBool(Element.Element(Ns.A + "rPr")?.Attr("i"));
        set
        {
            Properties.SetAttributeValue("i", value is null ? null : value.Value ? "1" : "0");
            _presentation.Touch();
        }
    }

    /// <summary>Underline.</summary>
    public bool? Underline
    {
        get => Element.Element(Ns.A + "rPr")?.Attr("u") is { } u && u != "none";
        set
        {
            Properties.SetAttributeValue("u", value is null ? null : value.Value ? "sng" : "none");
            _presentation.Touch();
        }
    }

    /// <summary>The font size.</summary>
    public Length? FontSize
    {
        get
        {
            // a:rPr/@sz is in hundredths of a point, unlike WordprocessingML's half-points.
            var raw = Element.Element(Ns.A + "rPr")?.Attr("sz");
            return raw is not null && double.TryParse(raw, out var hundredths)
                ? Length.FromPoints(hundredths / 100)
                : null;
        }
        set
        {
            Properties.SetAttributeValue("sz",
                value is null ? null : XmlUtil.Num(value.Value.Centipoints));
            _presentation.Touch();
        }
    }

    /// <summary>The text colour.</summary>
    public OfficeColor? Color
    {
        get
        {
            var fill = Element.Element(Ns.A + "rPr")?.Element(Ns.A + "solidFill");
            var raw = fill?.Element(Ns.A + "srgbClr")?.Attr("val");
            return raw is not null && OfficeColor.TryParse(raw, out var color) ? color : null;
        }
        set
        {
            var properties = Properties;
            properties.Elements(Ns.A + "solidFill").Remove();

            if (value is not null)
            {
                // a:solidFill must precede a:latin inside a:rPr.
                var fill = new XElement(Ns.A + "solidFill",
                    new XElement(Ns.A + "srgbClr", new XAttribute("val", value.Value.ToHex())));

                var latin = properties.Element(Ns.A + "latin");

                if (latin is not null)
                {
                    latin.AddBeforeSelf(fill);
                }
                else
                {
                    properties.Add(fill);
                }
            }

            _presentation.Touch();
        }
    }

    /// <summary>The font family name.</summary>
    public string? FontName
    {
        get => Element.Element(Ns.A + "rPr")?.Element(Ns.A + "latin")?.Attr("typeface");
        set
        {
            var properties = Properties;
            properties.Elements(Ns.A + "latin").Remove();
            properties.Elements(Ns.A + "cs").Remove();

            if (value is not null)
            {
                properties.Add(new XElement(Ns.A + "latin", new XAttribute("typeface", value)));
                properties.Add(new XElement(Ns.A + "cs", new XAttribute("typeface", value)));
            }

            _presentation.Touch();
        }
    }

    /// <summary>Turns the run into a hyperlink.</summary>
    public TextRun SetHyperlink(string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        var relationshipId = _presentation.AddHyperlink(Element, url);

        var properties = Properties;
        properties.Elements(Ns.A + "hlinkClick").Remove();

        // a:hlinkClick goes last in a:rPr.
        properties.Add(new XElement(Ns.A + "hlinkClick",
            new XAttribute(Ns.R + "id", relationshipId)));

        _presentation.Touch();
        return this;
    }

    /// <summary>Makes the run bold and returns it.</summary>
    public TextRun WithBold(bool value = true)
    {
        Bold = value;
        return this;
    }

    /// <summary>Makes the run italic and returns it.</summary>
    public TextRun WithItalic(bool value = true)
    {
        Italic = value;
        return this;
    }

    /// <summary>Sets the run's size in points and returns it.</summary>
    public TextRun WithSize(double points)
    {
        FontSize = Units.Pt(points);
        return this;
    }

    /// <summary>Sets the run's colour and returns it.</summary>
    public TextRun WithColor(OfficeColor color)
    {
        Color = color;
        return this;
    }

    /// <summary>Sets the run's font and returns it.</summary>
    public TextRun WithFont(string name)
    {
        FontName = name;
        return this;
    }

    public override string ToString() => Text;
}

/// <summary>A paragraph inside a text frame.</summary>
public sealed class TextParagraph
{
    private readonly Presentation _presentation;

    internal TextParagraph(Presentation presentation, XElement element)
    {
        _presentation = presentation;
        Element = element;
    }

    /// <summary>The underlying <c>a:p</c> element.</summary>
    public XElement Element { get; }

    private XElement Properties
    {
        get
        {
            var existing = Element.Element(Ns.A + "pPr");

            if (existing is not null)
            {
                return existing;
            }

            // a:pPr must be the first child of a:p.
            var created = new XElement(Ns.A + "pPr");
            Element.AddFirst(created);
            _presentation.Touch();
            return created;
        }
    }

    /// <summary>The paragraph's runs.</summary>
    public IReadOnlyList<TextRun> Runs =>
        [.. Element.Elements(Ns.A + "r").Select(e => new TextRun(_presentation, e))];

    /// <summary>The paragraph's text, runs concatenated and line breaks rendered.</summary>
    public string Text
    {
        get
        {
            var builder = new StringBuilder();

            foreach (var child in Element.Elements())
            {
                if (child.Name == Ns.A + "r")
                {
                    builder.Append(child.Element(Ns.A + "t")?.Value);
                }
                else if (child.Name == Ns.A + "br")
                {
                    builder.Append('\n');
                }
                else if (child.Name == Ns.A + "fld")
                {
                    // A field's cached result is its a:t; slide numbers and dates live here.
                    builder.Append(child.Element(Ns.A + "t")?.Value);
                }
            }

            return builder.ToString();
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            var properties = Element.Element(Ns.A + "pPr");
            Element.RemoveNodes();

            if (properties is not null)
            {
                Element.Add(properties);
            }

            if (value.Length > 0)
            {
                AddRun(value);
            }

            _presentation.Touch();
        }
    }

    /// <summary>
    /// The indent level, 0-8, which selects the list style the master defines.
    /// </summary>
    public int Level
    {
        get => Element.Element(Ns.A + "pPr").IntAttr("lvl");
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 8);

            Properties.SetAttributeValue("lvl", value == 0 ? null : value);
            _presentation.Touch();
        }
    }

    /// <summary>Horizontal alignment.</summary>
    public TextAlignment? Alignment
    {
        get => Element.Element(Ns.A + "pPr").Attr("algn") switch
        {
            "l" => TextAlignment.Left,
            "ctr" => TextAlignment.Center,
            "r" => TextAlignment.Right,
            "just" => TextAlignment.Justify,
            _ => null,
        };
        set
        {
            Properties.SetAttributeValue("algn", value switch
            {
                TextAlignment.Left => "l",
                TextAlignment.Center => "ctr",
                TextAlignment.Right => "r",
                TextAlignment.Justify => "just",
                _ => null,
            });
            _presentation.Touch();
        }
    }

    /// <summary>Space before the paragraph.</summary>
    public Length? SpaceBefore
    {
        get
        {
            var raw = Element.Element(Ns.A + "pPr")?.Element(Ns.A + "spcBef")
                ?.Element(Ns.A + "spcPts")?.Attr("val");
            return raw is not null && double.TryParse(raw, out var hundredths)
                ? Length.FromPoints(hundredths / 100)
                : null;
        }
        set
        {
            var properties = Properties;
            properties.Elements(Ns.A + "spcBef").Remove();

            if (value is not null)
            {
                properties.AddFirst(new XElement(Ns.A + "spcBef",
                    new XElement(Ns.A + "spcPts",
                        new XAttribute("val", XmlUtil.Num(value.Value.Centipoints)))));
            }

            _presentation.Touch();
        }
    }

    /// <summary>
    /// Whether the paragraph shows a bullet; <c>null</c> inherits from the level's list style.
    /// </summary>
    public bool? HasBullet
    {
        get
        {
            var properties = Element.Element(Ns.A + "pPr");

            if (properties is null)
            {
                return null;
            }

            if (properties.Element(Ns.A + "buNone") is not null)
            {
                return false;
            }

            return properties.Element(Ns.A + "buChar") is not null ||
                   properties.Element(Ns.A + "buAutoNum") is not null
                ? true
                : null;
        }
        set
        {
            var properties = Properties;
            properties.Elements(Ns.A + "buNone").Remove();
            properties.Elements(Ns.A + "buChar").Remove();
            properties.Elements(Ns.A + "buAutoNum").Remove();
            properties.Elements(Ns.A + "buFont").Remove();

            switch (value)
            {
                case false:
                    properties.Add(new XElement(Ns.A + "buNone"));
                    break;

                case true:
                    // The bullet font must be set alongside the character or the glyph comes from
                    // the body font, where U+2022 may not exist.
                    properties.Add(new XElement(Ns.A + "buFont",
                        new XAttribute("typeface", "Arial")));
                    properties.Add(new XElement(Ns.A + "buChar", new XAttribute("char", "•")));
                    break;
            }

            _presentation.Touch();
        }
    }

    /// <summary>Turns the paragraph into a numbered list item.</summary>
    public TextParagraph SetNumbered(string scheme = "arabicPeriod", int startAt = 1)
    {
        var properties = Properties;
        properties.Elements(Ns.A + "buNone").Remove();
        properties.Elements(Ns.A + "buChar").Remove();
        properties.Elements(Ns.A + "buAutoNum").Remove();

        properties.Add(new XElement(Ns.A + "buAutoNum",
            new XAttribute("type", scheme),
            startAt == 1 ? null : new XAttribute("startAt", startAt)));

        _presentation.Touch();
        return this;
    }

    /// <summary>Appends a run.</summary>
    public TextRun AddRun(string text = "")
    {
        var element = new XElement(Ns.A + "r",
            new XElement(Ns.A + "rPr", new XAttribute("lang", "en-US")));

        Element.Add(element);

        var run = new TextRun(_presentation, element);

        if (text.Length > 0)
        {
            run.Text = text;
        }

        _presentation.Touch();
        return run;
    }

    /// <summary>Appends a formatted run.</summary>
    public TextRun AddRun(string text, bool bold = false, bool italic = false,
        double? sizePoints = null, OfficeColor? color = null)
    {
        var run = AddRun(text);

        if (bold)
        {
            run.Bold = true;
        }

        if (italic)
        {
            run.Italic = true;
        }

        if (sizePoints is not null)
        {
            run.FontSize = Units.Pt(sizePoints.Value);
        }

        if (color is not null)
        {
            run.Color = color;
        }

        return run;
    }

    /// <summary>Appends a line break.</summary>
    public TextParagraph AddLineBreak()
    {
        Element.Add(new XElement(Ns.A + "br"));
        _presentation.Touch();
        return this;
    }

    public override string ToString() => Text;
}

/// <summary>The text inside a shape.</summary>
/// <remarks>
/// A text frame always holds at least one paragraph. PresentationML requires it, and PowerPoint
/// repairs a <c>p:txBody</c> with none — so <see cref="Clear"/> leaves one behind rather than
/// emptying the element.
/// </remarks>
public sealed class TextFrame
{
    private readonly Presentation _presentation;

    internal TextFrame(Presentation presentation, XElement element)
    {
        _presentation = presentation;
        Element = element;
    }

    /// <summary>The underlying <c>p:txBody</c> element.</summary>
    public XElement Element { get; }

    private XElement BodyProperties
    {
        get
        {
            var existing = Element.Element(Ns.A + "bodyPr");

            if (existing is not null)
            {
                return existing;
            }

            var created = new XElement(Ns.A + "bodyPr");
            Element.AddFirst(created);
            _presentation.Touch();
            return created;
        }
    }

    /// <summary>The frame's paragraphs.</summary>
    public IReadOnlyList<TextParagraph> Paragraphs =>
        [.. Element.Elements(Ns.A + "p").Select(e => new TextParagraph(_presentation, e))];

    /// <summary>
    /// All of the frame's text, paragraphs joined by newlines. Setting it replaces the content,
    /// one paragraph per line.
    /// </summary>
    public string Text
    {
        get => string.Join('\n', Paragraphs.Select(p => p.Text));
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            Element.Elements(Ns.A + "p").Remove();

            foreach (var line in value.Replace("\r\n", "\n").Split('\n'))
            {
                AddParagraph(line);
            }

            if (Element.Element(Ns.A + "p") is null)
            {
                AddParagraph();
            }

            _presentation.Touch();
        }
    }

    /// <summary>Appends a paragraph.</summary>
    public TextParagraph AddParagraph(string text = "", int level = 0)
    {
        var element = new XElement(Ns.A + "p");
        Element.Add(element);

        var paragraph = new TextParagraph(_presentation, element);

        if (level != 0)
        {
            paragraph.Level = level;
        }

        if (text.Length > 0)
        {
            paragraph.AddRun(text);
        }

        _presentation.Touch();
        return paragraph;
    }

    /// <summary>Appends several paragraphs as a bulleted list.</summary>
    public IReadOnlyList<TextParagraph> AddBullets(IEnumerable<string> items, int level = 0)
    {
        ArgumentNullException.ThrowIfNull(items);

        var result = new List<TextParagraph>();

        foreach (var item in items)
        {
            var paragraph = AddParagraph(item, level);
            paragraph.HasBullet = true;
            result.Add(paragraph);
        }

        return result;
    }

    /// <summary>Removes all content, leaving one empty paragraph.</summary>
    public void Clear()
    {
        Element.Elements(Ns.A + "p").Remove();
        Element.Add(new XElement(Ns.A + "p"));
        _presentation.Touch();
    }

    /// <summary>How the text sits vertically.</summary>
    public TextAnchor Anchor
    {
        get => Element.Element(Ns.A + "bodyPr")?.Attr("anchor") switch
        {
            "ctr" => TextAnchor.Middle,
            "b" => TextAnchor.Bottom,
            _ => TextAnchor.Top,
        };
        set
        {
            BodyProperties.SetAttributeValue("anchor", value switch
            {
                TextAnchor.Middle => "ctr",
                TextAnchor.Bottom => "b",
                _ => "t",
            });
            _presentation.Touch();
        }
    }

    /// <summary>Whether the text wraps at the shape's edge.</summary>
    public bool WordWrap
    {
        get => Element.Element(Ns.A + "bodyPr")?.Attr("wrap") is not "none";
        set
        {
            BodyProperties.SetAttributeValue("wrap", value ? "square" : "none");
            _presentation.Touch();
        }
    }

    /// <summary>What happens when the text does not fit.</summary>
    public AutoFitMode AutoFit
    {
        get
        {
            var properties = Element.Element(Ns.A + "bodyPr");

            if (properties?.Element(Ns.A + "normAutofit") is not null)
            {
                return AutoFitMode.ShrinkText;
            }

            return properties?.Element(Ns.A + "spAutoFit") is not null
                ? AutoFitMode.ResizeShape
                : AutoFitMode.None;
        }
        set
        {
            var properties = BodyProperties;
            properties.Elements(Ns.A + "noAutofit").Remove();
            properties.Elements(Ns.A + "normAutofit").Remove();
            properties.Elements(Ns.A + "spAutoFit").Remove();

            properties.Add(value switch
            {
                AutoFitMode.ShrinkText => new XElement(Ns.A + "normAutofit"),
                AutoFitMode.ResizeShape => new XElement(Ns.A + "spAutoFit"),
                _ => new XElement(Ns.A + "noAutofit"),
            });

            _presentation.Touch();
        }
    }

    /// <summary>Sets the frame's internal margins.</summary>
    public TextFrame SetMargins(Length left, Length top, Length right, Length bottom)
    {
        var properties = BodyProperties;
        properties.SetAttributeValue("lIns", left.Emu);
        properties.SetAttributeValue("tIns", top.Emu);
        properties.SetAttributeValue("rIns", right.Emu);
        properties.SetAttributeValue("bIns", bottom.Emu);
        _presentation.Touch();
        return this;
    }

    public override string ToString() => Text;
}

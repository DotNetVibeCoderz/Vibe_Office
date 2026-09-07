// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Xml.Linq;
using OfficeNet.Core;
using OfficeNet.Core.Drawing;
using OfficeNet.Core.Packaging;
using OfficeNet.Core.Xml;
using PowerPointNet.Shapes;

namespace PowerPointNet.Layouts;

/// <summary>
/// A slide layout: the arrangement of placeholders a slide inherits.
/// </summary>
/// <remarks>
/// A layout is a template, not content. Its shapes hold prompt text ("Click to edit…") that must
/// never be copied onto a slide, and its placeholders are matched to a slide's by <c>type</c> and
/// <c>idx</c> — not by position.
/// </remarks>
public sealed class SlideLayout
{
    private readonly Presentation _presentation;

    internal SlideLayout(Presentation presentation, OpcPart part, XElement root)
    {
        _presentation = presentation;
        Part = part;
        Root = root;
    }

    /// <summary>The layout's part.</summary>
    public OpcPart Part { get; }

    /// <summary>The <c>p:sldLayout</c> root element.</summary>
    public XElement Root { get; }

    /// <summary>The layout's name, as shown in PowerPoint's layout gallery.</summary>
    public string Name
    {
        get => Root.Element(Ns.P + "cSld")?.Attr("name") ?? Type;
        set
        {
            Root.Element(Ns.P + "cSld")?.SetAttributeValue("name", value);
            _presentation.Touch();
        }
    }

    /// <summary>The layout type, for example <c>title</c>, <c>obj</c> or <c>blank</c>.</summary>
    public string Type => Root.Attr("type") ?? "obj";

    /// <summary>The placeholder shapes the layout defines.</summary>
    public IReadOnlyList<Shape> Placeholders =>
    [
        .. Root.Element(Ns.P + "cSld")?.Element(Ns.P + "spTree")?.Elements(Ns.P + "sp")
            .Select(e => new Shape(_presentation, e))
            .Where(s => s.IsPlaceholder) ?? [],
    ];

    /// <summary>The layout's background colour; <c>null</c> inherits from the master.</summary>
    public OfficeColor? BackgroundColor
    {
        get
        {
            var raw = Root.Element(Ns.P + "cSld")?.Element(Ns.P + "bg")?.Element(Ns.P + "bgPr")
                ?.Element(Ns.A + "solidFill")?.Element(Ns.A + "srgbClr")?.Attr("val");

            return raw is not null && OfficeColor.TryParse(raw, out var color) ? color : null;
        }
        set
        {
            var common = Root.Element(Ns.P + "cSld")
                ?? throw new OfficeNetException($"{Part.Name} has no p:cSld.");

            common.Elements(Ns.P + "bg").Remove();

            if (value is not null)
            {
                common.AddFirst(new XElement(Ns.P + "bg",
                    new XElement(Ns.P + "bgPr",
                        new XElement(Ns.A + "solidFill",
                            new XElement(Ns.A + "srgbClr",
                                new XAttribute("val", value.Value.ToHex()))),
                        new XElement(Ns.A + "effectLst"))));
            }

            _presentation.Touch();
        }
    }

    public override string ToString() => $"Layout \"{Name}\" ({Type})";
}

/// <summary>
/// The slide master: what every layout, and through them every slide, inherits from.
/// </summary>
public sealed class SlideMaster
{
    private readonly Presentation _presentation;

    internal SlideMaster(Presentation presentation, OpcPart part, XElement root)
    {
        _presentation = presentation;
        Part = part;
        Root = root;
    }

    /// <summary>The master's part.</summary>
    public OpcPart Part { get; }

    /// <summary>The <c>p:sldMaster</c> root element.</summary>
    public XElement Root { get; }

    /// <summary>The theme part this master resolves its colours and fonts through.</summary>
    public OpcPart? ThemePart => Part.RelatedPartByType(RelationshipTypes.Theme);

    /// <summary>The master's background colour.</summary>
    public OfficeColor? BackgroundColor
    {
        get
        {
            var raw = Root.Element(Ns.P + "cSld")?.Element(Ns.P + "bg")?.Element(Ns.P + "bgPr")
                ?.Element(Ns.A + "solidFill")?.Element(Ns.A + "srgbClr")?.Attr("val");

            return raw is not null && OfficeColor.TryParse(raw, out var color) ? color : null;
        }
        set
        {
            var common = Root.Element(Ns.P + "cSld")
                ?? throw new OfficeNetException($"{Part.Name} has no p:cSld.");

            common.Elements(Ns.P + "bg").Remove();

            if (value is not null)
            {
                common.AddFirst(new XElement(Ns.P + "bg",
                    new XElement(Ns.P + "bgPr",
                        new XElement(Ns.A + "solidFill",
                            new XElement(Ns.A + "srgbClr",
                                new XAttribute("val", value.Value.ToHex()))),
                        new XElement(Ns.A + "effectLst"))));
            }

            _presentation.Touch();
        }
    }

    /// <summary>
    /// Replaces a theme colour slot, restyling every shape that references it.
    /// </summary>
    /// <param name="slot">
    /// The slot name: <c>accent1</c>..<c>accent6</c>, <c>dk2</c>, <c>lt2</c>, <c>hlink</c>,
    /// <c>folHlink</c>.
    /// </param>
    /// <param name="color">The colour the slot resolves to.</param>
    /// <remarks>
    /// This is what makes a theme worth having: a deck whose shapes reference <c>accent1</c>
    /// changes colour everywhere from one edit here. Shapes given a literal <c>srgbClr</c> — which
    /// is what <see cref="Shape.FillColor"/> writes — are unaffected by design.
    /// </remarks>
    public void SetThemeColor(string slot, OfficeColor color)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);

        if (slot is "dk1" or "lt1")
        {
            throw new ArgumentException(
                "dk1 and lt1 are system colours (windowText and window) and must stay system " +
                "colours, or the theme loses its high-contrast behaviour. Use dk2 or lt2.",
                nameof(slot));
        }

        var theme = ThemePart?.Xml.Root
            ?? throw new OfficeNetException("The master has no theme part.");

        var scheme = theme.Element(Ns.A + "themeElements")?.Element(Ns.A + "clrScheme")
            ?? throw new OfficeNetException("The theme has no colour scheme.");

        var element = scheme.Element(Ns.A + slot)
            ?? throw new ArgumentException($"The theme has no '{slot}' slot.", nameof(slot));

        element.RemoveNodes();
        element.Add(new XElement(Ns.A + "srgbClr", new XAttribute("val", color.ToHex())));

        _presentation.Touch();
    }

    /// <summary>Sets the theme's heading and body fonts.</summary>
    public void SetThemeFonts(string majorFont, string minorFont)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(majorFont);
        ArgumentException.ThrowIfNullOrWhiteSpace(minorFont);

        var theme = ThemePart?.Xml.Root
            ?? throw new OfficeNetException("The master has no theme part.");

        var scheme = theme.Element(Ns.A + "themeElements")?.Element(Ns.A + "fontScheme")
            ?? throw new OfficeNetException("The theme has no font scheme.");

        scheme.Element(Ns.A + "majorFont")?.Element(Ns.A + "latin")
            ?.SetAttributeValue("typeface", majorFont);

        scheme.Element(Ns.A + "minorFont")?.Element(Ns.A + "latin")
            ?.SetAttributeValue("typeface", minorFont);

        _presentation.Touch();
    }

    /// <summary>The theme's colour slots, as a name-to-colour map.</summary>
    public IReadOnlyDictionary<string, OfficeColor> ThemeColors
    {
        get
        {
            var result = new Dictionary<string, OfficeColor>(StringComparer.OrdinalIgnoreCase);

            var scheme = ThemePart?.Xml.Root?.Element(Ns.A + "themeElements")
                ?.Element(Ns.A + "clrScheme");

            if (scheme is null)
            {
                return result;
            }

            foreach (var slot in scheme.Elements())
            {
                var raw = slot.Element(Ns.A + "srgbClr")?.Attr("val")
                          // A system colour records its resolved value in lastClr, which is the
                          // only way to get a usable RGB out of windowText.
                          ?? slot.Element(Ns.A + "sysClr")?.Attr("lastClr");

                if (raw is not null && OfficeColor.TryParse(raw, out var color))
                {
                    result[slot.Name.LocalName] = color;
                }
            }

            return result;
        }
    }

    public override string ToString() => $"SlideMaster({Part.Name})";
}

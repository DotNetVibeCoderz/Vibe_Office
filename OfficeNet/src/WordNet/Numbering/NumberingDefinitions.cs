// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Xml.Linq;
using OfficeNet.Core;
using OfficeNet.Core.Xml;

namespace WordNet.Numbering;

/// <summary>The number formats a list level can use.</summary>
public enum NumberFormat
{
    /// <summary>A bullet character rather than a number.</summary>
    Bullet,

    /// <summary>1, 2, 3.</summary>
    Decimal,

    /// <summary>a, b, c.</summary>
    LowerLetter,

    /// <summary>A, B, C.</summary>
    UpperLetter,

    /// <summary>i, ii, iii.</summary>
    LowerRoman,

    /// <summary>I, II, III.</summary>
    UpperRoman,

    /// <summary>01, 02, 03.</summary>
    DecimalZero,

    /// <summary>Nothing is shown.</summary>
    None,
}

/// <summary>
/// The list definitions in <c>numbering.xml</c>.
/// </summary>
/// <remarks>
/// <para>
/// Word's list model has two layers, and both are required. An <em>abstract</em> numbering
/// (<c>w:abstractNum</c>) describes the nine indent levels — what character or number each shows,
/// how it indents, what it restarts on. A <em>concrete</em> numbering (<c>w:num</c>) points at an
/// abstract one and is what a paragraph's <c>w:numId</c> actually references.
/// </para>
/// <para>
/// The indirection is what makes "restart numbering at 1" possible: two <c>w:num</c> entries
/// sharing one abstract definition are two independent lists that look identical. A paragraph
/// referencing an abstract id directly numbers nothing at all.
/// </para>
/// </remarks>
public sealed class NumberingDefinitions
{
    private readonly WordDocument _document;
    private readonly XElement _root;

    internal NumberingDefinitions(WordDocument document, XElement root)
    {
        _document = document;
        _root = root;
    }

    /// <summary>The concrete numbering ids defined in the document.</summary>
    public IReadOnlyList<int> NumberingIds =>
    [
        .. _root.Elements(Ns.W + "num")
            .Select(e => e.Attr(Ns.W + "numId"))
            .Where(v => v is not null)
            .Select(v => int.TryParse(v, out var id) ? id : 0)
            .Where(id => id > 0),
    ];

    /// <summary>
    /// Defines a bulleted list and returns the numbering id to pass to
    /// <see cref="Paragraph.SetListItem"/>.
    /// </summary>
    /// <param name="bulletCharacters">
    /// The bullet for each level; the Word defaults are used when omitted.
    /// </param>
    public int AddBulletList(params string[] bulletCharacters)
    {
        // Word's own defaults: a filled bullet in Symbol, a hollow circle in Courier New, a filled
        // square in Wingdings — then the cycle repeats.
        string[] defaults = ["", "o", ""];
        string[] fonts = ["Symbol", "Courier New", "Wingdings"];

        var abstractId = NextAbstractId();
        var abstractNum = new XElement(Ns.W + "abstractNum",
            new XAttribute(Ns.W + "abstractNumId", abstractId),
            XmlUtil.ValElement(Ns.W + "multiLevelType", "hybridMultilevel"));

        for (var level = 0; level < 9; level++)
        {
            var text = bulletCharacters.Length > 0
                ? bulletCharacters[level % bulletCharacters.Length]
                : defaults[level % defaults.Length];

            var font = bulletCharacters.Length > 0 ? null : fonts[level % fonts.Length];

            abstractNum.Add(BulletLevel(level, text, font));
        }

        _root.AddFirst(abstractNum);
        return AddConcreteNumbering(abstractId);
    }

    /// <summary>
    /// Defines a numbered list.
    /// </summary>
    /// <param name="format">The format for the first level; deeper levels alternate.</param>
    /// <param name="startAt">The number the list starts at.</param>
    public int AddNumberedList(NumberFormat format = NumberFormat.Decimal, int startAt = 1)
    {
        var abstractId = NextAbstractId();
        var abstractNum = new XElement(Ns.W + "abstractNum",
            new XAttribute(Ns.W + "abstractNumId", abstractId),
            XmlUtil.ValElement(Ns.W + "multiLevelType", "hybridMultilevel"));

        // Word's default legal cycle: decimal, lower letter, lower roman, repeating.
        NumberFormat[] cycle = [format, NumberFormat.LowerLetter, NumberFormat.LowerRoman];

        for (var level = 0; level < 9; level++)
        {
            abstractNum.Add(NumberLevel(level, cycle[level % cycle.Length], level == 0 ? startAt : 1));
        }

        _root.AddFirst(abstractNum);
        return AddConcreteNumbering(abstractId);
    }

    /// <summary>
    /// Defines a multi-level list numbered <c>1.</c>, <c>1.1.</c>, <c>1.1.1.</c> and so on.
    /// </summary>
    public int AddOutlineList()
    {
        var abstractId = NextAbstractId();
        var abstractNum = new XElement(Ns.W + "abstractNum",
            new XAttribute(Ns.W + "abstractNumId", abstractId),
            XmlUtil.ValElement(Ns.W + "multiLevelType", "multilevel"));

        for (var level = 0; level < 9; level++)
        {
            // %1.%2.%3 — the placeholders are one-based level numbers, so level 2 (zero-based)
            // shows "%1.%2.%3.". Using zero-based indices here numbers everything wrong.
            var text = string.Concat(Enumerable.Range(1, level + 1).Select(n => $"%{n}."));

            var element = NumberLevel(level, NumberFormat.Decimal, 1);
            element.Element(Ns.W + "lvlText")?.SetAttributeValue(Ns.W + "val", text);

            // An outline level's indent grows with depth but the hanging indent stays wide enough
            // for the longest number the level can show.
            var indent = 360 * (level + 1);
            element.Element(Ns.W + "pPr")?.SetElementValue(Ns.W + "ind", null);
            element.Element(Ns.W + "pPr")?.Add(new XElement(Ns.W + "ind",
                new XAttribute(Ns.W + "left", indent + 360),
                new XAttribute(Ns.W + "hanging", 360 + level * 72)));

            abstractNum.Add(element);
        }

        _root.AddFirst(abstractNum);
        return AddConcreteNumbering(abstractId);
    }

    private XElement BulletLevel(int level, string text, string? font)
    {
        var indent = 720 * (level + 1);

        var runProperties = new XElement(Ns.W + "rPr");

        if (font is not null)
        {
            runProperties.Add(new XElement(Ns.W + "rFonts",
                new XAttribute(Ns.W + "ascii", font),
                new XAttribute(Ns.W + "hAnsi", font),
                // hint="default" is what tells Word to take the glyph from this font rather than
                // from the paragraph's font. Without it the Symbol bullet renders as "·" in some
                // versions and as a missing-glyph box in others.
                new XAttribute(Ns.W + "hint", "default")));
        }

        return new XElement(Ns.W + "lvl",
            new XAttribute(Ns.W + "ilvl", level),
            XmlUtil.ValElement(Ns.W + "start", "1"),
            XmlUtil.ValElement(Ns.W + "numFmt", "bullet"),
            XmlUtil.ValElement(Ns.W + "lvlText", text),
            XmlUtil.ValElement(Ns.W + "lvlJc", "left"),
            new XElement(Ns.W + "pPr",
                new XElement(Ns.W + "ind",
                    new XAttribute(Ns.W + "left", indent),
                    new XAttribute(Ns.W + "hanging", 360))),
            runProperties);
    }

    private XElement NumberLevel(int level, NumberFormat format, int startAt)
    {
        var indent = 720 * (level + 1);

        return new XElement(Ns.W + "lvl",
            new XAttribute(Ns.W + "ilvl", level),
            XmlUtil.ValElement(Ns.W + "start", XmlUtil.Num(startAt)),
            XmlUtil.ValElement(Ns.W + "numFmt", FormatName(format)),
            // %N is replaced by the level's counter. The trailing period is part of the text.
            XmlUtil.ValElement(Ns.W + "lvlText", $"%{level + 1}."),
            XmlUtil.ValElement(Ns.W + "lvlJc", "left"),
            new XElement(Ns.W + "pPr",
                new XElement(Ns.W + "ind",
                    new XAttribute(Ns.W + "left", indent),
                    new XAttribute(Ns.W + "hanging", 360))));
    }

    private static string FormatName(NumberFormat format) => format switch
    {
        NumberFormat.Bullet => "bullet",
        NumberFormat.LowerLetter => "lowerLetter",
        NumberFormat.UpperLetter => "upperLetter",
        NumberFormat.LowerRoman => "lowerRoman",
        NumberFormat.UpperRoman => "upperRoman",
        NumberFormat.DecimalZero => "decimalZero",
        NumberFormat.None => "none",
        _ => "decimal",
    };

    private int NextAbstractId()
    {
        var used = _root.Elements(Ns.W + "abstractNum")
            .Select(e => int.TryParse(e.Attr(Ns.W + "abstractNumId"), out var id) ? id : -1)
            .ToHashSet();

        var candidate = 0;
        while (used.Contains(candidate))
        {
            candidate++;
        }

        return candidate;
    }

    private int AddConcreteNumbering(int abstractId)
    {
        var used = NumberingIds.ToHashSet();
        var numId = 1;
        while (used.Contains(numId))
        {
            numId++;
        }

        // Every w:num must come after every w:abstractNum in the part. Word rejects the file when
        // they are interleaved, which is why the abstract definitions are added with AddFirst.
        _root.Add(new XElement(Ns.W + "num",
            new XAttribute(Ns.W + "numId", numId),
            XmlUtil.ValElement(Ns.W + "abstractNumId", XmlUtil.Num(abstractId))));

        _document.Touch();
        return numId;
    }

    /// <summary>
    /// Whether a list is bulleted rather than numbered at a given level.
    /// </summary>
    /// <remarks>
    /// The answer sits three references away from the paragraph: the paragraph names a
    /// <c>w:num</c>, the <c>w:num</c> names a <c>w:abstractNum</c>, and the level inside that says
    /// <c>bullet</c> or a number format. A <c>w:lvlOverride</c> on the <c>w:num</c> can replace the
    /// level outright, so it is consulted first. A reference that resolves to nothing is treated as
    /// bulleted: an unordered list is the least wrong reading of a list whose numbering is gone.
    /// </remarks>
    internal bool IsBulleted(int numberingId, int level)
    {
        var format = LevelDefinition(numberingId, level)
            ?.Element(Ns.W + "numFmt")?.Attribute(Ns.W + "val")?.Value;

        return format is null or "bullet" or "none";
    }

    /// <summary>The number a list level counts from; one when the definition does not say.</summary>
    /// <remarks>
    /// A <c>w:startOverride</c> on the <c>w:num</c> wins over the level's own <c>w:start</c>. It is how
    /// Word restarts a list without copying its whole definition, so ignoring it makes a restarted
    /// list continue.
    /// </remarks>
    internal int StartAt(int numberingId, int level)
    {
        var startOverride = (int?)NumElement(numberingId)?.Elements(Ns.W + "lvlOverride")
            .FirstOrDefault(o => (int?)o.Attribute(Ns.W + "ilvl") == level)
            ?.Element(Ns.W + "startOverride")?.Attribute(Ns.W + "val");

        return startOverride
               ?? (int?)LevelDefinition(numberingId, level)?.Element(Ns.W + "start")?.Attribute(Ns.W + "val")
               ?? 1;
    }

    private XElement? NumElement(int numberingId) =>
        _root.Elements(Ns.W + "num").FirstOrDefault(n => (int?)n.Attribute(Ns.W + "numId") == numberingId);

    /// <summary>The <c>w:lvl</c> in force for a list level, override first.</summary>
    private XElement? LevelDefinition(int numberingId, int level)
    {
        var num = NumElement(numberingId);

        if (num is null)
        {
            return null;
        }

        var overridden = num.Elements(Ns.W + "lvlOverride")
            .FirstOrDefault(o => (int?)o.Attribute(Ns.W + "ilvl") == level)
            ?.Element(Ns.W + "lvl");

        if (overridden is not null)
        {
            return overridden;
        }

        var abstractId = (int?)num.Element(Ns.W + "abstractNumId")?.Attribute(Ns.W + "val");

        return _root.Elements(Ns.W + "abstractNum")
            .FirstOrDefault(x => (int?)x.Attribute(Ns.W + "abstractNumId") == abstractId)
            ?.Elements(Ns.W + "lvl")
            .FirstOrDefault(l => (int?)l.Attribute(Ns.W + "ilvl") == level);
    }

    /// <summary>
    /// Creates a second list that shares an existing list's appearance but numbers independently.
    /// </summary>
    public int Restart(int existingNumberingId)
    {
        var existing = _root.Elements(Ns.W + "num")
            .FirstOrDefault(e => e.Attr(Ns.W + "numId") == existingNumberingId.ToString());

        if (existing is null)
        {
            throw new OfficeNetException($"There is no numbering with id {existingNumberingId}.");
        }

        var abstractId = int.TryParse(existing.Val(Ns.W + "abstractNumId"), out var id) ? id : 0;
        return AddConcreteNumbering(abstractId);
    }
}

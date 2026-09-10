// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Text;
using OfficeNet.Core.Drawing;

namespace OfficeNet.Core.Html;

/// <summary>How an HTML fragment is flattened into blocks.</summary>
public sealed class HtmlFlattenOptions
{
    /// <summary>Whether <c>img</c> elements are resolved and carried through.</summary>
    public bool IncludeImages { get; set; } = true;

    /// <summary>Where a relative <c>src</c> is resolved from.</summary>
    public string? BaseDirectory { get; set; }

    /// <summary>
    /// Supplies the bytes for a source this cannot read itself.
    /// </summary>
    /// <remarks>
    /// Conversion never makes a network request on its own. A remote image is fetched only if the
    /// caller hands over something that can fetch it, which keeps a document conversion from
    /// quietly becoming an outbound HTTP call.
    /// </remarks>
    public Func<string, byte[]?>? ImageResolver { get; set; }
}

/// <summary>What kind of content a parsed block holds.</summary>
public enum HtmlBlockKind
{
    Heading,
    Paragraph,
    ListItem,
    Table,
    Image,
    Code,
    Rule,
}

/// <summary>One formatted span of a block's text.</summary>
public readonly record struct HtmlSpan(string Text, CssStyle Style, string? Hyperlink);

/// <summary>A block-level piece of the source document.</summary>
public sealed class HtmlBlock
{
    public HtmlBlockKind Kind { get; init; }

    public int HeadingLevel { get; init; }

    public int ListLevel { get; init; }

    public bool Numbered { get; init; }

    /// <summary>
    /// Which list the item belongs to, counting outermost <c>ul</c> and <c>ol</c> elements from one.
    /// Zero for anything that is not a list item.
    /// </summary>
    /// <remarks>
    /// A flat run of items cannot say where one list ends and the next begins when two lists sit
    /// side by side, and a consumer that numbers by run continues the second list where the first
    /// left off. A nested list keeps its parent's id: it is part of the same list, one level down.
    /// </remarks>
    public int ListId { get; init; }

    /// <summary>
    /// The item's number within its list, or zero when nobody counted.
    /// </summary>
    /// <remarks>
    /// A numbered list interrupted by a paragraph is still one list, and its next item is 3, not 1.
    /// HTML cannot say that without <c>start</c>, and a writer cannot know the count unless whoever
    /// produced the blocks, which knows its own numbering, passes it on. Reading HTML never needs
    /// it; writing HTML from Word does.
    /// </remarks>
    public int ListNumber { get; init; }

    public List<HtmlSpan> Spans { get; init; } = [];

    public List<List<string>>? TableRows { get; init; }

    public bool TableHasHeader { get; init; }

    public byte[]? Image { get; init; }

    public string? AltText { get; init; }

    public CssStyle Style { get; init; }

    public string PlainText => string.Concat(Spans.Select(s => s.Text));

}


/// <summary>
/// Flattens an HTML fragment into a linear list of formatted blocks.
/// </summary>
/// <remarks>
/// <para>
/// This is the half of HTML conversion that has nothing to do with any particular output format:
/// what the document <em>says</em>, in order, with its inline formatting resolved. Turning those
/// blocks into slides, into paragraphs, or into anything else is the caller's half.
/// </para>
/// <para>
/// It lives in Core because otherwise each library would grow its own HTML reader, and they would
/// disagree — on which tags are containers, on how whitespace collapses, on what a nested list
/// level means. One reader that every converter shares is the point.
/// </para>
/// </remarks>
public static class HtmlFlattener
{
    /// <summary>Parses and flattens an HTML fragment.</summary>
    public static List<HtmlBlock> Flatten(string html, HtmlFlattenOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);

        return Flatten(HtmlParser.Parse(html), options);
    }

    /// <summary>
    /// Reads a single <c>table</c> element into a block, without walking anything around it.
    /// </summary>
    /// <remarks>
    /// For a caller that wants the table and nothing else — converting a report's one grid, rather
    /// than the page it sits on.
    /// </remarks>
    public static HtmlBlock FlattenTable(HtmlNode table, CssStyle style = default)
    {
        ArgumentNullException.ThrowIfNull(table);

        return ReadTable(table, style);
    }

    /// <summary>Flattens an already-parsed fragment.</summary>
    public static List<HtmlBlock> Flatten(HtmlNode root, HtmlFlattenOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(root);

        var blocks = new List<HtmlBlock>();
        Walk(root, default, 0, false, blocks, options ?? new HtmlFlattenOptions(), 0,
            new ListCounter());

        return blocks;
    }

    // ---- Walking the tree ----------------------------------------------------------------------

    private static readonly HashSet<string> Skipped = new(StringComparer.Ordinal)
    {
        "script", "style", "head", "meta", "link", "noscript", "template", "svg", "iframe",
    };

    /// <summary>Hands out list ids across the whole walk, which recursion alone cannot do.</summary>
    private sealed class ListCounter
    {
        public int Last;
    }

    private static void Walk(HtmlNode node, CssStyle inherited, int listLevel, bool numbered,
        List<HtmlBlock> blocks, HtmlFlattenOptions options, int listId, ListCounter counter)
    {
        foreach (var child in node.Children)
        {
            if (child.IsText)
            {
                // Loose text directly under a container becomes its own paragraph; ignoring it
                // silently drops content from fragments that are not fully marked up.
                var text = Collapse(child.Text ?? string.Empty);

                if (text.Trim().Length > 0)
                {
                    blocks.Add(new HtmlBlock
                    {
                        Kind = HtmlBlockKind.Paragraph,
                        Spans = [new HtmlSpan(text, inherited, null)],
                        Style = inherited,
                    });
                }

                continue;
            }

            if (Skipped.Contains(child.Name))
            {
                continue;
            }

            var style = inherited.Merge(CssStyle.Of(child));

            switch (child.Name)
            {
                case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                    blocks.Add(new HtmlBlock
                    {
                        Kind = HtmlBlockKind.Heading,
                        HeadingLevel = child.Name[1] - '0',
                        Spans = ReadSpans(child, style, null),
                        Style = style,
                    });
                    break;

                case "p" or "blockquote" or "figcaption" or "dd" or "dt":
                    AddParagraph(child, style, blocks);
                    break;

                case "ul" or "ol":
                    // Only an outermost list starts a new id. One reached through a container at
                    // list depth — a div inside an li — is still part of the list around it.
                    Walk(child, style, listLevel + 1, child.Name == "ol", blocks, options,
                        listLevel == 0 ? ++counter.Last : listId, counter);
                    break;

                case "li":
                    blocks.Add(new HtmlBlock
                    {
                        Kind = HtmlBlockKind.ListItem,
                        // A list at depth 1 is level 0 in DrawingML, which counts from zero.
                        ListLevel = Math.Max(0, Math.Min(listLevel - 1, 8)),
                        Numbered = numbered,
                        ListId = listId,
                        Spans = ReadSpans(child, style, null),
                        Style = style,
                    });

                    // A nested list inside the item continues at the next level.
                    foreach (var nested in child.Elements().Where(e => e.Name is "ul" or "ol"))
                    {
                        Walk(nested, style, listLevel + 1, nested.Name == "ol", blocks, options,
                            listId, counter);
                    }

                    break;

                case "table":
                    blocks.Add(ReadTable(child, style));
                    break;

                case "img":
                    if (options.IncludeImages && ResolveImage(child, options) is { } image)
                    {
                        blocks.Add(new HtmlBlock
                        {
                            Kind = HtmlBlockKind.Image,
                            Image = image,
                            AltText = child.Attribute("alt"),
                            Style = style,
                        });
                    }

                    break;

                case "pre":
                    blocks.Add(new HtmlBlock
                    {
                        Kind = HtmlBlockKind.Code,
                        Spans = [new HtmlSpan(child.InnerText.TrimEnd(), style, null)],
                        Style = style,
                    });
                    break;

                case "hr":
                    blocks.Add(new HtmlBlock { Kind = HtmlBlockKind.Rule, Style = style });
                    break;

                case "br":
                    break;

                default:
                    // A container (div, section, article, span at block level) contributes its
                    // children, not itself.
                    Walk(child, style, listLevel, numbered, blocks, options, listId, counter);
                    break;
            }
        }
    }

    private static void AddParagraph(HtmlNode node, CssStyle style, List<HtmlBlock> blocks)
    {
        var spans = ReadSpans(node, style, null);

        if (spans.Count == 0 || spans.All(s => s.Text.Trim().Length == 0))
        {
            return;
        }

        blocks.Add(new HtmlBlock
        {
            Kind = HtmlBlockKind.Paragraph,
            Spans = spans,
            Style = style,
        });
    }

    /// <summary>
    /// Flattens an element's inline content into formatted spans.
    /// </summary>
    /// <remarks>
    /// Nested lists and tables are skipped here: they are block content and are walked separately,
    /// so including their text would duplicate it into the parent paragraph as well.
    /// </remarks>
    private static List<HtmlSpan> ReadSpans(HtmlNode node, CssStyle inherited, string? hyperlink)
    {
        var spans = new List<HtmlSpan>();
        Collect(node, inherited, hyperlink, spans);
        return Coalesce(spans);

        static void Collect(HtmlNode node, CssStyle style, string? link, List<HtmlSpan> spans)
        {
            foreach (var child in node.Children)
            {
                if (child.IsText)
                {
                    var text = Collapse(child.Text ?? string.Empty);

                    if (text.Length > 0)
                    {
                        spans.Add(new HtmlSpan(text, style, link));
                    }

                    continue;
                }

                if (Skipped.Contains(child.Name) || child.Name is "ul" or "ol" or "table")
                {
                    continue;
                }

                if (child.Name == "br")
                {
                    spans.Add(new HtmlSpan("\n", style, link));
                    continue;
                }

                var childLink = child.Name == "a" ? child.Attribute("href") ?? link : link;
                Collect(child, style.Merge(CssStyle.Of(child)), childLink, spans);
            }
        }
    }

    /// <summary>
    /// Merges adjacent spans that share formatting.
    /// </summary>
    /// <remarks>
    /// A fragment like <c>&lt;span&gt;a&lt;/span&gt;&lt;span&gt;b&lt;/span&gt;</c> produces two
    /// spans with identical formatting. Writing them as two DrawingML runs is legal and doubles the
    /// run count of a text-heavy deck for no benefit.
    /// </remarks>
    private static List<HtmlSpan> Coalesce(List<HtmlSpan> spans)
    {
        var result = new List<HtmlSpan>(spans.Count);

        foreach (var span in spans)
        {
            if (result.Count > 0)
            {
                var previous = result[^1];

                if (previous.Style == span.Style && previous.Hyperlink == span.Hyperlink)
                {
                    result[^1] = previous with { Text = previous.Text + span.Text };
                    continue;
                }
            }

            result.Add(span);
        }

        // Leading and trailing whitespace on a block is layout, not content.
        if (result.Count > 0)
        {
            result[0] = result[0] with { Text = result[0].Text.TrimStart() };
            result[^1] = result[^1] with { Text = result[^1].Text.TrimEnd() };
        }

        return [.. result.Where(s => s.Text.Length > 0)];
    }

    /// <summary>
    /// Collapses runs of whitespace the way HTML rendering does.
    /// </summary>
    /// <remarks>
    /// Source HTML is indented, so its text nodes are full of newlines and runs of spaces that a
    /// browser collapses to one space. Copying them verbatim into a slide produces text riddled
    /// with gaps and line breaks that were never in the document.
    /// </remarks>
    private static string Collapse(string text)
    {
        var builder = new StringBuilder(text.Length);
        var inWhitespace = false;

        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!inWhitespace)
                {
                    builder.Append(' ');
                    inWhitespace = true;
                }

                continue;
            }

            builder.Append(c);
            inWhitespace = false;
        }

        return builder.ToString();
    }

    private static HtmlBlock ReadTable(HtmlNode table, CssStyle style)
    {
        var rows = new List<List<string>>();
        var hasHeader = false;

        // thead/tbody/tfoot are optional in the source and often absent; collecting every tr in
        // document order gets the same result whether they are there or not.
        foreach (var row in table.Descendants("tr"))
        {
            var cells = new List<string>();
            var isHeaderRow = true;

            foreach (var cell in row.Elements().Where(e => e.Name is "td" or "th"))
            {
                cells.Add(Collapse(cell.InnerText).Trim());

                if (cell.Name != "th")
                {
                    isHeaderRow = false;
                }
            }

            if (cells.Count == 0)
            {
                continue;
            }

            if (rows.Count == 0 && isHeaderRow)
            {
                hasHeader = true;
            }

            rows.Add(cells);
        }

        // A ragged table (a row with a colspan, or simply fewer cells) must still be rectangular
        // on the slide, or the columns after the gap shift left.
        var width = rows.Count == 0 ? 0 : rows.Max(r => r.Count);

        foreach (var row in rows)
        {
            while (row.Count < width)
            {
                row.Add(string.Empty);
            }
        }

        return new HtmlBlock
        {
            Kind = HtmlBlockKind.Table,
            TableRows = rows,
            TableHasHeader = hasHeader,
            Style = style,
            Spans = [new HtmlSpan(table.Attribute("summary") ?? string.Empty, style, null)],
        };
    }

    /// <summary>
    /// Resolves an <c>&lt;img&gt;</c> to bytes: a data URI, a local file, or the caller's resolver.
    /// </summary>
    private static byte[]? ResolveImage(HtmlNode image, HtmlFlattenOptions options)
    {
        var source = image.Attribute("src");

        if (string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        if (source.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var comma = source.IndexOf(',');

            if (comma < 0)
            {
                return null;
            }

            var header = source[..comma];

            if (!header.Contains(";base64", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            try
            {
                return System.Convert.FromBase64String(source[(comma + 1)..].Trim());
            }
            catch (FormatException)
            {
                return null;
            }
        }

        // A remote URL is only fetched through the caller's resolver; conversion never makes a
        // network request on its own.
        if (source.StartsWith("http:", StringComparison.OrdinalIgnoreCase) ||
            source.StartsWith("https:", StringComparison.OrdinalIgnoreCase) ||
            source.StartsWith("//", StringComparison.Ordinal))
        {
            return options.ImageResolver?.Invoke(source);
        }

        var path = source.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
            ? new Uri(source).LocalPath
            : source;

        if (!Path.IsPathRooted(path) && options.BaseDirectory is { Length: > 0 } baseDirectory)
        {
            path = Path.Combine(baseDirectory, path);
        }

        try
        {
            return File.Exists(path) ? File.ReadAllBytes(path) : options.ImageResolver?.Invoke(source);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                       ArgumentException or NotSupportedException)
        {
            return options.ImageResolver?.Invoke(source);
        }
    }
}

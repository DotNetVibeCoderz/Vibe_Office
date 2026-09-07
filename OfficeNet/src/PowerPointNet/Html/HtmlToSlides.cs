// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Text;
using System.Xml.Linq;
using OfficeNet.Core;
using OfficeNet.Core.Drawing;
using PowerPointNet.Shapes;

namespace PowerPointNet.Html;

/// <summary>How an HTML fragment is turned into slides.</summary>
public sealed class HtmlSlideOptions
{
    /// <summary>
    /// The deepest heading level that starts a new slide; 2 means <c>h1</c> and <c>h2</c> do.
    /// </summary>
    /// <remarks>
    /// Set it to 1 for a document whose <c>h2</c>s are subsections of one slide, or to 6 to give
    /// every heading its own slide. A fragment with no headings at all becomes one slide per
    /// content run, paginated by <see cref="MaxLinesPerSlide"/>.
    /// </remarks>
    public int SplitOnHeadingLevel { get; set; } = 2;

    /// <summary>A title slide to open with; <c>null</c> for none.</summary>
    public string? TitleSlide { get; set; }

    /// <summary>The subtitle of the title slide.</summary>
    public string? SubtitleSlide { get; set; }

    /// <summary>Draws <c>&lt;img&gt;</c> elements.</summary>
    public bool IncludeImages { get; set; } = true;

    /// <summary>
    /// Resolves an <c>&lt;img src&gt;</c> to image bytes.
    /// </summary>
    /// <remarks>
    /// <c>data:</c> URIs and local file paths are handled without this. A remote URL is not
    /// fetched unless a resolver is supplied — converting a document must not silently make
    /// network requests, which would be both a surprise and a way to leak that a file was opened.
    /// </remarks>
    public Func<string, byte[]?>? ImageResolver { get; set; }

    /// <summary>The directory relative image paths are resolved against.</summary>
    public string? BaseDirectory { get; set; }

    /// <summary>How many body lines fit on one slide before it paginates.</summary>
    public int MaxLinesPerSlide { get; set; } = 9;

    /// <summary>How many table rows fit on one slide before the table paginates.</summary>
    public int MaxTableRowsPerSlide { get; set; } = 9;

    /// <summary>The body text size when the HTML does not specify one.</summary>
    public double BodyFontSizePoints { get; set; } = 18;

    /// <summary>Adds each slide's source heading path as speaker notes.</summary>
    public bool AddSourceNotes { get; set; }

    /// <summary>The layout index used for content slides.</summary>
    public int ContentLayoutIndex { get; set; } = 1;

    /// <summary>The layout index used for slides that hold only a table or picture.</summary>
    public int BlankLayoutIndex { get; set; } = 2;
}

/// <summary>What kind of content a parsed block holds.</summary>
internal enum HtmlBlockKind
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
internal readonly record struct HtmlSpan(string Text, CssStyle Style, string? Hyperlink);

/// <summary>A block-level piece of the source document.</summary>
internal sealed class HtmlBlock
{
    internal HtmlBlockKind Kind { get; init; }

    internal int HeadingLevel { get; init; }

    internal int ListLevel { get; init; }

    internal bool Numbered { get; init; }

    internal List<HtmlSpan> Spans { get; init; } = [];

    internal List<List<string>>? TableRows { get; init; }

    internal bool TableHasHeader { get; init; }

    internal byte[]? Image { get; init; }

    internal string? AltText { get; init; }

    internal CssStyle Style { get; init; }

    internal string PlainText => string.Concat(Spans.Select(s => s.Text));

    /// <summary>Roughly how many rendered lines the block occupies, for pagination.</summary>
    internal int LineCost => Kind switch
    {
        HtmlBlockKind.Image => 6,
        HtmlBlockKind.Rule => 1,
        HtmlBlockKind.Table => (TableRows?.Count ?? 0) + 1,
        // A long paragraph wraps; 90 characters is about one line at 18 pt across a 16:9 slide.
        _ => Math.Max(1, (PlainText.Length + 89) / 90),
    };
}

/// <summary>
/// Converts an HTML fragment into slides — the feature PptxGenJS calls <c>html2ppt</c>.
/// </summary>
/// <remarks>
/// <para>
/// The conversion is structural, not visual. HTML reflows to its container and a slide does not,
/// so the useful thing to preserve is the document's <em>outline</em>: headings become slide
/// titles, the content under each heading becomes that slide's body, and content that does not fit
/// paginates onto a continuation slide rather than overflowing off the bottom.
/// </para>
/// <para>
/// Inline formatting survives run by run — bold, italic, underline, strikethrough, colour, font,
/// size and links all map onto DrawingML runs. Layout does not: floats, columns, absolute
/// positioning and the box model have no counterpart in a text frame and are ignored rather than
/// approximated into something that looks broken.
/// </para>
/// </remarks>
public static class HtmlToSlides
{
    /// <summary>Creates a new presentation from an HTML fragment.</summary>
    public static Presentation CreatePresentation(string html, HtmlSlideOptions? options = null)
    {
        var presentation = Presentation.Create();

        try
        {
            Convert(presentation, html, options);
            return presentation;
        }
        catch
        {
            presentation.Dispose();
            throw;
        }
    }

    /// <summary>Creates a presentation from an HTML file.</summary>
    public static Presentation CreatePresentationFromFile(string path, HtmlSlideOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        options ??= new HtmlSlideOptions();
        // Relative <img src> paths in a file are relative to that file, not to the process.
        options.BaseDirectory ??= Path.GetDirectoryName(Path.GetFullPath(path));

        return CreatePresentation(File.ReadAllText(path), options);
    }

    /// <summary>Appends slides built from an HTML fragment to an existing presentation.</summary>
    /// <returns>The slides that were added.</returns>
    public static IReadOnlyList<Slide> Convert(Presentation presentation, string html,
        HtmlSlideOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(html);

        options ??= new HtmlSlideOptions();

        var root = HtmlParser.Parse(html);
        var blocks = new List<HtmlBlock>();
        Walk(root, default, 0, false, blocks, options);

        var added = new List<Slide>();

        if (options.TitleSlide is { Length: > 0 } title)
        {
            added.Add(presentation.AddTitleSlide(title, options.SubtitleSlide));
        }

        foreach (var group in GroupIntoSlides(blocks, options))
        {
            added.AddRange(Render(presentation, group, options));
        }

        // A fragment with no renderable content still has to produce something, or the caller gets
        // a presentation with zero slides — which PowerPoint cannot open.
        if (added.Count == 0)
        {
            added.Add(presentation.AddSlide(options.BlankLayoutIndex));
        }

        return added;
    }

    // ---- Walking the tree ----------------------------------------------------------------------

    private static readonly HashSet<string> Skipped = new(StringComparer.Ordinal)
    {
        "script", "style", "head", "meta", "link", "noscript", "template", "svg", "iframe",
    };

    private static void Walk(HtmlNode node, CssStyle inherited, int listLevel, bool numbered,
        List<HtmlBlock> blocks, HtmlSlideOptions options)
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
                    Walk(child, style, listLevel + 1, child.Name == "ol", blocks, options);
                    break;

                case "li":
                    blocks.Add(new HtmlBlock
                    {
                        Kind = HtmlBlockKind.ListItem,
                        // A list at depth 1 is level 0 in DrawingML, which counts from zero.
                        ListLevel = Math.Max(0, Math.Min(listLevel - 1, 8)),
                        Numbered = numbered,
                        Spans = ReadSpans(child, style, null),
                        Style = style,
                    });

                    // A nested list inside the item continues at the next level.
                    foreach (var nested in child.Elements().Where(e => e.Name is "ul" or "ol"))
                    {
                        Walk(nested, style, listLevel + 1, nested.Name == "ol", blocks, options);
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
                    Walk(child, style, listLevel, numbered, blocks, options);
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
    private static byte[]? ResolveImage(HtmlNode image, HtmlSlideOptions options)
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

    // ---- Grouping into slides --------------------------------------------------------------------

    private sealed class SlideGroup
    {
        internal HtmlBlock? Heading { get; set; }

        internal List<HtmlBlock> Body { get; } = [];

        internal string? SourcePath { get; set; }
    }

    private static List<SlideGroup> GroupIntoSlides(List<HtmlBlock> blocks, HtmlSlideOptions options)
    {
        var groups = new List<SlideGroup>();
        var current = new SlideGroup();
        var cost = 0;
        var path = new string[7];
        var headingEmitted = false;

        void Flush()
        {
            // A group carrying only a heading is worth a slide the first time — a section divider
            // — but not afterwards. Splitting before and after a table leaves a carried-over
            // heading with nothing under it, and emitting that produces a blank duplicate slide.
            if (current.Body.Count > 0 || (current.Heading is not null && !headingEmitted))
            {
                groups.Add(current);

                if (current.Heading is not null)
                {
                    headingEmitted = true;
                }
            }

            current = new SlideGroup();
            cost = 0;
        }

        foreach (var block in blocks)
        {
            if (block.Kind == HtmlBlockKind.Heading)
            {
                var level = Math.Clamp(block.HeadingLevel, 1, 6);
                path[level] = block.PlainText;

                for (var deeper = level + 1; deeper < path.Length; deeper++)
                {
                    path[deeper] = string.Empty;
                }

                if (level <= options.SplitOnHeadingLevel)
                {
                    Flush();

                    headingEmitted = false;
                    current.Heading = block;
                    current.SourcePath = string.Join(" › ",
                        path.Where(p => !string.IsNullOrEmpty(p)));

                    continue;
                }

                // A heading below the split level becomes a bold line in the body rather than a
                // new slide, so the document's structure survives without exploding the deck.
                current.Body.Add(block);
                cost += block.LineCost;
                continue;
            }

            // A table or an image large enough to fill a slide gets one of its own rather than
            // being squeezed under whatever preceded it.
            if (block.Kind is HtmlBlockKind.Table or HtmlBlockKind.Image &&
                (cost > 0 || current.Body.Count > 0))
            {
                var heading = current.Heading;
                var source = current.SourcePath;
                Flush();
                current.Heading = heading;
                current.SourcePath = source;
            }

            if (cost + block.LineCost > options.MaxLinesPerSlide && current.Body.Count > 0)
            {
                var heading = current.Heading;
                var source = current.SourcePath;
                Flush();

                // A continuation slide keeps the heading so the reader knows where they are.
                current.Heading = heading;
                current.SourcePath = source;
            }

            current.Body.Add(block);
            cost += block.LineCost;

            if (block.Kind is HtmlBlockKind.Table or HtmlBlockKind.Image)
            {
                var heading = current.Heading;
                var source = current.SourcePath;
                Flush();
                current.Heading = heading;
                current.SourcePath = source;
            }
        }

        Flush();
        return groups;
    }

    // ---- Rendering -------------------------------------------------------------------------------

    private static IEnumerable<Slide> Render(Presentation presentation, SlideGroup group,
        HtmlSlideOptions options)
    {
        var table = group.Body.FirstOrDefault(b => b.Kind == HtmlBlockKind.Table);

        if (table?.TableRows is { Count: > 0 })
        {
            foreach (var slide in RenderTable(presentation, group, table, options))
            {
                yield return slide;
            }

            yield break;
        }

        var picture = group.Body.FirstOrDefault(b => b.Kind == HtmlBlockKind.Image);

        if (picture?.Image is not null)
        {
            yield return RenderImage(presentation, group, picture, options);
            yield break;
        }

        yield return RenderText(presentation, group, options);
    }

    private static Slide RenderText(Presentation presentation, SlideGroup group,
        HtmlSlideOptions options)
    {
        var slide = presentation.AddSlide(options.ContentLayoutIndex);

        if (group.Heading is { } heading)
        {
            slide.SetTitle(heading.PlainText);
        }

        var body = group.Body.Where(b => b.Kind is not (HtmlBlockKind.Table or HtmlBlockKind.Image))
            .ToList();

        if (body.Count > 0)
        {
            var placeholder = slide.Body ?? slide.SetBody([string.Empty]);
            var frame = placeholder.TextFrame!;
            frame.Element.Elements(OfficeNet.Core.Xml.Ns.A + "p").Remove();

            foreach (var block in body)
            {
                WriteBlock(frame, block, options);
            }

            if (frame.Paragraphs.Count == 0)
            {
                frame.AddParagraph();
            }

            // Shrinking rather than overflowing is what keeps a converted slide readable when the
            // source paragraph was longer than the estimate.
            frame.AutoFit = AutoFitMode.ShrinkText;
        }

        if (options.AddSourceNotes && group.SourcePath is { Length: > 0 } path)
        {
            slide.Notes = path;
        }

        return slide;
    }

    private static void WriteBlock(TextFrame frame, HtmlBlock block, HtmlSlideOptions options)
    {
        if (block.Kind == HtmlBlockKind.Rule)
        {
            // A horizontal rule has no text-frame equivalent; an empty line is the honest
            // stand-in for the break it represents.
            frame.AddParagraph();
            return;
        }

        var paragraph = frame.AddParagraph(string.Empty, block.ListLevel);

        paragraph.HasBullet = block.Kind == HtmlBlockKind.ListItem;

        if (block.Kind == HtmlBlockKind.ListItem && block.Numbered)
        {
            paragraph.SetNumbered();
        }

        if (block.Style.Alignment is { } alignment)
        {
            paragraph.Alignment = alignment;
        }

        if (block.Spans.Count == 0)
        {
            return;
        }

        foreach (var span in block.Spans)
        {
            // A <br> inside a paragraph is a line break, not a new paragraph.
            var pieces = span.Text.Split('\n');

            for (var i = 0; i < pieces.Length; i++)
            {
                if (i > 0)
                {
                    paragraph.AddLineBreak();
                }

                if (pieces[i].Length == 0)
                {
                    continue;
                }

                var run = paragraph.AddRun(pieces[i]);
                ApplyStyle(run, span.Style, block, options);

                if (span.Hyperlink is { Length: > 0 } href && IsLinkable(href))
                {
                    run.SetHyperlink(href);
                }
            }
        }
    }

    /// <summary>
    /// True when a link target can be written as a relationship.
    /// </summary>
    /// <remarks>
    /// An in-page anchor (<c>#section</c>) has no meaning in a deck and would produce a
    /// relationship pointing at nothing, which PowerPoint reports as needing repair.
    /// </remarks>
    private static bool IsLinkable(string href) =>
        !href.StartsWith('#') && !href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase);

    private static void ApplyStyle(TextRun run, CssStyle style, HtmlBlock block,
        HtmlSlideOptions options)
    {
        if (style.Bold is { } bold)
        {
            run.Bold = bold;
        }

        if (style.Italic is { } italic)
        {
            run.Italic = italic;
        }

        if (style.Underline is { } underline)
        {
            run.Underline = underline;
        }

        if (style.Color is { } color)
        {
            run.Color = color;
        }

        if (style.FontFamily is { } family)
        {
            run.FontName = family;
        }

        // A heading inside the body keeps its relative prominence; everything else takes the
        // configured body size unless the source named one.
        run.FontSize = style.FontSize
                       ?? (block.Kind == HtmlBlockKind.Heading
                           ? Units.Pt(Math.Max(options.BodyFontSizePoints,
                               24 - (block.HeadingLevel - 1) * 2))
                           : Units.Pt(options.BodyFontSizePoints));

        if (block.Kind == HtmlBlockKind.Code)
        {
            run.FontName = "Consolas";
            run.FontSize = Units.Pt(Math.Max(10, options.BodyFontSizePoints - 4));
        }
    }

    private static Slide RenderImage(Presentation presentation, SlideGroup group, HtmlBlock block,
        HtmlSlideOptions options)
    {
        var slide = presentation.AddSlide(options.BlankLayoutIndex);

        if (group.Heading is { } heading)
        {
            slide.SetTitle(heading.PlainText);
        }

        var top = group.Heading is null ? Units.Inches(0.8) : Units.Inches(1.8);
        var available = presentation.SlideHeight - top - Units.Inches(0.6);
        var maxWidth = presentation.SlideWidth - Units.Inches(1.6);

        try
        {
            var info = ImageInfo.Read(block.Image!);

            // Fit inside the box while keeping the aspect ratio — the "contain" behaviour, which
            // is what a reader expects and what stops a tall screenshot spilling off the slide.
            var width = maxWidth;
            var height = Length.FromEmu((long)(width.Emu / info.AspectRatio));

            if (height > available)
            {
                height = available;
                width = Length.FromEmu((long)(height.Emu * info.AspectRatio));
            }

            var left = (presentation.SlideWidth - width) / 2;

            var picture = slide.AddPicture(block.Image!, left, top, width, height);
            picture.AltText = block.AltText;
        }
        catch (OfficeNetException)
        {
            // An unreadable image becomes a note rather than an exception; one broken <img> must
            // not abort a whole document's conversion.
            slide.AddTextBox($"[gambar tidak dapat dibaca: {block.AltText ?? "tanpa keterangan"}]",
                Units.Inches(0.8), top, maxWidth, Units.Inches(0.6));
        }

        if (options.AddSourceNotes && group.SourcePath is { Length: > 0 } path)
        {
            slide.Notes = path;
        }

        return slide;
    }

    private static IEnumerable<Slide> RenderTable(Presentation presentation, SlideGroup group,
        HtmlBlock block, HtmlSlideOptions options)
    {
        var rows = block.TableRows!;
        var header = block.TableHasHeader ? rows[0] : null;
        var dataRows = block.TableHasHeader ? rows.Skip(1).ToList() : rows;

        var perSlide = Math.Max(1, options.MaxTableRowsPerSlide - (header is null ? 0 : 1));
        var index = 0;
        var page = 0;

        while (index < dataRows.Count || page == 0)
        {
            var chunk = dataRows.Skip(index).Take(perSlide).ToList();
            index += perSlide;
            page++;

            var slide = presentation.AddSlide(options.BlankLayoutIndex);

            if (group.Heading is { } heading)
            {
                // A continuation slide says so, rather than repeating the title as if it were the
                // same table starting again.
                slide.SetTitle(page == 1
                    ? heading.PlainText
                    : $"{heading.PlainText} ({page})");
            }

            var top = group.Heading is null ? Units.Inches(0.8) : Units.Inches(1.8);
            var left = Units.Inches(0.7);
            var width = presentation.SlideWidth - Units.Inches(1.4);

            var displayed = new List<List<string>>();

            if (header is not null)
            {
                displayed.Add(header);
            }

            displayed.AddRange(chunk);

            if (displayed.Count == 0)
            {
                displayed.Add([string.Empty]);
            }

            var columns = Math.Max(1, displayed.Max(r => r.Count));
            var rowHeight = Units.Inches(0.4);
            var height = rowHeight * displayed.Count;

            var maximum = presentation.SlideHeight - top - Units.Inches(0.5);

            if (height > maximum)
            {
                height = maximum;
            }

            var table = slide.AddTable(displayed.Count, columns, left, top, width, height);
            table.HasHeaderRow = header is not null;
            table.SetData(displayed, boldFirstRow: header is not null);

            if (options.AddSourceNotes && group.SourcePath is { Length: > 0 } path)
            {
                slide.Notes = path;
            }

            yield return slide;

            if (index >= dataRows.Count)
            {
                yield break;
            }
        }
    }

    /// <summary>
    /// Splits one HTML table across as many slides as it needs, repeating the header row.
    /// </summary>
    /// <remarks>
    /// The direct equivalent of PptxGenJS's <c>tableToSlides</c>. Use it when the input is one
    /// large table rather than a document — a query result, an export, a report grid — and the
    /// point is to page through it rather than to reproduce a document's outline.
    /// </remarks>
    public static IReadOnlyList<Slide> TableToSlides(Presentation presentation, string html,
        string? title = null, HtmlSlideOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(html);

        options ??= new HtmlSlideOptions();

        var root = HtmlParser.Parse(html);
        var element = root.Descendants("table").FirstOrDefault()
            ?? throw new OfficeNetException("The HTML contains no <table> element.");

        var block = ReadTable(element, default);

        var group = new SlideGroup
        {
            Heading = title is null
                ? null
                : new HtmlBlock
                {
                    Kind = HtmlBlockKind.Heading,
                    HeadingLevel = 1,
                    Spans = [new HtmlSpan(title, default, null)],
                },
        };

        return [.. RenderTable(presentation, group, block, options)];
    }
}

// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using OfficeNet.Core;
using OfficeNet.Core.Drawing;
using OfficeNet.Core.Html;
using WordNet.Numbering;
using WordNet.Tables;

namespace WordNet.Import;

/// <summary>How an HTML fragment is turned into a document.</summary>
public sealed class HtmlWordOptions
{
    /// <summary>Whether <c>img</c> elements are converted to pictures.</summary>
    public bool IncludeImages { get; set; } = true;

    /// <summary>Where a relative image <c>src</c> is resolved from.</summary>
    public string? BaseDirectory { get; set; }

    /// <summary>
    /// Supplies the bytes for an image this cannot read itself.
    /// </summary>
    /// <remarks>
    /// Conversion never makes a network request on its own. A remote image is fetched only if the
    /// caller supplies something that can fetch it.
    /// </remarks>
    public Func<string, byte[]?>? ImageResolver { get; set; }

    /// <summary>The width a picture is scaled to when it would otherwise overflow the page.</summary>
    public Length MaxImageWidth { get; set; } = Length.FromCentimeters(15);

    /// <summary>The font used for <c>pre</c> and <c>code</c>.</summary>
    public string MonospaceFont { get; set; } = "Consolas";

    /// <summary>The body font size, in points, when the HTML does not say.</summary>
    public double DefaultFontSizePoints { get; set; } = 11;

    internal HtmlFlattenOptions ForFlattening() => new()
    {
        IncludeImages = IncludeImages,
        BaseDirectory = BaseDirectory,
        ImageResolver = ImageResolver,
    };
}

/// <summary>
/// Converts an HTML fragment into WordprocessingML.
/// </summary>
/// <remarks>
/// <para>
/// The reading is shared with every other HTML converter in OfficeNet:
/// <see cref="HtmlFlattener"/> turns the markup into an ordered list of blocks with their inline
/// formatting resolved, and this maps those blocks onto paragraphs, runs and tables. Two converters
/// that read HTML separately would disagree about it, and the disagreements would be the kind
/// nobody notices until a document comes out subtly different from the deck made from the same
/// page.
/// </para>
/// <para>
/// The conversion is structural. A document reflows exactly as HTML does, so more survives here
/// than in the slide converter: headings keep their level, lists keep their nesting, tables keep
/// their shape, and inline formatting maps run for run. What does not survive is layout — floats,
/// columns, absolute positioning and the box model have no counterpart in WordprocessingML and are
/// ignored rather than approximated.
/// </para>
/// </remarks>
public static class HtmlToWord
{
    /// <summary>Creates a document from an HTML fragment.</summary>
    public static WordDocument CreateDocument(string html, HtmlWordOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(html);

        var document = WordDocument.Create();

        try
        {
            Convert(document, html, options);
            return document;
        }
        catch
        {
            document.Dispose();
            throw;
        }
    }

    /// <summary>Creates a document from an HTML file, resolving images beside it.</summary>
    public static WordDocument CreateDocumentFromFile(string path, HtmlWordOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(path);

        options ??= new HtmlWordOptions();
        options.BaseDirectory ??= Path.GetDirectoryName(Path.GetFullPath(path));

        return CreateDocument(File.ReadAllText(path), options);
    }

    /// <summary>
    /// Appends an HTML fragment to a document that already exists.
    /// </summary>
    /// <returns>The blocks that were written, in order.</returns>
    public static IReadOnlyList<object> Convert(WordDocument document, string html,
        HtmlWordOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(html);

        options ??= new HtmlWordOptions();

        var blocks = HtmlFlattener.Flatten(html, options.ForFlattening());
        var written = new List<object>();

        // One numbering definition per list, so a second list restarts at one instead of
        // continuing the first. Word numbers by definition, not by position on the page, and two
        // lists side by side have no block between them to mark the change — which is why the
        // flattener records the list each item came from.
        var lists = new ListNumbering(document);
        var currentList = 0;

        foreach (var block in blocks)
        {
            if (block.Kind != HtmlBlockKind.ListItem || block.ListId != currentList)
            {
                lists.Break();
            }

            currentList = block.Kind == HtmlBlockKind.ListItem ? block.ListId : 0;

            var result = WriteBlock(document, block, options, lists);

            if (result is not null)
            {
                written.Add(result);
            }
        }

        return written;
    }

    private static object? WriteBlock(WordDocument document, HtmlBlock block,
        HtmlWordOptions options, ListNumbering lists) => block.Kind switch
    {
        HtmlBlockKind.Heading => WriteHeading(document, block, options),
        HtmlBlockKind.Paragraph => WriteParagraph(document, block, options),
        HtmlBlockKind.ListItem => WriteListItem(document, block, options, lists),
        HtmlBlockKind.Table => WriteTable(document, block),
        HtmlBlockKind.Image => WriteImage(document, block, options),
        HtmlBlockKind.Code => WriteCode(document, block, options),
        HtmlBlockKind.Rule => WriteRule(document),
        _ => null,
    };

    private static Paragraph WriteHeading(WordDocument document, HtmlBlock block,
        HtmlWordOptions options)
    {
        // Word's built-in heading styles run 1 to 9; HTML stops at 6, so no clamping is needed
        // beyond guarding against a level of zero.
        var paragraph = document.AddHeading(string.Empty, Math.Clamp(block.HeadingLevel, 1, 9));

        WriteSpans(paragraph, block, options, applyStyleFont: false);
        ApplyAlignment(paragraph, block);

        return paragraph;
    }

    private static Paragraph WriteParagraph(WordDocument document, HtmlBlock block,
        HtmlWordOptions options)
    {
        var paragraph = document.AddParagraph();

        WriteSpans(paragraph, block, options, applyStyleFont: true);
        ApplyAlignment(paragraph, block);

        return paragraph;
    }

    private static Paragraph WriteListItem(WordDocument document, HtmlBlock block,
        HtmlWordOptions options, ListNumbering lists)
    {
        var paragraph = document.AddParagraph();

        WriteSpans(paragraph, block, options, applyStyleFont: true);
        paragraph.SetListItem(lists.IdFor(block.Numbered), Math.Clamp(block.ListLevel, 0, 8));
        ApplyAlignment(paragraph, block);

        return paragraph;
    }

    private static Paragraph WriteCode(WordDocument document, HtmlBlock block,
        HtmlWordOptions options)
    {
        // A pre block keeps its line breaks, which is the whole point of it. Each line becomes a
        // break inside one paragraph rather than a paragraph of its own, so the block stays one
        // unit for spacing and for selection.
        var paragraph = document.AddParagraph();
        var lines = block.PlainText.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                paragraph.AddRun().AddBreak();
            }

            var run = paragraph.AddRun(lines[i]);
            run.Format.FontName = options.MonospaceFont;
            run.Format.FontSize = Length.FromPoints(options.DefaultFontSizePoints - 1);
        }

        return paragraph;
    }

    private static Table WriteTable(WordDocument document, HtmlBlock block)
    {
        var rows = block.TableRows ?? [];

        if (rows.Count == 0)
        {
            return document.AddTable(1, 1);
        }

        return document.AddTable(rows, block.TableHasHeader);
    }

    private static Paragraph? WriteImage(WordDocument document, HtmlBlock block,
        HtmlWordOptions options)
    {
        if (block.Image is not { Length: > 0 } bytes)
        {
            return null;
        }

        try
        {
            return document.AddPicture(bytes, options.MaxImageWidth, altText: block.AltText);
        }
        catch (OfficeNetException)
        {
            // An image whose format the library cannot read should not take the conversion down.
            // The alt text is what a reader would have been given anyway.
            return block.AltText is { Length: > 0 } alt ? document.AddParagraph(alt) : null;
        }
    }

    private static Paragraph WriteRule(WordDocument document)
    {
        // WordprocessingML has no horizontal rule element. An empty paragraph with a bottom border
        // is what Word itself produces for one, and it survives a round trip through Word.
        var paragraph = document.AddParagraph();

        paragraph.Format.SetBottomBorder(BorderStyle.Single,
            OfficeColor.FromRgb(0xA0, 0xA0, 0xA0), Length.FromPoints(0.5));

        return paragraph;
    }

    private static void WriteSpans(Paragraph paragraph, HtmlBlock block, HtmlWordOptions options,
        bool applyStyleFont)
    {
        foreach (var span in block.Spans)
        {
            if (span.Text.Length == 0)
            {
                continue;
            }

            var run = span.Hyperlink is { Length: > 0 } url
                ? paragraph.AddHyperlink(span.Text, url)
                : paragraph.AddRun(span.Text);

            ApplyStyle(run, span.Style, options, applyStyleFont);
        }
    }

    private static void ApplyStyle(Run run, CssStyle style, HtmlWordOptions options,
        bool applyStyleFont)
    {
        if (style.Bold is { } bold)
        {
            run.Format.Bold = bold;
        }

        if (style.Italic is { } italic)
        {
            run.Format.Italic = italic;
        }

        if (style.Underline == true)
        {
            run.Format.Underline = UnderlineStyle.Single;
        }

        if (style.Strike is { } strike)
        {
            run.Format.Strike = strike;
        }

        if (style.Color is { } color)
        {
            run.Format.Color = color;
        }

        if (style.BackgroundColor is { } background)
        {
            run.Format.Highlight = NearestHighlight(background);
        }

        // A heading's size and face come from its style; overriding them from CSS would replace
        // the document's own typography with the web page's, which is rarely what is wanted and is
        // impossible to undo afterwards.
        if (!applyStyleFont)
        {
            return;
        }

        if (style.FontFamily is { Length: > 0 } family)
        {
            run.Format.FontName = family;
        }

        if (style.FontSize is { } size && size.Points > 0)
        {
            run.Format.FontSize = size;
        }
    }

    private static void ApplyAlignment(Paragraph paragraph, HtmlBlock block)
    {
        if (block.Style.Alignment is { } alignment)
        {
            paragraph.Alignment = alignment switch
            {
                TextAlign.Center => ParagraphAlignment.Center,
                TextAlign.Right => ParagraphAlignment.Right,
                TextAlign.Justify => ParagraphAlignment.Justify,
                _ => ParagraphAlignment.Left,
            };
        }
    }

    /// <summary>
    /// The closest of Word's fixed highlight colours to an arbitrary CSS colour.
    /// </summary>
    /// <remarks>
    /// <c>w:highlight</c> takes a name from a fixed list, not a value — a background of #FFF9C4 has
    /// to become one of about a dozen colours or nothing at all. Nearest by squared distance in RGB
    /// is crude but predictable, and dropping the background entirely loses information a reader
    /// can see.
    /// </remarks>
    private static string NearestHighlight(OfficeColor color)
    {
        (string Name, int R, int G, int B)[] palette =
        [
            ("yellow", 255, 255, 0), ("green", 0, 255, 0), ("cyan", 0, 255, 255),
            ("magenta", 255, 0, 255), ("blue", 0, 0, 255), ("red", 255, 0, 0),
            ("darkBlue", 0, 0, 139), ("darkCyan", 0, 139, 139), ("darkGreen", 0, 100, 0),
            ("darkMagenta", 139, 0, 139), ("darkRed", 139, 0, 0), ("darkYellow", 128, 128, 0),
            ("darkGray", 169, 169, 169), ("lightGray", 211, 211, 211), ("black", 0, 0, 0),
            ("white", 255, 255, 255),
        ];

        var best = "yellow";
        var bestDistance = int.MaxValue;

        foreach (var (name, r, g, b) in palette)
        {
            var dr = color.R - r;
            var dg = color.G - g;
            var db = color.B - b;
            var distance = (dr * dr) + (dg * dg) + (db * db);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = name;
            }
        }

        return best;
    }

    /// <summary>
    /// Hands out numbering definitions so that separate lists number separately.
    /// </summary>
    /// <remarks>
    /// Word restarts a numbered list when it meets a new numbering definition, not when it meets a
    /// new list in the markup. Reusing one definition for every <c>ol</c> on the page makes the
    /// second list continue "4, 5, 6" where the first left off.
    /// </remarks>
    private sealed class ListNumbering(WordDocument document)
    {
        private int? _bulleted;
        private int? _numbered;

        /// <summary>Ends the current run of list items, so the next list starts fresh.</summary>
        public void Break()
        {
            _bulleted = null;
            _numbered = null;
        }

        public int IdFor(bool numbered)
        {
            if (numbered)
            {
                return _numbered ??= document.Numbering.AddNumberedList();
            }

            return _bulleted ??= document.Numbering.AddBulletList();
        }
    }
}

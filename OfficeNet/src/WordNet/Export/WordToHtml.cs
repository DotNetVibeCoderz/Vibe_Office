// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Text;
using System.Xml.Linq;
using OfficeNet.Core.Drawing;
using OfficeNet.Core.Html;
using OfficeNet.Core.Xml;
using WordNet.Tables;

namespace WordNet.Export;

/// <summary>How a document is written out as HTML.</summary>
public sealed class WordHtmlOptions
{
    /// <summary>A complete page, or only the body's content.</summary>
    public bool FullDocument { get; set; } = true;

    /// <summary>The page title. Defaults to the first heading.</summary>
    public string? Title { get; set; }

    /// <summary>Whether pictures are embedded as <c>data:</c> URIs rather than replaced by alt text.</summary>
    public bool EmbedImages { get; set; } = true;

    /// <summary>
    /// The stylesheet written into the head: <c>null</c> for a small default, empty for none.
    /// </summary>
    public string? Stylesheet { get; set; }

    internal HtmlWriteOptions ForWriting() => new()
    {
        FullDocument = FullDocument,
        Title = Title,
        EmbedImages = EmbedImages,
        Stylesheet = Stylesheet,
    };
}

/// <summary>
/// Converts a document into HTML.
/// </summary>
/// <remarks>
/// <para>
/// The mirror image of <see cref="Import.HtmlToWord"/>, and it goes through the same model: the
/// document is mapped onto <see cref="HtmlBlock"/>s and <see cref="HtmlWriter"/> writes them. A
/// page taken into Word and back out again therefore passes through one description of what a
/// heading, a list and a link are, not two that happen to agree.
/// </para>
/// <para>
/// It is structural, like the importer. Headings become <c>h1</c>–<c>h6</c> from their styles,
/// lists become <c>ul</c> and <c>ol</c> from their numbering definitions, and a numbered list
/// interrupted by a paragraph resumes at the right number rather than restarting at one. Inline
/// formatting is the run's own — direct bold, italic, colour, font and size. Formatting that comes
/// only from a character or paragraph style other than a heading is not resolved, so text that is
/// red because its style says so comes out in the page's default colour.
/// </para>
/// <para>
/// Table cells come out as their text: the shared block model carries a cell as a string, so
/// formatting inside a cell, merged cells and nested tables are flattened. A picture inside a
/// paragraph is written after that paragraph's text rather than in the middle of it.
/// </para>
/// </remarks>
public static class WordToHtml
{
    /// <summary>Converts a document to an HTML string.</summary>
    public static string Convert(WordDocument document, WordHtmlOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        options ??= new WordHtmlOptions();

        // Pictures are always collected; whether each becomes a data URI or its alt text is the
        // writer's decision, and dropping them here would lose the alt text too.
        return HtmlWriter.Write(ToBlocks(document, includeImages: true), options.ForWriting());
    }

    /// <summary>Converts a .docx file to an .html file.</summary>
    public static void Convert(string docxPath, string htmlPath, WordHtmlOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(docxPath);
        ArgumentNullException.ThrowIfNull(htmlPath);

        using var document = WordDocument.Open(docxPath);
        File.WriteAllText(htmlPath, Convert(document, options), new UTF8Encoding(false));
    }

    /// <summary>Maps the document onto the shared block model.</summary>
    internal static List<HtmlBlock> ToBlocks(WordDocument document, bool includeImages)
    {
        var blocks = new List<HtmlBlock>();
        var numbering = new ListCounters();

        foreach (var block in document.Blocks)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    AddParagraph(document, paragraph, blocks, numbering, includeImages);
                    break;

                case Table table:
                    numbering.EndRun();
                    blocks.Add(ReadTable(table));
                    break;
            }
        }

        return blocks;
    }

    private static void AddParagraph(WordDocument document, Paragraph paragraph,
        List<HtmlBlock> blocks, ListCounters numbering, bool includeImages)
    {
        var spans = new List<HtmlSpan>();
        var images = new List<HtmlBlock>();

        foreach (var child in paragraph.Element.Elements())
        {
            if (child.Name == Ns.W + "r")
            {
                AddRun(document, child, null, spans, images, includeImages);
            }
            else if (child.Name == Ns.W + "hyperlink")
            {
                var href = HrefOf(document, child);

                foreach (var run in child.Elements(Ns.W + "r"))
                {
                    AddRun(document, run, href, spans, images, includeImages);
                }
            }
        }

        var style = new CssStyle { Alignment = AlignmentOf(paragraph) };
        var hasText = spans.Any(s => s.Text.Trim().Length > 0);
        var level = HeadingLevel(paragraph.StyleId);

        if (level > 0 && hasText)
        {
            numbering.EndRun();

            blocks.Add(new HtmlBlock
            {
                Kind = HtmlBlockKind.Heading,
                HeadingLevel = level,
                Spans = spans,
                Style = style,
            });
        }
        else if (ListOf(paragraph) is { } list)
        {
            var bulleted = document.Numbering.IsBulleted(list.NumberingId, list.Level);

            blocks.Add(new HtmlBlock
            {
                Kind = HtmlBlockKind.ListItem,
                ListLevel = list.Level,
                Numbered = !bulleted,
                ListId = numbering.RunFor(list.NumberingId, list.Level),
                ListNumber = numbering.Next(document, list.NumberingId, list.Level),
                Spans = spans,
                Style = style,
            });
        }
        else if (hasText)
        {
            numbering.EndRun();
            blocks.Add(new HtmlBlock { Kind = HtmlBlockKind.Paragraph, Spans = spans, Style = style });
        }
        else if (images.Count == 0 && IsRule(paragraph))
        {
            numbering.EndRun();
            blocks.Add(new HtmlBlock { Kind = HtmlBlockKind.Rule });
        }

        // An empty paragraph adds no block, so it does not end a list; a picture does.
        if (images.Count > 0)
        {
            numbering.EndRun();
            blocks.AddRange(images);
        }
    }

    private static void AddRun(WordDocument document, XElement element, string? href,
        List<HtmlSpan> spans, List<HtmlBlock> images, bool includeImages)
    {
        var run = new Run(document, element);

        if (run.Format.Hidden == true)
        {
            return;
        }

        if (includeImages)
        {
            foreach (var drawing in element.Elements(Ns.W + "drawing"))
            {
                if (ImageOf(document, drawing) is { } image)
                {
                    images.Add(image);
                }
            }
        }

        var text = new StringBuilder();
        Run.AppendText(element, text);

        if (text.Length > 0)
        {
            spans.Add(new HtmlSpan(text.ToString(), StyleOf(run.Format), href));
        }
    }

    private static CssStyle StyleOf(RunFormat format) => new()
    {
        Bold = format.Bold,
        Italic = format.Italic,
        Underline = format.Underline is { } underline ? underline != UnderlineStyle.None : null,
        Strike = format.Strike == true || format.DoubleStrike == true ? true : format.Strike,
        Color = format.Color is { IsAutomatic: false } color ? color : null,
        BackgroundColor = HighlightColor(format.Highlight) ?? format.Shading,
        FontFamily = format.FontName,
        FontSize = format.FontSize,
        Position = format.VerticalAlignment switch
        {
            VerticalAlignment.Superscript => TextPosition.Superscript,
            VerticalAlignment.Subscript => TextPosition.Subscript,
            _ => null,
        },
    };

    /// <summary>The link a <c>w:hyperlink</c> points at: an external target or an anchor.</summary>
    private static string? HrefOf(WordDocument document, XElement hyperlink)
    {
        var id = hyperlink.Attribute(Ns.R + "id")?.Value;

        if (id is not null && document.DocumentPart.RelationshipById(id) is { } relationship)
        {
            return relationship.Target;
        }

        var anchor = hyperlink.Attribute(Ns.W + "anchor")?.Value;

        return anchor is { Length: > 0 } ? "#" + anchor : null;
    }

    private static HtmlBlock? ImageOf(WordDocument document, XElement drawing)
    {
        var embed = drawing.Descendants(Ns.A + "blip").FirstOrDefault()?.Attribute(Ns.R + "embed")?.Value;

        if (embed is null || document.DocumentPart.RelatedPart(embed) is not { } part)
        {
            return null;
        }

        var properties = drawing.Descendants(Ns.Wp + "docPr").FirstOrDefault();
        var alt = properties?.Attribute("descr")?.Value is { Length: > 0 } description
            ? description
            : properties?.Attribute("title")?.Value;

        return new HtmlBlock { Kind = HtmlBlockKind.Image, Image = part.GetBytes(), AltText = alt };
    }

    private static HtmlBlock ReadTable(Table table)
    {
        var rows = new List<List<string>>();

        foreach (var row in table.Rows)
        {
            rows.Add([.. row.Cells.Select(cell => cell.Text.TrimEnd())]);
        }

        return new HtmlBlock
        {
            Kind = HtmlBlockKind.Table,
            TableRows = rows,
            TableHasHeader = table.Rows.Count > 1 && table.Rows[0].IsHeader,
        };
    }

    /// <summary>
    /// The heading level a style id implies, or zero for body text.
    /// </summary>
    /// <remarks>
    /// Word's built-in ids, which are what this library and Word itself write. HTML stops at six,
    /// so Word's levels seven to nine become <c>h6</c> rather than being demoted to paragraphs.
    /// </remarks>
    private static int HeadingLevel(string? styleId)
    {
        if (styleId is null)
        {
            return 0;
        }

        if (styleId.Equals("Title", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return styleId.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) &&
               int.TryParse(styleId.AsSpan(7), out var level) && level >= 1
            ? Math.Min(level, 6)
            : 0;
    }

    private static TextAlign? AlignmentOf(Paragraph paragraph) => paragraph.Alignment switch
    {
        ParagraphAlignment.Center => TextAlign.Center,
        ParagraphAlignment.Right => TextAlign.Right,
        ParagraphAlignment.Justify or ParagraphAlignment.Distribute => TextAlign.Justify,
        _ => null,
    };

    private static (int NumberingId, int Level)? ListOf(Paragraph paragraph)
    {
        var numbering = paragraph.Element.Element(Ns.W + "pPr")?.Element(Ns.W + "numPr");
        var id = (int?)numbering?.Element(Ns.W + "numId")?.Attribute(Ns.W + "val");

        // numId 0 is Word's way of saying "no numbering here", used to switch a list style off.
        return id is > 0 ? (id.Value, Math.Clamp(paragraph.ListLevel ?? 0, 0, 8)) : null;
    }

    /// <summary>An empty paragraph whose only content is a bottom border is Word's horizontal rule.</summary>
    private static bool IsRule(Paragraph paragraph)
    {
        var borders = paragraph.Element.Element(Ns.W + "pPr")?.Element(Ns.W + "pBdr");

        return borders?.Element(Ns.W + "bottom") is not null && borders.Element(Ns.W + "top") is null;
    }

    /// <summary>The CSS colour for one of Word's named highlight colours.</summary>
    private static OfficeColor? HighlightColor(string? name) => name switch
    {
        "yellow" => OfficeColor.FromRgb(0xFF, 0xFF, 0x00),
        "green" => OfficeColor.FromRgb(0x00, 0xFF, 0x00),
        "cyan" => OfficeColor.FromRgb(0x00, 0xFF, 0xFF),
        "magenta" => OfficeColor.FromRgb(0xFF, 0x00, 0xFF),
        "blue" => OfficeColor.FromRgb(0x00, 0x00, 0xFF),
        "red" => OfficeColor.FromRgb(0xFF, 0x00, 0x00),
        "darkBlue" => OfficeColor.FromRgb(0x00, 0x00, 0x8B),
        "darkCyan" => OfficeColor.FromRgb(0x00, 0x8B, 0x8B),
        "darkGreen" => OfficeColor.FromRgb(0x00, 0x64, 0x00),
        "darkMagenta" => OfficeColor.FromRgb(0x8B, 0x00, 0x8B),
        "darkRed" => OfficeColor.FromRgb(0x8B, 0x00, 0x00),
        "darkYellow" => OfficeColor.FromRgb(0x80, 0x80, 0x00),
        "darkGray" => OfficeColor.FromRgb(0xA9, 0xA9, 0xA9),
        "lightGray" => OfficeColor.FromRgb(0xD3, 0xD3, 0xD3),
        "black" => OfficeColor.FromRgb(0x00, 0x00, 0x00),
        "white" => OfficeColor.FromRgb(0xFF, 0xFF, 0xFF),
        _ => null,
    };

    /// <summary>
    /// Counts list items so an interrupted numbered list resumes at the right number.
    /// </summary>
    /// <remarks>
    /// Word numbers a list by its definition, not by where it sits, so "1, 2, a paragraph, 3" is one
    /// list. HTML would make that two lists and start the second at one again; the count is carried
    /// out so the writer can say <c>start="3"</c>. Deeper levels restart whenever a shallower item
    /// appears, which is what a nested outline does.
    /// </remarks>
    private sealed class ListCounters
    {
        private readonly Dictionary<int, int[]> _counts = [];
        private int _runs;
        private int _run;
        private int _runRoot;

        /// <summary>
        /// Which list an item belongs to, for the writer's purposes.
        /// </summary>
        /// <remarks>
        /// Not simply the numbering id. A nested list of another kind — bullets under a numbered
        /// item — often has a numbering definition of its own, and keying the list by id closes the
        /// outer list and starts a new one instead of nesting inside the item. Only an item at the
        /// outermost level with a different definition starts a new list.
        /// </remarks>
        public int RunFor(int numberingId, int level)
        {
            if (_run == 0 || (level == 0 && numberingId != _runRoot))
            {
                _run = ++_runs;
                _runRoot = numberingId;
            }

            return _run;
        }

        /// <summary>Ends the current list: something that is not a list item came between.</summary>
        public void EndRun()
        {
            _run = 0;
            _runRoot = 0;
        }

        public int Next(WordDocument document, int numberingId, int level)
        {
            if (!_counts.TryGetValue(numberingId, out var counts))
            {
                counts = new int[9];

                for (var i = 0; i < counts.Length; i++)
                {
                    counts[i] = document.Numbering.StartAt(numberingId, i) - 1;
                }

                _counts[numberingId] = counts;
            }

            counts[level]++;

            for (var deeper = level + 1; deeper < counts.Length; deeper++)
            {
                counts[deeper] = document.Numbering.StartAt(numberingId, deeper) - 1;
            }

            return counts[level];
        }
    }
}

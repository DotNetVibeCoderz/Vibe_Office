// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Xml.Linq;
using OfficeNet.Core;
using OfficeNet.Core.Drawing;
using OfficeNet.Core.Xml;
using PdfNet.Annotations;
using PdfNet.Content;
using PdfNet.Document;
using WordNet.Sections;
using WordNet.Tables;

namespace WordNet.Export;

/// <summary>Options for rendering a Word document to PDF.</summary>
public sealed class PdfExportOptions
{
    /// <summary>Renders the section's headers and footers on every page.</summary>
    public bool IncludeHeadersAndFooters { get; set; } = true;

    /// <summary>
    /// Replaces <c>PAGE</c> and <c>NUMPAGES</c> field results with the real numbers.
    /// </summary>
    /// <remarks>
    /// The document itself carries whatever placeholder Word last cached, which for a
    /// freshly-generated file is usually "1" on every page. Evaluating them here is what makes an
    /// exported PDF's page numbers correct.
    /// </remarks>
    public bool EvaluatePageFields { get; set; } = true;

    /// <summary>Draws images. Turning this off produces a text-only PDF much faster.</summary>
    public bool IncludeImages { get; set; } = true;

    /// <summary>A watermark stamped diagonally across every page.</summary>
    public string? Watermark { get; set; }

    /// <summary>The watermark's opacity.</summary>
    public double WatermarkOpacity { get; set; } = 0.12;

    /// <summary>The default font family when a run does not name one.</summary>
    public string DefaultFontFamily { get; set; } = "Calibri";

    /// <summary>The default font size when neither the run nor its style gives one.</summary>
    public double DefaultFontSizePoints { get; set; } = 11;
}

/// <summary>
/// Lays a Word document out onto PDF pages.
/// </summary>
/// <remarks>
/// <para>
/// This is a real flow layout, not a screenshot: it resolves each run's effective formatting
/// through the style chain, measures text with the target font's own metrics, wraps and justifies
/// paragraphs, breaks pages when the cursor runs past the bottom margin, and repeats table header
/// rows. What it does not do is everything Word's engine does — floating objects, footnotes,
/// hyphenation, kerning pairs and text boxes are out of scope, and a document leaning on those
/// will differ from Word's own PDF export.
/// </para>
/// <para>
/// Fonts are mapped to the standard 14 rather than embedded. That keeps the output free of font
/// licensing questions and small, at the cost of exact glyph shapes; the metrics are close enough
/// that line breaks land in the same places for ordinary Latin text.
/// </para>
/// </remarks>
public static class WordToPdf
{
    /// <summary>Renders a document to a new PDF.</summary>
    public static PdfDocument Convert(WordDocument document, PdfExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        options ??= new PdfExportOptions();

        var pdf = PdfDocument.Create();
        pdf.Info.Title = document.Properties.Title;
        pdf.Info.Author = document.Properties.Creator;
        pdf.Info.Subject = document.Properties.Subject;
        pdf.Info.Keywords = document.Properties.Keywords;
        pdf.Info.Creator = "OfficeNet WordNet by Gravicode Studios";

        var section = document.Section;
        var layout = new LayoutEngine(document, pdf, section, options);

        layout.Run();
        layout.Finish();

        return pdf;
    }

    private sealed class LayoutEngine
    {
        private readonly WordDocument _document;
        private readonly PdfDocument _pdf;
        private readonly Section _section;
        private readonly PdfExportOptions _options;
        private readonly PdfRectangle _pageBox;
        private readonly double _left;
        private readonly double _right;
        private readonly double _top;
        private readonly double _bottom;

        private PdfPage? _page;
        private PdfCanvas? _canvas;
        private double _cursor;
        private readonly List<PdfPage> _pages = [];
        private readonly List<(PdfPage Page, int Number)> _pageNumbers = [];

        public LayoutEngine(WordDocument document, PdfDocument pdf, Section section,
            PdfExportOptions options)
        {
            _document = document;
            _pdf = pdf;
            _section = section;
            _options = options;

            _pageBox = PageSize.Points(section.PageWidth.Points, section.PageHeight.Points);
            _left = section.LeftMargin.Points;
            _right = section.PageWidth.Points - section.RightMargin.Points;
            _top = section.TopMargin.Points;
            _bottom = section.PageHeight.Points - section.BottomMargin.Points;
        }

        private double ContentWidth => _right - _left;

        public void Run()
        {
            NewPage();

            foreach (var block in _document.Blocks)
            {
                switch (block)
                {
                    case Paragraph paragraph:
                        DrawParagraph(paragraph, _left, ContentWidth);
                        break;

                    case Table table:
                        DrawTable(table);
                        break;
                }
            }
        }

        public void Finish()
        {
            _canvas?.Dispose();
            _canvas = null;

            if (_options.IncludeHeadersAndFooters)
            {
                DrawHeadersAndFooters();
            }

            if (_options.Watermark is { Length: > 0 } watermark)
            {
                foreach (var page in _pages)
                {
                    page.AddWatermark(watermark, OfficeColor.Gray, _options.WatermarkOpacity);
                }
            }
        }

        private void NewPage()
        {
            _canvas?.Dispose();

            _page = _pdf.Pages.Add(_pageBox);
            _pages.Add(_page);
            _pageNumbers.Add((_page, _pages.Count));

            _canvas = _page.OpenCanvas();
            _canvas.TopDown = true;
            _cursor = _top;
        }

        private void EnsureSpace(double height)
        {
            if (_cursor + height > _bottom && _cursor > _top)
            {
                NewPage();
            }
        }

        // ---- Formatting resolution -------------------------------------------------------------

        private readonly record struct Effective(
            string FontFamily,
            double SizePoints,
            bool Bold,
            bool Italic,
            bool Underline,
            bool Strike,
            OfficeColor Color,
            OfficeColor? Highlight);

        /// <summary>
        /// Resolves a run's effective formatting through its own properties, its character style,
        /// its paragraph's style, and that style's ancestors.
        /// </summary>
        /// <remarks>
        /// Style inheritance is where a naive converter loses everything: a Heading 1 paragraph
        /// carries no direct formatting at all, so a run that reads only its own <c>w:rPr</c>
        /// renders body text where the heading should be.
        /// </remarks>
        private Effective Resolve(Run? run, Paragraph paragraph)
        {
            var family = _options.DefaultFontFamily;
            var size = _options.DefaultFontSizePoints;
            var bold = false;
            var italic = false;
            var underline = false;
            var strike = false;
            var color = OfficeColor.Black;
            OfficeColor? highlight = null;

            // Walk the style chain from the root down so that nearer definitions overwrite.
            foreach (var style in StyleChain(paragraph.StyleId).Reverse())
            {
                Apply(style.RunFormat);
            }

            if (run is not null)
            {
                foreach (var style in StyleChain(run.Format.StyleId).Reverse())
                {
                    Apply(style.RunFormat);
                }

                Apply(run.Format);
            }

            return new Effective(family, size, bold, italic, underline, strike, color, highlight);

            void Apply(RunFormat format)
            {
                if (format.FontName is { } name)
                {
                    family = name;
                }

                if (format.FontSize is { } fontSize)
                {
                    size = fontSize.Points;
                }

                if (format.Bold is { } b)
                {
                    bold = b;
                }

                if (format.Italic is { } i)
                {
                    italic = i;
                }

                if (format.Underline is { } u)
                {
                    underline = u != UnderlineStyle.None;
                }

                if (format.Strike is { } s)
                {
                    strike = s;
                }

                if (format.Color is { } c && !c.IsAutomatic)
                {
                    color = c;
                }

                if (format.Highlight is { } h && OfficeColor.TryParse(h, out var parsed))
                {
                    highlight = parsed;
                }
            }
        }

        private IEnumerable<Styles.Style> StyleChain(string? styleId)
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var current = styleId;

            // A cyclic basedOn chain is malformed but does occur in documents produced by
            // conversion tools, and following it forever hangs the export.
            while (current is not null && visited.Add(current))
            {
                var style = _document.Styles[current];

                if (style is null)
                {
                    yield break;
                }

                yield return style;
                current = style.BasedOn;
            }
        }

        private (double Before, double After, double LineHeight, ParagraphAlignment Alignment,
            double LeftIndent, double RightIndent, double FirstLineIndent)
            ResolveParagraph(Paragraph paragraph, double fontSize)
        {
            double before = 0, after = 8, lineFactor = 1.15;
            var alignment = ParagraphAlignment.Left;
            double leftIndent = 0, rightIndent = 0, firstLineIndent = 0;

            foreach (var style in StyleChain(paragraph.StyleId).Reverse())
            {
                ApplyParagraph(style.ParagraphFormat);
            }

            ApplyParagraph(paragraph.Format);

            return (before, after, fontSize * lineFactor, alignment, leftIndent, rightIndent, firstLineIndent);

            void ApplyParagraph(ParagraphFormat format)
            {
                if (format.SpaceBefore is { } b)
                {
                    before = b.Points;
                }

                if (format.SpaceAfter is { } a)
                {
                    after = a.Points;
                }

                if (format.LineSpacing is { } line && format.LineSpacingRule != LineSpacingRule.Exact)
                {
                    lineFactor = line;
                }

                if (format.Alignment is { } jc)
                {
                    alignment = jc;
                }

                if (format.LeftIndent is { } li)
                {
                    leftIndent = li.Points;
                }

                if (format.RightIndent is { } ri)
                {
                    rightIndent = ri.Points;
                }

                if (format.FirstLineIndent is { } fi)
                {
                    firstLineIndent = fi.Points;
                }
            }
        }

        // ---- Paragraphs --------------------------------------------------------------------------

        private void DrawParagraph(Paragraph paragraph, double x, double width)
        {
            var runs = paragraph.Runs;

            // A page break in its own paragraph is a common shape and must not also emit an empty
            // line at the top of the new page.
            if (HasPageBreak(paragraph))
            {
                if (_cursor > _top)
                {
                    NewPage();
                }

                if (runs.All(r => r.Text.Trim().Length == 0 && !r.HasDrawing))
                {
                    return;
                }
            }

            if (paragraph.Format.PageBreakBefore == true && _cursor > _top)
            {
                NewPage();
            }

            var baseFormat = Resolve(runs.FirstOrDefault(), paragraph);
            var (before, after, lineHeight, alignment, leftIndent, rightIndent, firstLineIndent) =
                ResolveParagraph(paragraph, baseFormat.SizePoints);

            _cursor += before;

            var contentLeft = x + leftIndent;
            var contentWidth = Math.Max(20, width - leftIndent - rightIndent);

            if (_options.IncludeImages)
            {
                foreach (var run in runs.Where(r => r.HasDrawing))
                {
                    DrawInlineImage(run, contentLeft, contentWidth, alignment);
                }
            }

            var bulletText = BulletFor(paragraph);
            var segments = BuildSegments(runs, paragraph);

            if (segments.Count == 0)
            {
                if (bulletText is null)
                {
                    _cursor += lineHeight + after;
                    return;
                }

                segments.Add(new Segment(string.Empty, baseFormat));
            }

            var lines = WrapSegments(segments, contentWidth, firstLineIndent,
                bulletText is null ? 0 : 18);

            for (var i = 0; i < lines.Count; i++)
            {
                EnsureSpace(lineHeight);

                var line = lines[i];
                var indent = i == 0 ? Math.Max(0, firstLineIndent) : 0;
                var lineLeft = contentLeft + indent + (bulletText is not null ? 18 : 0);
                var lineWidth = contentWidth - indent - (bulletText is not null ? 18 : 0);

                if (i == 0 && bulletText is not null)
                {
                    var canvas = _canvas!;
                    canvas.SetFont(StandardFonts.Match(baseFormat.FontFamily, false, false),
                        baseFormat.SizePoints);
                    canvas.SetFillColor(baseFormat.Color);
                    canvas.DrawText(bulletText, contentLeft, _cursor + baseFormat.SizePoints * 0.85);
                }

                // The last line of a justified paragraph is set flush left, as typesetting requires.
                var effectiveAlignment = alignment == ParagraphAlignment.Justify && i == lines.Count - 1
                    ? ParagraphAlignment.Left
                    : alignment;

                DrawLine(line, lineLeft, lineWidth, lineHeight, effectiveAlignment);
                _cursor += lineHeight;
            }

            _cursor += after;
        }

        private static bool HasPageBreak(Paragraph paragraph) =>
            paragraph.Element.Descendants(Ns.W + "br")
                .Any(e => e.Attr(Ns.W + "type") == "page");

        private string? BulletFor(Paragraph paragraph)
        {
            var level = paragraph.ListLevel;

            if (level is null)
            {
                return null;
            }

            // Rendering the real numbering would mean running Word's counter machinery across the
            // whole document. A bullet glyph per level is honest about that limit and keeps list
            // structure visible.
            return (level.Value % 3) switch
            {
                0 => "•",
                1 => "◦",
                _ => "▪",
            };
        }

        private readonly record struct Segment(string Text, Effective Format);

        private List<Segment> BuildSegments(IReadOnlyList<Run> runs, Paragraph paragraph)
        {
            var result = new List<Segment>();

            foreach (var run in runs)
            {
                if (run.Format.Hidden == true)
                {
                    continue;
                }

                var text = run.Text;

                if (text.Length == 0)
                {
                    continue;
                }

                // A field's instruction text is markup, not content; only the cached result between
                // "separate" and "end" is shown.
                if (IsInstructionRun(run))
                {
                    continue;
                }

                result.Add(new Segment(text.Replace('\n', ' ').Replace('\t', ' '), Resolve(run, paragraph)));
            }

            return result;
        }

        private static bool IsInstructionRun(Run run) =>
            run.Element.Element(Ns.W + "instrText") is not null ||
            run.Element.Element(Ns.W + "fldChar") is not null;

        private readonly record struct LinePiece(string Text, Effective Format);

        private List<List<LinePiece>> WrapSegments(List<Segment> segments, double width,
            double firstLineIndent, double bulletIndent)
        {
            var lines = new List<List<LinePiece>>();
            var current = new List<LinePiece>();
            double used = 0;

            var available = width - Math.Max(0, firstLineIndent) - bulletIndent;

            foreach (var segment in segments)
            {
                var font = StandardFonts.Match(segment.Format.FontFamily, segment.Format.Bold,
                    segment.Format.Italic);

                // Splitting on spaces but keeping them attached to the preceding word is what makes
                // the measured width match what is drawn.
                foreach (var word in SplitWords(segment.Text))
                {
                    var wordWidth = StandardFonts.MeasurePoints(font, word, segment.Format.SizePoints);

                    if (used + wordWidth > available && current.Count > 0)
                    {
                        lines.Add(current);
                        current = [];
                        used = 0;
                        available = width - bulletIndent;

                        // A wrapped line never starts with a space.
                        var trimmed = word.TrimStart();
                        if (trimmed.Length == 0)
                        {
                            continue;
                        }

                        wordWidth = StandardFonts.MeasurePoints(font, trimmed, segment.Format.SizePoints);
                        current.Add(new LinePiece(trimmed, segment.Format));
                        used += wordWidth;
                        continue;
                    }

                    current.Add(new LinePiece(word, segment.Format));
                    used += wordWidth;
                }
            }

            if (current.Count > 0)
            {
                lines.Add(current);
            }

            return lines;
        }

        private static IEnumerable<string> SplitWords(string text)
        {
            var start = 0;

            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] != ' ')
                {
                    continue;
                }

                // Include the space so widths add up and a run boundary mid-space is harmless.
                yield return text[start..(i + 1)];
                start = i + 1;
            }

            if (start < text.Length)
            {
                yield return text[start..];
            }
        }

        private void DrawLine(List<LinePiece> pieces, double x, double width, double lineHeight,
            ParagraphAlignment alignment)
        {
            var canvas = _canvas!;

            var totalWidth = pieces.Sum(p => StandardFonts.MeasurePoints(
                StandardFonts.Match(p.Format.FontFamily, p.Format.Bold, p.Format.Italic),
                p.Text, p.Format.SizePoints));

            var cursor = alignment switch
            {
                ParagraphAlignment.Center => x + Math.Max(0, (width - totalWidth) / 2),
                ParagraphAlignment.Right => x + Math.Max(0, width - totalWidth),
                _ => x,
            };

            // Justification stretches the spaces between words, which means distributing the slack
            // over the gaps rather than scaling the glyphs.
            var extraPerSpace = 0.0;
            if (alignment is ParagraphAlignment.Justify or ParagraphAlignment.Distribute)
            {
                var spaces = pieces.Sum(p => p.Text.Count(c => c == ' '));
                if (spaces > 0 && totalWidth < width)
                {
                    extraPerSpace = (width - totalWidth) / spaces;
                }
            }

            var maxSize = pieces.Max(p => p.Format.SizePoints);
            var baseline = _cursor + lineHeight - (lineHeight - maxSize) / 2 - maxSize * 0.22;

            foreach (var piece in pieces)
            {
                var font = StandardFonts.Match(piece.Format.FontFamily, piece.Format.Bold,
                    piece.Format.Italic);

                canvas.SetFont(font, piece.Format.SizePoints);

                var pieceWidth = StandardFonts.MeasurePoints(font, piece.Text, piece.Format.SizePoints)
                                 + extraPerSpace * piece.Text.Count(c => c == ' ');

                if (piece.Format.Highlight is { } highlight)
                {
                    canvas.SetFillColor(highlight);
                    canvas.Rectangle(cursor, baseline - piece.Format.SizePoints * 0.8,
                        pieceWidth, piece.Format.SizePoints * 1.05).Fill();
                }

                canvas.SetFillColor(piece.Format.Color);

                if (extraPerSpace > 0)
                {
                    DrawWithExtraSpacing(canvas, piece, font, cursor, baseline, extraPerSpace);
                }
                else
                {
                    canvas.DrawText(piece.Text, cursor, baseline);
                }

                if (piece.Format.Underline)
                {
                    canvas.SetStrokeColor(piece.Format.Color);
                    canvas.SetLineWidth(Math.Max(0.5, piece.Format.SizePoints * 0.055));
                    canvas.DrawLine(cursor, baseline + piece.Format.SizePoints * 0.14,
                        cursor + pieceWidth, baseline + piece.Format.SizePoints * 0.14);
                }

                if (piece.Format.Strike)
                {
                    canvas.SetStrokeColor(piece.Format.Color);
                    canvas.SetLineWidth(Math.Max(0.5, piece.Format.SizePoints * 0.055));
                    canvas.DrawLine(cursor, baseline - piece.Format.SizePoints * 0.28,
                        cursor + pieceWidth, baseline - piece.Format.SizePoints * 0.28);
                }

                cursor += pieceWidth;
            }
        }

        private static void DrawWithExtraSpacing(PdfCanvas canvas, LinePiece piece, StandardFont font,
            double x, double baseline, double extraPerSpace)
        {
            var cursor = x;

            foreach (var word in piece.Text.Split(' '))
            {
                if (word.Length > 0)
                {
                    canvas.DrawText(word, cursor, baseline);
                    cursor += StandardFonts.MeasurePoints(font, word, piece.Format.SizePoints);
                }

                cursor += StandardFonts.MeasurePoints(font, " ", piece.Format.SizePoints) + extraPerSpace;
            }
        }

        private void DrawInlineImage(Run run, double x, double width, ParagraphAlignment alignment)
        {
            var blip = run.Element.Descendants(Ns.A + "blip").FirstOrDefault();
            var embedId = blip?.Attr(Ns.R + "embed");

            if (embedId is null)
            {
                return;
            }

            var part = _document.DocumentPart.RelatedPart(embedId);
            if (part is null)
            {
                return;
            }

            var extent = run.Element.Descendants(Ns.Wp + "extent").FirstOrDefault();
            var imageWidth = Length.FromEmu(extent.LongAttr("cx")).Points;
            var imageHeight = Length.FromEmu(extent.LongAttr("cy")).Points;

            if (imageWidth <= 0 || imageHeight <= 0)
            {
                return;
            }

            // An image wider than the text column is scaled down rather than clipped, which is what
            // Word does when a picture is pasted at full resolution.
            if (imageWidth > width)
            {
                imageHeight *= width / imageWidth;
                imageWidth = width;
            }

            EnsureSpace(imageHeight);

            var offset = alignment switch
            {
                ParagraphAlignment.Center => (width - imageWidth) / 2,
                ParagraphAlignment.Right => width - imageWidth,
                _ => 0,
            };

            try
            {
                _canvas!.DrawImage(part.GetBytes(), x + Math.Max(0, offset), _cursor,
                    imageWidth, imageHeight);
            }
            catch (OfficeNetException)
            {
                // A format PdfNet cannot embed (GIF, TIFF, a metafile) leaves a gap rather than
                // aborting the whole export.
            }

            _cursor += imageHeight + 6;
        }

        // ---- Tables ------------------------------------------------------------------------------

        private void DrawTable(Table table)
        {
            var columnCount = Math.Max(1, table.ColumnCount);
            var widths = ResolveColumnWidths(table, columnCount);

            var headerRows = table.Rows.TakeWhile(r => r.IsHeader).ToList();

            foreach (var row in table.Rows)
            {
                var rowHeight = MeasureRow(row, widths);

                if (_cursor + rowHeight > _bottom && _cursor > _top)
                {
                    NewPage();

                    // Repeating the header on the continuation page is what makes a table that
                    // spans pages readable, and it is what w:tblHeader asks for.
                    foreach (var header in headerRows)
                    {
                        DrawRow(header, widths, MeasureRow(header, widths));
                    }
                }

                DrawRow(row, widths, rowHeight);
            }

            _cursor += 6;
        }

        private double[] ResolveColumnWidths(Table table, int columnCount)
        {
            var grid = table.Element.Element(Ns.W + "tblGrid")?.Elements(Ns.W + "gridCol").ToList();
            var widths = new double[columnCount];
            double declared = 0;

            for (var i = 0; i < columnCount; i++)
            {
                var raw = grid is not null && i < grid.Count ? grid[i].Attr(Ns.W + "w") : null;

                if (raw is not null && double.TryParse(raw, out var twips) && twips > 0)
                {
                    widths[i] = Length.FromTwips(twips).Points;
                    declared += widths[i];
                }
            }

            // A grid with no widths, or one that does not fill the column, is scaled to the text
            // width — which is what the autofit layout does and what the document looks like.
            if (declared <= 0)
            {
                Array.Fill(widths, ContentWidth / columnCount);
                return widths;
            }

            var scale = ContentWidth / declared;
            for (var i = 0; i < columnCount; i++)
            {
                widths[i] = widths[i] > 0 ? widths[i] * scale : ContentWidth / columnCount;
            }

            return widths;
        }

        private double MeasureRow(TableRow row, double[] widths)
        {
            double tallest = 0;
            var cells = row.Cells;
            var column = 0;

            foreach (var cell in cells)
            {
                var span = Math.Max(1, cell.GridSpan);
                var cellWidth = 0.0;

                for (var i = 0; i < span && column + i < widths.Length; i++)
                {
                    cellWidth += widths[column + i];
                }

                double height = 4;

                foreach (var paragraph in cell.Paragraphs)
                {
                    var format = Resolve(paragraph.Runs.FirstOrDefault(), paragraph);
                    var (_, _, lineHeight, _, _, _, _) = ResolveParagraph(paragraph, format.SizePoints);

                    var segments = BuildSegments(paragraph.Runs, paragraph);
                    var lines = segments.Count == 0
                        ? 1
                        : WrapSegments(segments, Math.Max(10, cellWidth - 10), 0, 0).Count;

                    height += Math.Max(1, lines) * lineHeight;
                }

                tallest = Math.Max(tallest, height + 4);
                column += span;
            }

            return Math.Max(tallest, 16);
        }

        private void DrawRow(TableRow row, double[] widths, double rowHeight)
        {
            var canvas = _canvas!;
            var x = _left;
            var column = 0;

            foreach (var cell in row.Cells)
            {
                var span = Math.Max(1, cell.GridSpan);
                var cellWidth = 0.0;

                for (var i = 0; i < span && column + i < widths.Length; i++)
                {
                    cellWidth += widths[column + i];
                }

                if (cellWidth <= 0)
                {
                    cellWidth = widths.Length > 0 ? widths[0] : ContentWidth;
                }

                if (cell.Shading is { } shading)
                {
                    canvas.SetFillColor(shading);
                    canvas.Rectangle(x, _cursor, cellWidth, rowHeight).Fill();
                }

                canvas.SetStrokeColor(OfficeColor.FromRgb(0xBF, 0xBF, 0xBF));
                canvas.SetLineWidth(0.5);
                canvas.Rectangle(x, _cursor, cellWidth, rowHeight).Stroke();

                var savedCursor = _cursor;
                _cursor += 3;

                foreach (var paragraph in cell.Paragraphs)
                {
                    DrawParagraphInCell(paragraph, x + 5, cellWidth - 10);
                }

                _cursor = savedCursor;
                x += cellWidth;
                column += span;
            }

            _cursor += rowHeight;
        }

        private void DrawParagraphInCell(Paragraph paragraph, double x, double width)
        {
            var runs = paragraph.Runs;
            var baseFormat = Resolve(runs.FirstOrDefault(), paragraph);
            var (_, _, lineHeight, alignment, _, _, _) = ResolveParagraph(paragraph, baseFormat.SizePoints);

            var segments = BuildSegments(runs, paragraph);

            if (segments.Count == 0)
            {
                _cursor += lineHeight;
                return;
            }

            // A cell never paginates on its own: the row was measured to fit, so the lines are
            // drawn without a page-break check that would split a cell across pages.
            foreach (var line in WrapSegments(segments, Math.Max(10, width), 0, 0))
            {
                DrawLine(line, x, width, lineHeight,
                    alignment == ParagraphAlignment.Justify ? ParagraphAlignment.Left : alignment);
                _cursor += lineHeight;
            }
        }

        // ---- Headers and footers -----------------------------------------------------------------

        private void DrawHeadersAndFooters()
        {
            var headerContent = ReadHeaderFooter(isHeader: true);
            var footerContent = ReadHeaderFooter(isHeader: false);

            if (headerContent.Count == 0 && footerContent.Count == 0)
            {
                return;
            }

            var total = _pages.Count;

            foreach (var (page, number) in _pageNumbers)
            {
                using var canvas = page.OpenCanvas();
                canvas.TopDown = true;

                var y = _section.HeaderDistance.Points;
                foreach (var (text, alignment, format) in headerContent)
                {
                    DrawHeaderLine(canvas, Substitute(text, number, total), alignment, format, y);
                    y += format.SizePoints * 1.3;
                }

                y = _section.PageHeight.Points - _section.FooterDistance.Points
                    - footerContent.Count * 14;

                foreach (var (text, alignment, format) in footerContent)
                {
                    DrawHeaderLine(canvas, Substitute(text, number, total), alignment, format, y);
                    y += format.SizePoints * 1.3;
                }
            }
        }

        private string Substitute(string text, int pageNumber, int total)
        {
            if (!_options.EvaluatePageFields)
            {
                return text;
            }

            return text
                .Replace("{PAGE}", pageNumber.ToString())
                .Replace("{NUMPAGES}", total.ToString());
        }

        private List<(string Text, ParagraphAlignment Alignment, Effective Format)> ReadHeaderFooter(
            bool isHeader)
        {
            var result = new List<(string, ParagraphAlignment, Effective)>();

            var referenceName = isHeader ? Ns.W + "headerReference" : Ns.W + "footerReference";
            var reference = _section.Properties.Elements(referenceName)
                .FirstOrDefault(e => (e.Attr(Ns.W + "type") ?? "default") == "default");

            var id = reference?.Attr(Ns.R + "id");
            var part = id is null ? null : _document.DocumentPart.RelatedPart(id);
            var root = part?.Xml.Root;

            if (root is null)
            {
                return result;
            }

            foreach (var element in root.Elements(Ns.W + "p"))
            {
                var paragraph = new Paragraph(_document, element);
                var text = ExtractWithFields(paragraph);

                if (text.Trim().Length == 0)
                {
                    continue;
                }

                var format = Resolve(paragraph.Runs.FirstOrDefault(), paragraph);
                result.Add((text, paragraph.Alignment ?? ParagraphAlignment.Left, format));
            }

            return result;
        }

        /// <summary>
        /// Reads a header paragraph's text, turning PAGE and NUMPAGES fields into markers.
        /// </summary>
        /// <remarks>
        /// The field's own cached result is whatever Word last wrote — usually "1" — so it is
        /// replaced by a marker here and substituted per page later. Rendering the cached value
        /// puts "Page 1 of 1" on every page of a fifty-page document.
        /// </remarks>
        private static string ExtractWithFields(Paragraph paragraph)
        {
            var builder = new System.Text.StringBuilder();
            string? pendingInstruction = null;
            var inResult = false;

            foreach (var element in paragraph.Element.Elements())
            {
                if (element.Name == Ns.W + "hyperlink")
                {
                    foreach (var run in element.Elements(Ns.W + "r"))
                    {
                        builder.Append(RunText(run));
                    }

                    continue;
                }

                if (element.Name != Ns.W + "r")
                {
                    continue;
                }

                var fieldChar = element.Element(Ns.W + "fldChar")?.Attr(Ns.W + "fldCharType");

                if (fieldChar == "begin")
                {
                    pendingInstruction = string.Empty;
                    continue;
                }

                if (fieldChar == "separate")
                {
                    inResult = true;

                    var instruction = (pendingInstruction ?? string.Empty).Trim().ToUpperInvariant();

                    if (instruction.StartsWith("PAGE", StringComparison.Ordinal))
                    {
                        builder.Append("{PAGE}");
                    }
                    else if (instruction.StartsWith("NUMPAGES", StringComparison.Ordinal))
                    {
                        builder.Append("{NUMPAGES}");
                    }

                    continue;
                }

                if (fieldChar == "end")
                {
                    pendingInstruction = null;
                    inResult = false;
                    continue;
                }

                var instructionText = element.Element(Ns.W + "instrText")?.Value;

                if (instructionText is not null && pendingInstruction is not null)
                {
                    pendingInstruction += instructionText;
                    continue;
                }

                if (inResult)
                {
                    // The cached result was already replaced by a marker.
                    continue;
                }

                builder.Append(RunText(element));
            }

            return builder.ToString();

            static string RunText(XElement run)
            {
                var text = new System.Text.StringBuilder();

                foreach (var child in run.Elements())
                {
                    if (child.Name == Ns.W + "t")
                    {
                        text.Append(child.Value);
                    }
                    else if (child.Name == Ns.W + "tab")
                    {
                        text.Append("    ");
                    }
                }

                return text.ToString();
            }
        }

        private void DrawHeaderLine(PdfCanvas canvas, string text, ParagraphAlignment alignment,
            Effective format, double y)
        {
            if (text.Length == 0)
            {
                return;
            }

            var font = StandardFonts.Match(format.FontFamily, format.Bold, format.Italic);
            canvas.SetFont(font, format.SizePoints);
            canvas.SetFillColor(format.Color);

            var width = ContentWidth;
            var textWidth = StandardFonts.MeasurePoints(font, text, format.SizePoints);

            var x = alignment switch
            {
                ParagraphAlignment.Center => _left + (width - textWidth) / 2,
                ParagraphAlignment.Right => _left + width - textWidth,
                _ => _left,
            };

            canvas.DrawText(text, Math.Max(_left, x), y + format.SizePoints);
        }
    }
}

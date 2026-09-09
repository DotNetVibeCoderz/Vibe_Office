// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Globalization;
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

        /// <summary>The line being set, reused rather than reallocated for every line.</summary>
        private readonly List<LinePiece> _line = [];
        private readonly List<PdfPage> _pages = [];
        private readonly List<(PdfPage Page, int Number)> _pageNumbers = [];

        // Footnotes referenced on the page being laid out, and the height reserved for them at the
        // foot of it. The reservation has to happen as each reference is met, because a note found
        // on the last line changes where that line is allowed to sit.
        private readonly List<(int Number, string Text)> _pageNotes = [];
        private double _noteAreaHeight;
        private int _nextNoteNumber = 1;

        // Floating objects met on the page being laid out. Their rectangles stay for the rest of the
        // page so that later lines flow around them; the ones that belong in front of the text are
        // held back and painted when the page is finished, since PDF has no z-order but paint order.
        private readonly List<Exclusion> _floats = [];
        private readonly List<Action<PdfCanvas>> _deferredFloats = [];

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

        /// <summary>Where the body text has to stop, once the page's footnotes are accounted for.</summary>
        private double TextBottom => _bottom - _noteAreaHeight;

        private const double NoteFontSize = 8.5;
        private const double NoteLineHeight = 11;
        private const double NoteSeparatorGap = 8;

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
            DrawPageNotes();
            DrawDeferredFloats();

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
            // The page's footnotes are drawn last but belong at its foot, which is why the space
            // was reserved as the references were met rather than found at the end.
            DrawPageNotes();
            DrawDeferredFloats();

            _canvas?.Dispose();
            _floats.Clear();

            _page = _pdf.Pages.Add(_pageBox);
            _pages.Add(_page);
            _pageNumbers.Add((_page, _pages.Count));

            _canvas = _page.OpenCanvas();
            _canvas.TopDown = true;
            _cursor = _top;
        }

        private void EnsureSpace(double height)
        {
            if (_cursor + height > TextBottom && _cursor > _top)
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
            OfficeColor? Highlight)
        {
            /// <summary>Raises the text, which is how a footnote's reference number is drawn.</summary>
            public bool Superscript { get; init; }
        }

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
                // An anchored drawing floats: it is placed once, at its own coordinates, and the
                // lines that follow flow around it. An inline one is laid out where it sits, like a
                // very large character. Sending both down the inline path — which is what a flowing
                // engine does by default — puts every text box in the wrong place and pushes the
                // paragraph down by its height.
                foreach (var drawing in runs.Where(r => r.HasDrawing)
                             .SelectMany(r => r.Element.Elements(Ns.W + "drawing")))
                {
                    if (drawing.Element(Ns.Wp + "anchor") is { } anchored)
                    {
                        PlaceFloating(drawing, anchored, contentLeft, contentWidth);
                    }
                    else
                    {
                        DrawInlineDrawing(drawing, contentLeft, contentWidth, alignment);
                    }
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

            var filler = new LineFiller(segments);
            var bulletIndent = bulletText is null ? 0 : 18;

            for (var i = 0; !filler.AtEnd; i++)
            {
                EnsureSpace(lineHeight);

                var indent = i == 0 ? Math.Max(0, firstLineIndent) : 0;
                var slotLeft = contentLeft + indent + bulletIndent;
                var slotWidth = contentWidth - indent - bulletIndent;

                // The width has to be settled here rather than up front, because it depends on which
                // floats this line is level with, and that depends on where the cursor has reached.
                var (lineLeft, lineWidth) = FreeSpan(slotLeft, slotWidth, lineHeight);

                if (lineWidth < MinimumLineWidth)
                {
                    // Not enough room beside a float for even a short word. Step down past it rather
                    // than setting text on top of it. This terminates: the cursor only moves down,
                    // and the next page starts with no floats on it.
                    _cursor += lineHeight;
                    i--;
                    continue;
                }

                // Safe to reuse across lines because it is live only from here to the DrawLine
                // below. EnsureSpace above is the one call that can re-enter layout — starting a
                // page draws that page's footnotes and its deferred floats — and by then the
                // previous line has already been drawn.
                var line = filler.Next(lineWidth, _line);

                if (i == 0 && bulletText is not null)
                {
                    var canvas = _canvas!;
                    canvas.SetFont(StandardFonts.Match(baseFormat.FontFamily, false, false),
                        baseFormat.SizePoints);
                    canvas.SetFillColor(baseFormat.Color);
                    canvas.DrawText(bulletText, lineLeft - bulletIndent,
                        _cursor + baseFormat.SizePoints * 0.85);
                }

                // The last line of a justified paragraph is set flush left, as typesetting requires.
                var effectiveAlignment = alignment == ParagraphAlignment.Justify && filler.AtEnd
                    ? ParagraphAlignment.Left
                    : alignment;

                DrawLine(line, lineLeft, lineWidth, lineHeight, effectiveAlignment);
                _cursor += lineHeight;
            }

            _cursor += after;
        }

        /// <summary>Counts spaces without allocating an enumerator.</summary>
        /// <remarks>
        /// <c>text.Count(c => c == ' ')</c> reads better and boxes the string's char enumerator every
        /// time it is called, which is once per word on every line of the document.
        /// </remarks>
        private static int CountSpaces(string text)
        {
            var count = 0;

            foreach (var c in text)
            {
                if (c == ' ')
                {
                    count++;
                }
            }

            return count;
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

                // A footnote reference run carries no text — the number is drawn by the consumer,
                // not stored — so it has to be recognised before the empty-run check below.
                if (TakeNoteReference(run) is { } number)
                {
                    result.Add(new Segment(
                        number.ToString(CultureInfo.InvariantCulture),
                        Resolve(run, paragraph) with { SizePoints = NoteFontSize, Superscript = true }));

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

        /// <summary>
        /// Recognises a footnote reference, numbers it, and reserves room for it on this page.
        /// </summary>
        /// <returns>The note's display number, or <c>null</c> when the run is not a reference.</returns>
        /// <remarks>
        /// <para>
        /// The reservation happens here rather than at the end of the page because it changes where
        /// the body text is allowed to stop: a note found on what would have been the last line
        /// pushes that line onto the next page.
        /// </para>
        /// <para>
        /// Endnotes are deliberately not handled. They belong in a block after the last page, which
        /// is a different piece of work; a reference to one is dropped rather than drawn in the
        /// wrong place.
        /// </para>
        /// </remarks>
        private int? TakeNoteReference(Run run)
        {
            var reference = run.Element.Element(Ns.W + "footnoteReference");

            if (reference is null)
            {
                return null;
            }

            var id = reference.IntAttr(Ns.W + "id");
            var note = _document.Footnotes[id];

            if (note is null)
            {
                // A reference with no definition: draw nothing rather than a number pointing at
                // a note the reader will never find.
                return null;
            }

            var number = _nextNoteNumber++;
            var text = note.Text.Trim();

            _pageNotes.Add((number, text));

            // Reserve the note's own height plus, for the first note on the page, the gap and the
            // separator rule above them.
            var lines = Math.Max(1, EstimateNoteLines(number, text));

            _noteAreaHeight += lines * NoteLineHeight;

            if (_pageNotes.Count == 1)
            {
                _noteAreaHeight += NoteSeparatorGap * 2;
            }

            return number;
        }

        /// <summary>How many lines a note will take at the width available to it.</summary>
        private int EstimateNoteLines(int number, string text)
        {
            var font = StandardFonts.Match(_options.DefaultFontFamily, false, false);
            var prefix = number.ToString(CultureInfo.InvariantCulture) + " ";

            var width = StandardFonts.MeasurePoints(font, prefix + text, NoteFontSize);
            var available = Math.Max(1, ContentWidth);

            return (int)Math.Ceiling(width / available);
        }

        /// <summary>Draws the current page's footnotes at its foot, above the bottom margin.</summary>
        private void DrawPageNotes()
        {
            if (_canvas is not { } canvas || _pageNotes.Count == 0)
            {
                _pageNotes.Clear();
                _noteAreaHeight = 0;
                return;
            }

            // Anchored to the bottom margin rather than to where the text happened to end: a
            // half-empty page still carries its notes at the foot, which is what a footnote means.
            var y = _bottom - _noteAreaHeight + NoteSeparatorGap;

            canvas.SetStrokeColor(OfficeColor.FromRgb(0x80, 0x80, 0x80));
            canvas.SetLineWidth(0.5);
            canvas.MoveTo(_left, y).LineTo(_left + Math.Min(ContentWidth / 3, 144), y).Stroke();

            y += NoteSeparatorGap;

            var font = StandardFonts.Match(_options.DefaultFontFamily, false, false);

            foreach (var (number, text) in _pageNotes)
            {
                var prefix = number.ToString(CultureInfo.InvariantCulture);

                canvas.SetFont(font, NoteFontSize * 0.8);
                canvas.SetFillColor(OfficeColor.Black);
                canvas.DrawText(prefix, _left, y + NoteFontSize * 0.5);

                var indent = StandardFonts.MeasurePoints(font, prefix + " ", NoteFontSize);

                canvas.SetFont(font, NoteFontSize);

                foreach (var line in WrapPlainText(text, ContentWidth - indent, font, NoteFontSize))
                {
                    canvas.DrawText(line, _left + indent, y + NoteFontSize * 0.85);
                    y += NoteLineHeight;
                }
            }

            _pageNotes.Clear();
            _noteAreaHeight = 0;
        }

        /// <summary>Breaks plain text to a width, for the note area's simpler layout needs.</summary>
        private static IEnumerable<string> WrapPlainText(
            string text, double width, StandardFont font, double size)
        {
            var current = string.Empty;

            foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = current.Length == 0 ? word : current + " " + word;

                if (StandardFonts.MeasurePoints(font, candidate, size) > width && current.Length > 0)
                {
                    yield return current;
                    current = word;
                }
                else
                {
                    current = candidate;
                }
            }

            if (current.Length > 0)
            {
                yield return current;
            }
        }

        private static bool IsInstructionRun(Run run) =>
            run.Element.Element(Ns.W + "instrText") is not null ||
            run.Element.Element(Ns.W + "fldChar") is not null;

        /// <summary>One piece of a line: some text, its formatting, and how wide it is.</summary>
        /// <remarks>
        /// The width is carried rather than recomputed. <see cref="LineFiller"/> has to measure every
        /// word to decide where the line breaks, and measuring it a second time to draw it was the
        /// single largest per-word cost in the export.
        /// </remarks>
        private readonly record struct LinePiece(string Text, Effective Format, double Width);

        private static List<List<LinePiece>> WrapSegments(List<Segment> segments, double width,
            double firstLineIndent, double bulletIndent)
        {
            var filler = new LineFiller(segments);
            var lines = new List<List<LinePiece>>();
            var available = width - Math.Max(0, firstLineIndent) - bulletIndent;

            while (!filler.AtEnd)
            {
                lines.Add(filler.Next(available));
                available = width - bulletIndent;
            }

            return lines;
        }

        /// <summary>
        /// Fills one line at a time, so the width can change from line to line.
        /// </summary>
        /// <remarks>
        /// Wrapping a whole paragraph at one fixed width is simpler, and wrong the moment something
        /// floats beside it: the lines level with a text box are narrower than the ones above and
        /// below it. The width is therefore asked for per line, once the cursor is known and the
        /// floats it passes are known with it.
        /// </remarks>
        private sealed class LineFiller
        {
            private readonly List<(string Word, Effective Format, StandardFont Font, double Width)> _words = [];
            private int _index;

            public LineFiller(List<Segment> segments)
            {
                // Growing from four doubles the list four times for a normal paragraph, and every
                // step copies and abandons the one before — most of what building a filler cost.
                // Words average five or six characters, so this overshoots rarely and by little.
                var estimate = 0;

                foreach (var segment in segments)
                {
                    estimate += (segment.Text.Length / 5) + 1;
                }

                _words.EnsureCapacity(estimate);

                foreach (var segment in segments)
                {
                    var font = StandardFonts.Match(segment.Format.FontFamily, segment.Format.Bold,
                        segment.Format.Italic);

                    // Splitting on spaces but keeping each space attached to the word before it is
                    // what makes the measured width match what is drawn.
                    foreach (var word in SplitWords(segment.Text))
                    {
                        _words.Add((word, segment.Format, font,
                            StandardFonts.MeasurePoints(font, word, segment.Format.SizePoints)));
                    }
                }
            }

            /// <summary>Whether every word has been placed.</summary>
            public bool AtEnd => _index >= _words.Count;

            /// <summary>Takes as many words as fit in <paramref name="available"/> points.</summary>
            public List<LinePiece> Next(double available) => Next(available, []);

            /// <summary>
            /// Takes as many words as fit, into a list the caller owns.
            /// </summary>
            /// <remarks>
            /// A caller that draws each line and forgets it can hand the same list back every time,
            /// which is worth about 1.4 KB a line — a fresh list starts at four entries and doubles
            /// three times before it holds a normal line's words, copying and abandoning the one
            /// before at every step. A caller that keeps its lines, as <see cref="WrapSegments"/>
            /// does, passes a new one and gets the obvious behaviour.
            /// </remarks>
            public List<LinePiece> Next(double available, List<LinePiece> line)
            {
                line.Clear();

                double used = 0;

                while (_index < _words.Count)
                {
                    var (word, format, font, width) = _words[_index];

                    if (line.Count == 0)
                    {
                        // A line never starts with a space — it would print as an indent nobody asked
                        // for, and a different one on every line.
                        var trimmed = word.TrimStart();

                        if (trimmed.Length == 0)
                        {
                            _index++;
                            continue;
                        }

                        if (trimmed.Length != word.Length)
                        {
                            word = trimmed;
                            width = StandardFonts.MeasurePoints(font, trimmed, format.SizePoints);
                        }
                    }
                    else if (format.Superscript)
                    {
                        // A footnote marker belongs to the word before it. Letting it wrap on its own
                        // leaves a bare superscript digit at the start of a line, which reads as a
                        // typesetting error rather than as a reference.
                        line.Add(new LinePiece(word, format, width));
                        used += width;
                        _index++;
                        continue;
                    }

                    if (used + width > available && line.Count > 0)
                    {
                        return line;
                    }

                    line.Add(new LinePiece(word, format, width));
                    used += width;
                    _index++;
                }

                return line;
            }
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
            ParagraphAlignment alignment) =>
            DrawLineOn(_canvas!, pieces, x, width, _cursor, lineHeight, alignment);

        /// <summary>
        /// Sets one line on a given canvas at a given top.
        /// </summary>
        /// <remarks>
        /// Taking both explicitly is what lets a shape reuse the whole of this: its text is laid out
        /// inside its own box rather than at the page cursor, and duplicating the baseline and
        /// justification arithmetic to say so is how the two quietly drift apart.
        /// </remarks>
        private static void DrawLineOn(PdfCanvas canvas, List<LinePiece> pieces, double x,
            double width, double top, double lineHeight, ParagraphAlignment alignment)
        {
            // One pass with plain loops rather than three LINQ passes. Each of those allocated a
            // closure and an enumerator per line, and the innermost — counting spaces with
            // string.Count(predicate) — boxed the string's char enumerator once per word. On a
            // ten-thousand-paragraph export that was tens of megabytes of garbage for three numbers.
            var totalWidth = 0.0;
            var spaces = 0;
            var maxSize = 0.0;

            foreach (var piece in pieces)
            {
                totalWidth += piece.Width;
                spaces += CountSpaces(piece.Text);

                if (piece.Format.SizePoints > maxSize)
                {
                    maxSize = piece.Format.SizePoints;
                }
            }

            var cursor = alignment switch
            {
                ParagraphAlignment.Center => x + Math.Max(0, (width - totalWidth) / 2),
                ParagraphAlignment.Right => x + Math.Max(0, width - totalWidth),
                _ => x,
            };

            // Justification stretches the spaces between words, which means distributing the slack
            // over the gaps rather than scaling the glyphs.
            var extraPerSpace = 0.0;

            if (alignment is ParagraphAlignment.Justify or ParagraphAlignment.Distribute &&
                spaces > 0 && totalWidth < width)
            {
                extraPerSpace = (width - totalWidth) / spaces;
            }

            var baseline = top + lineHeight - (lineHeight - maxSize) / 2 - maxSize * 0.22;

            // Pieces are words, and a line's words nearly always share one format. Drawing each one
            // on its own emits a font, a colour and a text-positioning operator per word — measured
            // at 713 bytes allocated per word, against 147 when twelve words go out in one call. So
            // consecutive pieces that would be drawn identically are drawn as one.
            //
            // This is exact rather than approximate. SplitWords keeps each word's trailing space
            // attached, so concatenating consecutive pieces reproduces the text character for
            // character; and a piece's width is the sum of its glyph advances, so the merged run
            // puts every glyph exactly where the per-piece loop put it. The decorations follow:
            // abutting highlight rectangles are one rectangle, abutting underlines one line.
            var index = 0;

            while (index < pieces.Count)
            {
                var format = pieces[index].Format;
                var font = StandardFonts.Match(format.FontFamily, format.Bold, format.Italic);

                var runEnd = index + 1;
                var runWidth = pieces[index].Width + (extraPerSpace * CountSpaces(pieces[index].Text));

                // Justified text positions every word itself, so there is nothing to merge into.
                if (extraPerSpace <= 0)
                {
                    while (runEnd < pieces.Count && pieces[runEnd].Format == format)
                    {
                        runWidth += pieces[runEnd].Width;
                        runEnd++;
                    }
                }

                canvas.SetFont(font, format.SizePoints);

                if (format.Highlight is { } highlight)
                {
                    canvas.SetFillColor(highlight);
                    canvas.Rectangle(cursor, baseline - format.SizePoints * 0.8,
                        runWidth, format.SizePoints * 1.05).Fill();
                }

                canvas.SetFillColor(format.Color);

                // A superscript sits above the baseline; without the shift a footnote's number
                // reads as a stray digit in the middle of the sentence.
                var pieceBaseline = format.Superscript
                    ? baseline - format.SizePoints * 0.42
                    : baseline;

                if (extraPerSpace > 0)
                {
                    DrawWithExtraSpacing(canvas, pieces[index], font, cursor, pieceBaseline,
                        extraPerSpace);
                }
                else
                {
                    canvas.DrawText(TextOf(pieces, index, runEnd), cursor, pieceBaseline);
                }

                if (format.Underline)
                {
                    canvas.SetStrokeColor(format.Color);
                    canvas.SetLineWidth(Math.Max(0.5, format.SizePoints * 0.055));
                    canvas.DrawLine(cursor, baseline + format.SizePoints * 0.14,
                        cursor + runWidth, baseline + format.SizePoints * 0.14);
                }

                if (format.Strike)
                {
                    canvas.SetStrokeColor(format.Color);
                    canvas.SetLineWidth(Math.Max(0.5, format.SizePoints * 0.055));
                    canvas.DrawLine(cursor, baseline - format.SizePoints * 0.28,
                        cursor + runWidth, baseline - format.SizePoints * 0.28);
                }

                cursor += runWidth;
                index = runEnd;
            }
        }

        /// <summary>Joins a run of pieces back into the text they were split from.</summary>
        /// <remarks>
        /// A run of one — the common case for a line of mixed formatting, and every case under
        /// justification — hands back the piece's own string rather than copying it.
        /// </remarks>
        private static string TextOf(List<LinePiece> pieces, int start, int end)
        {
            if (end - start == 1)
            {
                return pieces[start].Text;
            }

            var length = 0;

            for (var i = start; i < end; i++)
            {
                length += pieces[i].Text.Length;
            }

            return string.Create(length, (pieces, start, end), static (span, state) =>
            {
                var (source, from, to) = state;
                var at = 0;

                for (var i = from; i < to; i++)
                {
                    source[i].Text.CopyTo(span[at..]);
                    at += source[i].Text.Length;
                }
            });
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

        private void DrawInlineDrawing(XElement drawing, double x, double width,
            ParagraphAlignment alignment)
        {
            var extent = drawing.Descendants(Ns.Wp + "extent").FirstOrDefault();
            var drawnWidth = Length.FromEmu(extent.LongAttr("cx")).Points;
            var drawnHeight = Length.FromEmu(extent.LongAttr("cy")).Points;

            if (drawnWidth <= 0 || drawnHeight <= 0)
            {
                return;
            }

            // An object wider than the text column is scaled down rather than clipped, which is what
            // Word does when a picture is pasted at full resolution.
            if (drawnWidth > width)
            {
                drawnHeight *= width / drawnWidth;
                drawnWidth = width;
            }

            EnsureSpace(drawnHeight);

            var offset = alignment switch
            {
                ParagraphAlignment.Center => (width - drawnWidth) / 2,
                ParagraphAlignment.Right => width - drawnWidth,
                _ => 0,
            };

            Paint(_canvas!, drawing, x + Math.Max(0, offset), _cursor, drawnWidth, drawnHeight);

            _cursor += drawnHeight + 6;
        }

        /// <summary>
        /// Places an anchored drawing at its own coordinates and records the space it takes from the
        /// text.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The position is an offset from something the anchor names, and the names do not all mean
        /// the same origin: <c>page</c> counts from the paper edge, <c>margin</c> and <c>column</c>
        /// from the text area, <c>paragraph</c> from wherever this paragraph has reached. Reading all
        /// four as one origin puts objects a margin out of place, consistently enough to look
        /// deliberate.
        /// </para>
        /// <para>
        /// The float is painted before the text so the text sits over it, which is what a watermark
        /// and a filled box both want. Only <c>InFrontOfText</c> needs the other order, and that one
        /// is held back until the page is finished.
        /// </para>
        /// </remarks>
        private void PlaceFloating(XElement drawing, XElement anchor, double contentLeft,
            double contentWidth)
        {
            var extent = anchor.Element(Ns.Wp + "extent");
            var width = Length.FromEmu(extent.LongAttr("cx")).Points;
            var height = Length.FromEmu(extent.LongAttr("cy")).Points;

            if (width <= 0 || height <= 0)
            {
                return;
            }

            var x = Origin(anchor, Ns.Wp + "positionH", horizontal: true, contentLeft) +
                    Offset(anchor, Ns.Wp + "positionH");

            var y = Origin(anchor, Ns.Wp + "positionV", horizontal: false, contentLeft) +
                    Offset(anchor, Ns.Wp + "positionV");

            // A float positioned off the page comes from a template that assumed different paper.
            // Clamping keeps it visible instead of silently dropping it.
            x = Math.Clamp(x, 0, Math.Max(0, _pageBox.Width - width));
            y = Math.Max(0, y);

            var wrap = WrapOf(anchor);

            if (wrap is FloatWrap.InFrontOfText)
            {
                // Captured by value, so the page it was met on is the page it lands on.
                var (px, py, pw, ph) = (x, y, width, height);
                _deferredFloats.Add(canvas => Paint(canvas, drawing, px, py, pw, ph));
            }
            else
            {
                Paint(_canvas!, drawing, x, y, width, height);
            }

            if (wrap is FloatWrap.None)
            {
                return;
            }

            // The gaps Word keeps between the object and the text flowing past it.
            var gapLeft = Length.FromEmu(anchor.LongAttr("distL")).Points;
            var gapRight = Length.FromEmu(anchor.LongAttr("distR")).Points;
            var gapTop = Length.FromEmu(anchor.LongAttr("distT")).Points;
            var gapBottom = Length.FromEmu(anchor.LongAttr("distB")).Points;

            // Top-and-bottom wrap stops the text rather than narrowing it, which is the same thing as
            // an exclusion spanning the whole column.
            var (excludeLeft, excludeRight) = wrap == FloatWrap.TopAndBottom
                ? (contentLeft, contentLeft + contentWidth)
                : (x - gapLeft, x + width + gapRight);

            _floats.Add(new Exclusion(excludeLeft, y - gapTop, excludeRight, y + height + gapBottom));
        }

        private void DrawDeferredFloats()
        {
            if (_canvas is not { } canvas)
            {
                _deferredFloats.Clear();
                return;
            }

            foreach (var paint in _deferredFloats)
            {
                paint(canvas);
            }

            _deferredFloats.Clear();
        }

        /// <summary>What an anchor offset is measured from, in page points.</summary>
        private double Origin(XElement anchor, XName position, bool horizontal, double contentLeft) =>
            anchor.Element(position)?.Attr("relativeFrom") switch
            {
                "page" => 0,
                "margin" => horizontal ? _left : _top,
                "character" => contentLeft,
                "line" or "paragraph" => horizontal ? _left : _cursor,
                // "column" and anything unrecognised: the text area, which is where Word puts it in a
                // single-column document and the closest honest answer in a multi-column one.
                _ => horizontal ? _left : _cursor,
            };

        private static double Offset(XElement anchor, XName position) =>
            Length.FromEmu(
                long.TryParse(anchor.Element(position)?.Element(Ns.Wp + "posOffset")?.Value,
                    CultureInfo.InvariantCulture, out var emu) ? emu : 0).Points;

        private enum FloatWrap { None, Square, TopAndBottom, InFrontOfText }

        private static FloatWrap WrapOf(XElement anchor)
        {
            if (anchor.Element(Ns.Wp + "wrapSquare") is not null ||
                anchor.Element(Ns.Wp + "wrapTight") is not null ||
                anchor.Element(Ns.Wp + "wrapThrough") is not null)
            {
                // Tight and through follow the object outline rather than its box. Doing that
                // properly means intersecting every line with the wrap polygon; the box is a visible
                // approximation rather than a wrong answer.
                return FloatWrap.Square;
            }

            if (anchor.Element(Ns.Wp + "wrapTopAndBottom") is not null)
            {
                return FloatWrap.TopAndBottom;
            }

            // wrapNone covers both no-wrap cases, and behindDoc is what tells them apart.
            return anchor.Attr("behindDoc") == "1" ? FloatWrap.None : FloatWrap.InFrontOfText;
        }

        /// <summary>A rectangle text may not enter, in page points with y growing downwards.</summary>
        private readonly record struct Exclusion(double Left, double Top, double Right, double Bottom);

        /// <summary>The narrowest line worth setting beside a float.</summary>
        private const double MinimumLineWidth = 36;

        /// <summary>
        /// The widest run of a line slot that no float occupies.
        /// </summary>
        /// <remarks>
        /// Word can break one line into several pieces around several objects. This takes the widest
        /// single gap instead, which is the same answer whenever one object floats beside the text —
        /// the case that covers pull quotes, logos and side figures — and a narrower one when two do.
        /// </remarks>
        private (double Left, double Width) FreeSpan(double left, double width, double lineHeight)
        {
            if (_floats.Count == 0)
            {
                return (left, width);
            }

            var top = _cursor;
            var bottom = _cursor + lineHeight;

            var blocking = _floats
                .Where(f => f.Top < bottom && f.Bottom > top)
                .OrderBy(f => f.Left)
                .ToList();

            if (blocking.Count == 0)
            {
                return (left, width);
            }

            var bestLeft = left;
            double bestWidth = 0;
            var cursor = left;

            foreach (var block in blocking)
            {
                var gap = Math.Min(block.Left, left + width) - cursor;

                if (gap > bestWidth)
                {
                    (bestLeft, bestWidth) = (cursor, gap);
                }

                cursor = Math.Max(cursor, block.Right);
            }

            var tail = left + width - cursor;

            if (tail > bestWidth)
            {
                (bestLeft, bestWidth) = (cursor, tail);
            }

            return (bestLeft, Math.Max(0, bestWidth));
        }

        /// <summary>Draws whatever a <c>w:drawing</c> holds: a picture, or a shape.</summary>
        private void Paint(PdfCanvas canvas, XElement drawing, double x, double y,
            double width, double height)
        {
            if (drawing.Descendants(Ns.Wps + "wsp").FirstOrDefault() is { } shape)
            {
                PaintShape(canvas, shape, x, y, width, height);
                return;
            }

            PaintPicture(canvas, drawing, x, y, width, height);
        }

        private void PaintPicture(PdfCanvas canvas, XElement drawing, double x, double y,
            double width, double height)
        {
            var embedId = drawing.Descendants(Ns.A + "blip").FirstOrDefault()?.Attr(Ns.R + "embed");

            if (embedId is null || _document.DocumentPart.RelatedPart(embedId) is not { } part)
            {
                return;
            }

            try
            {
                canvas.DrawImage(part.GetBytes(), x, y, width, height);
            }
            catch (OfficeNetException)
            {
                // A format PdfNet cannot embed (GIF, TIFF, a metafile) leaves a gap rather than
                // aborting the whole export.
            }
        }

        /// <summary>
        /// Draws a shape: its outline, its fill, and any text inside it.
        /// </summary>
        /// <remarks>
        /// The geometry is a preset name out of a list of some two hundred. Six of them cover almost
        /// everything anyone puts in a Word document, and the rest fall back to a rectangle: a shape
        /// drawn as the wrong outline still holds the right words in the right place, which is a
        /// better failure than a blank hole.
        /// </remarks>
        private void PaintShape(PdfCanvas canvas, XElement shape, double x, double y,
            double width, double height)
        {
            var properties = shape.Element(Ns.Wps + "spPr");
            var fill = SolidColor(properties?.Element(Ns.A + "solidFill"));
            var outline = properties?.Element(Ns.A + "ln");
            var stroke = SolidColor(outline?.Element(Ns.A + "solidFill"));
            var strokeWidth = Length.FromEmu(outline.LongAttr("w")).Points;

            // An outline with a colour and no width is Word saying "use the default", not "hairline".
            if (stroke is not null && strokeWidth <= 0)
            {
                strokeWidth = 0.75;
            }

            var rotation = properties?.Element(Ns.A + "xfrm")?.Attr("rot") is { } rot &&
                           long.TryParse(rot, CultureInfo.InvariantCulture, out var units)
                ? units / 60000.0
                : 0;

            var geometry = properties?.Element(Ns.A + "prstGeom")?.Attr("prst") ?? "rect";

            canvas.Save();

            if (rotation != 0)
            {
                // DrawingML rotates about the centre of the box, clockwise, in a top-down system.
                // The canvas flips y per drawing call rather than through the matrix, so the centre
                // has to be named in PDF space — y up from the bottom of the page — or the rotation
                // turns about a point reflected across the middle of the sheet and the shape lands
                // somewhere else entirely. Translate() is no use here for the same reason: it negates
                // dy for top-down callers, which does not compose either side of a rotation.
                var centreX = x + width / 2;
                var centreY = _pageBox.Height - (y + height / 2);

                canvas.Transform(1, 0, 0, 1, centreX, centreY);
                canvas.Rotate(-rotation);
                canvas.Transform(1, 0, 0, 1, -centreX, -centreY);
            }

            if (fill is not null || stroke is not null)
            {
                if (fill is { } fillColor)
                {
                    canvas.SetFillColor(fillColor);
                }

                if (stroke is { } strokeColor)
                {
                    canvas.SetStrokeColor(strokeColor);
                    canvas.SetLineWidth(strokeWidth);
                }

                Trace(canvas, geometry, x, y, width, height);

                if (fill is not null && stroke is not null)
                {
                    canvas.FillAndStroke();
                }
                else if (fill is not null)
                {
                    canvas.Fill();
                }
                else
                {
                    canvas.Stroke();
                }
            }

            // Inside the transform, so the text turns with the box it sits in.
            PaintShapeText(canvas, shape, x, y, width, height);

            canvas.Restore();
        }

        /// <summary>Traces a preset geometry as a path, ready to fill or stroke.</summary>
        private static void Trace(PdfCanvas canvas, string geometry, double x, double y,
            double width, double height)
        {
            switch (geometry)
            {
                case "ellipse":
                    canvas.Ellipse(x, y, width, height);
                    break;

                case "roundRect":
                case "wedgeRoundRectCallout":
                    // Word measures the corner as a fraction of the shorter side; this is its default.
                    canvas.RoundedRectangle(x, y, width, height, Math.Min(width, height) * 0.16667);
                    break;

                case "line":
                case "straightConnector1":
                    canvas.MoveTo(x, y);
                    canvas.LineTo(x + width, y + height);
                    break;

                case "triangle":
                    canvas.MoveTo(x + width / 2, y);
                    canvas.LineTo(x + width, y + height);
                    canvas.LineTo(x, y + height);
                    canvas.ClosePath();
                    break;

                case "diamond":
                    canvas.MoveTo(x + width / 2, y);
                    canvas.LineTo(x + width, y + height / 2);
                    canvas.LineTo(x + width / 2, y + height);
                    canvas.LineTo(x, y + height / 2);
                    canvas.ClosePath();
                    break;

                case "hexagon":
                    var inset = width * 0.25;
                    canvas.MoveTo(x + inset, y);
                    canvas.LineTo(x + width - inset, y);
                    canvas.LineTo(x + width, y + height / 2);
                    canvas.LineTo(x + width - inset, y + height);
                    canvas.LineTo(x + inset, y + height);
                    canvas.LineTo(x, y + height / 2);
                    canvas.ClosePath();
                    break;

                case "star5":
                    TraceStar(canvas, x + width / 2, y + height / 2, width / 2, height / 2);
                    break;

                default:
                    canvas.Rectangle(x, y, width, height);
                    break;
            }
        }

        private static void TraceStar(PdfCanvas canvas, double centreX, double centreY,
            double radiusX, double radiusY)
        {
            // Ten points alternating between the outer and inner radius. The inner one is the ratio
            // that makes a five-pointed star look like one.
            const double InnerRatio = 0.382;

            for (var i = 0; i < 10; i++)
            {
                var angle = -Math.PI / 2 + i * Math.PI / 5;
                var scale = i % 2 == 0 ? 1 : InnerRatio;

                var pointX = centreX + Math.Cos(angle) * radiusX * scale;
                var pointY = centreY + Math.Sin(angle) * radiusY * scale;

                if (i == 0)
                {
                    canvas.MoveTo(pointX, pointY);
                }
                else
                {
                    canvas.LineTo(pointX, pointY);
                }
            }

            canvas.ClosePath();
        }

        /// <summary>Sets a shape text inside its box, clipped to it.</summary>
        /// <remarks>
        /// The insets come from <c>wps:bodyPr</c> and default to Word own: a tenth of an inch left
        /// and right, half that above and below. Ignoring them puts the first character hard against
        /// the outline, which is the single most obvious sign of a shape drawn by something other
        /// than Word.
        /// </remarks>
        private void PaintShapeText(PdfCanvas canvas, XElement shape, double x, double y,
            double width, double height)
        {
            var content = shape.Element(Ns.Wps + "txbx")?.Element(Ns.W + "txbxContent");

            if (content is null)
            {
                return;
            }

            var body = shape.Element(Ns.Wps + "bodyPr");
            var insetLeft = Inset(body, "lIns", 91440);
            var insetRight = Inset(body, "rIns", 91440);
            var insetTop = Inset(body, "tIns", 45720);
            var insetBottom = Inset(body, "bIns", 45720);

            var textLeft = x + insetLeft;
            var textWidth = Math.Max(1, width - insetLeft - insetRight);
            var textBottom = y + height - insetBottom;
            var cursor = y + insetTop;

            foreach (var element in content.Elements(Ns.W + "p"))
            {
                var paragraph = new Paragraph(_document, element);
                var runs = paragraph.Runs;
                var format = Resolve(runs.FirstOrDefault(), paragraph);
                var (before, after, lineHeight, alignment, _, _, _) =
                    ResolveParagraph(paragraph, format.SizePoints);

                cursor += before;

                foreach (var line in WrapSegments(BuildSegments(runs, paragraph), textWidth, 0, 0))
                {
                    // Text past the bottom of the box is clipped by Word too — it simply stops being
                    // visible. Drawing it anyway would spill words across the page.
                    if (cursor + lineHeight > textBottom)
                    {
                        return;
                    }

                    DrawLineOn(canvas, line, textLeft, textWidth, cursor, lineHeight, alignment);
                    cursor += lineHeight;
                }

                cursor += after;
            }

            static double Inset(XElement? body, string name, long fallback) =>
                Length.FromEmu(
                    long.TryParse(body?.Attr(name), CultureInfo.InvariantCulture, out var emu)
                        ? emu
                        : fallback).Points;
        }

        private static OfficeColor? SolidColor(XElement? fill) =>
            fill?.Element(Ns.A + "srgbClr")?.Attr("val") is { } hex &&
            OfficeColor.TryParse(hex, out var color)
                ? color
                : null;

        // ---- Tables ------------------------------------------------------------------------------

        private void DrawTable(Table table)
        {
            var columnCount = Math.Max(1, table.ColumnCount);
            var widths = ResolveColumnWidths(table, columnCount);

            var headerRows = table.Rows.TakeWhile(r => r.IsHeader).ToList();

            foreach (var row in table.Rows)
            {
                var rowHeight = MeasureRow(row, widths);

                if (_cursor + rowHeight > TextBottom && _cursor > _top)
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

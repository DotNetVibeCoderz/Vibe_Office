// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Text;
using PdfNet.Document;
using PdfNet.Objects;

namespace PdfNet.Text;

/// <summary>A run of text with the position and size it was drawn at, in page coordinates.</summary>
/// <param name="Text">The decoded text.</param>
/// <param name="X">The left edge in points from the page's left.</param>
/// <param name="Y">The baseline in points from the page's bottom.</param>
/// <param name="Width">The advance width in points.</param>
/// <param name="FontSize">The effective font size in points.</param>
/// <param name="FontName">The font's base name, when known.</param>
public readonly record struct TextFragment(
    string Text,
    double X,
    double Y,
    double Width,
    double FontSize,
    string? FontName)
{
    /// <summary>The right edge of the fragment.</summary>
    public double Right => X + Width;

    public override string ToString() => $"\"{Text}\" @ ({X:0.#}, {Y:0.#}) {FontSize:0.#}pt";
}

/// <summary>
/// Recovers text from a page's content stream.
/// </summary>
/// <remarks>
/// <para>
/// A PDF does not contain text in reading order; it contains drawing instructions. Recovering
/// readable text means running the text-positioning operators to find where each run landed, then
/// grouping runs into lines by their baselines and inferring the spaces that the file never stored
/// — because a producer that positions each word with <c>Td</c> writes no space characters at all.
/// </para>
/// <para>
/// This is why extraction quality varies between tools on the same file, and why the width metrics
/// in <see cref="PdfFontInfo"/> matter: the gap threshold is a fraction of the font's own space
/// width, not a fixed number of points, so it works at 8 pt and at 40 pt.
/// </para>
/// </remarks>
public static class TextExtractor
{
    /// <summary>Extracts a page's text as lines in reading order.</summary>
    public static string Extract(PdfPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return Assemble(ExtractFragments(page));
    }

    /// <summary>Extracts a page's text as positioned fragments.</summary>
    public static IReadOnlyList<TextFragment> ExtractFragments(PdfPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        var state = new ExtractionState(page.Document);
        var fragments = new List<TextFragment>(256);

        Run(page.GetContent(), page.Resources, page.Document, state, fragments, 0);
        return fragments;
    }

    private sealed class ExtractionState(PdfDocument document)
    {
        public readonly PdfDocument Document = document;
        public readonly Dictionary<PdfDictionary, PdfFontInfo> FontCache = [];

        public double[] Ctm = [1, 0, 0, 1, 0, 0];
        public readonly Stack<double[]> CtmStack = new();

        public double[] TextMatrix = [1, 0, 0, 1, 0, 0];
        public double[] LineMatrix = [1, 0, 0, 1, 0, 0];

        public PdfFontInfo? Font;
        public string? FontName;
        public double FontSize;
        public double CharSpacing;
        public double WordSpacing;
        public double HorizontalScale = 1;
        public double Leading;
        public double Rise;
        public int RenderMode;
    }

    private static void Run(byte[] content, PdfDictionary resources, PdfDocument document,
        ExtractionState state, List<TextFragment> fragments, int depth)
    {
        // Form XObjects nest, and a malformed file can make one contain itself.
        if (depth > 12)
        {
            return;
        }

        var fonts = document.Follow(resources[PdfName.Font]) as PdfDictionary;
        var xobjects = document.Follow(resources[PdfName.XObject]) as PdfDictionary;

        foreach (var operation in ContentStreamParser.Parse(content))
        {
            switch (operation.Operator)
            {
                case "q":
                    state.CtmStack.Push((double[])state.Ctm.Clone());
                    break;

                case "Q":
                    if (state.CtmStack.Count > 0)
                    {
                        state.Ctm = state.CtmStack.Pop();
                    }

                    break;

                case "cm":
                    state.Ctm = Multiply(
                    [
                        operation.Number(0, 1), operation.Number(1), operation.Number(2),
                        operation.Number(3, 1), operation.Number(4), operation.Number(5),
                    ], state.Ctm);
                    break;

                case "BT":
                    state.TextMatrix = [1, 0, 0, 1, 0, 0];
                    state.LineMatrix = [1, 0, 0, 1, 0, 0];
                    break;

                case "ET":
                    break;

                case "Tf":
                {
                    state.FontName = operation.Name(0);
                    state.FontSize = operation.Number(1);

                    var fontDictionary = state.FontName is null
                        ? null
                        : document.Follow(fonts?[PdfName.Get(state.FontName)]) as PdfDictionary;

                    if (fontDictionary is null)
                    {
                        state.Font = PdfFontInfo.Fallback;
                    }
                    else if (state.FontCache.TryGetValue(fontDictionary, out var cached))
                    {
                        state.Font = cached;
                    }
                    else
                    {
                        var info = PdfFontInfo.Read(fontDictionary, document);
                        state.FontCache[fontDictionary] = info;
                        state.Font = info;
                    }

                    break;
                }

                case "Tc":
                    state.CharSpacing = operation.Number(0);
                    break;

                case "Tw":
                    state.WordSpacing = operation.Number(0);
                    break;

                case "Tz":
                    state.HorizontalScale = operation.Number(0, 100) / 100.0;
                    break;

                case "TL":
                    state.Leading = operation.Number(0);
                    break;

                case "Ts":
                    state.Rise = operation.Number(0);
                    break;

                case "Tr":
                    state.RenderMode = (int)operation.Number(0);
                    break;

                case "Td":
                    state.LineMatrix = Multiply([1, 0, 0, 1, operation.Number(0), operation.Number(1)],
                        state.LineMatrix);
                    state.TextMatrix = (double[])state.LineMatrix.Clone();
                    break;

                case "TD":
                    // TD is Td plus setting the leading to the negated y offset.
                    state.Leading = -operation.Number(1);
                    state.LineMatrix = Multiply([1, 0, 0, 1, operation.Number(0), operation.Number(1)],
                        state.LineMatrix);
                    state.TextMatrix = (double[])state.LineMatrix.Clone();
                    break;

                case "Tm":
                    state.LineMatrix =
                    [
                        operation.Number(0, 1), operation.Number(1), operation.Number(2),
                        operation.Number(3, 1), operation.Number(4), operation.Number(5),
                    ];
                    state.TextMatrix = (double[])state.LineMatrix.Clone();
                    break;

                case "T*":
                    NextLine(state);
                    break;

                case "Tj":
                    ShowText(operation.String(0), state, fragments);
                    break;

                case "'":
                    NextLine(state);
                    ShowText(operation.String(0), state, fragments);
                    break;

                case "\"":
                    state.WordSpacing = operation.Number(0);
                    state.CharSpacing = operation.Number(1);
                    NextLine(state);
                    ShowText(operation.String(2), state, fragments);
                    break;

                case "TJ":
                {
                    var array = operation.Array(0);
                    if (array is null)
                    {
                        break;
                    }

                    foreach (var item in array)
                    {
                        switch (item)
                        {
                            case PdfString text:
                                ShowText(text.Value, state, fragments);
                                break;

                            case PdfNumber adjustment:
                            {
                                // A positive number moves left by that many thousandths of an em.
                                // This is where kerning lives, and where the *space* between words
                                // lives in text set by TeX and by most typesetting engines.
                                var shift = -adjustment.DoubleValue / 1000 * state.FontSize *
                                            state.HorizontalScale;
                                state.TextMatrix = Multiply([1, 0, 0, 1, shift, 0], state.TextMatrix);
                                break;
                            }
                        }
                    }

                    break;
                }

                case "Do":
                {
                    var name = operation.Name(0);
                    if (name is null || xobjects is null)
                    {
                        break;
                    }

                    if (document.Follow(xobjects[PdfName.Get(name)]) is not PdfStream form ||
                        form.DictionarySubtype != "Form")
                    {
                        break;
                    }

                    // A form XObject has its own resources and its own matrix, and the outer
                    // graphics state must be restored afterwards or the rest of the page shifts.
                    var savedCtm = (double[])state.Ctm.Clone();
                    var savedText = (double[])state.TextMatrix.Clone();
                    var savedLine = (double[])state.LineMatrix.Clone();

                    if (form.Get(PdfName.Get("Matrix")) is PdfArray matrix && matrix.Count >= 6)
                    {
                        state.Ctm = Multiply(matrix.AsDoubles(), state.Ctm);
                    }

                    var formResources = document.Follow(form[PdfName.Resources]) as PdfDictionary
                                        ?? resources;

                    Run(form.Decoded, formResources, document, state, fragments, depth + 1);

                    state.Ctm = savedCtm;
                    state.TextMatrix = savedText;
                    state.LineMatrix = savedLine;
                    break;
                }
            }
        }
    }

    private static void NextLine(ExtractionState state)
    {
        state.LineMatrix = Multiply([1, 0, 0, 1, 0, -state.Leading], state.LineMatrix);
        state.TextMatrix = (double[])state.LineMatrix.Clone();
    }

    private static void ShowText(byte[]? bytes, ExtractionState state, List<TextFragment> fragments)
    {
        if (bytes is null || bytes.Length == 0)
        {
            return;
        }

        var font = state.Font ?? PdfFontInfo.Fallback;

        // Render mode 3 is "invisible" and is what a scanner's OCR layer uses. It is real text and
        // must be extracted — skipping it makes every searchable scan come back empty.
        // Mode 7 (clip only) adds nothing to the page and is skipped.
        if (state.RenderMode == 7)
        {
            return;
        }

        var builder = new StringBuilder(bytes.Length);
        var startMatrix = (double[])state.TextMatrix.Clone();
        double totalAdvance = 0;

        foreach (var code in font.DecodeCodes(bytes))
        {
            var text = font.CodeToText(code);
            builder.Append(text);

            var glyphWidth = font.WidthOf(code) / 1000.0 * state.FontSize;
            var advance = (glyphWidth + state.CharSpacing) * state.HorizontalScale;

            // Word spacing applies only to the single byte 32 — and only in a simple font. In a
            // composite font it applies to a one-byte code 32, which Identity-H never produces.
            if (code == 32 && !font.IsComposite)
            {
                advance += state.WordSpacing * state.HorizontalScale;
            }

            totalAdvance += advance;
        }

        if (builder.Length > 0)
        {
            var (x, y) = Transform(startMatrix, state.Ctm, 0, state.Rise);
            var (x2, _) = Transform(startMatrix, state.Ctm, totalAdvance, state.Rise);

            // Scale is the CTM's effect on a vertical unit, which is what makes a fragment inside a
            // scaled form XObject report the size it visually appears at.
            var scale = Math.Sqrt(Math.Abs(state.Ctm[0] * state.Ctm[3] - state.Ctm[1] * state.Ctm[2]));
            if (scale <= 0 || double.IsNaN(scale))
            {
                scale = 1;
            }

            fragments.Add(new TextFragment(
                builder.ToString(),
                x,
                y,
                Math.Abs(x2 - x),
                state.FontSize * scale * Math.Abs(state.TextMatrix[3] == 0 ? 1 : state.TextMatrix[3]),
                font.BaseFont));
        }

        state.TextMatrix = Multiply([1, 0, 0, 1, totalAdvance, 0], state.TextMatrix);
    }

    private static (double X, double Y) Transform(double[] textMatrix, double[] ctm, double x, double y)
    {
        var tx = textMatrix[0] * x + textMatrix[2] * y + textMatrix[4];
        var ty = textMatrix[1] * x + textMatrix[3] * y + textMatrix[5];

        return (ctm[0] * tx + ctm[2] * ty + ctm[4], ctm[1] * tx + ctm[3] * ty + ctm[5]);
    }

    private static double[] Multiply(double[] a, double[] b) =>
    [
        a[0] * b[0] + a[1] * b[2],
        a[0] * b[1] + a[1] * b[3],
        a[2] * b[0] + a[3] * b[2],
        a[2] * b[1] + a[3] * b[3],
        a[4] * b[0] + a[5] * b[2] + b[4],
        a[4] * b[1] + a[5] * b[3] + b[5],
    ];

    /// <summary>
    /// Groups fragments into lines and joins them with inferred spaces.
    /// </summary>
    public static string Assemble(IReadOnlyList<TextFragment> fragments)
    {
        ArgumentNullException.ThrowIfNull(fragments);

        if (fragments.Count == 0)
        {
            return string.Empty;
        }

        // Group by baseline. A tolerance proportional to the font size is what handles a line that
        // mixes 10 pt body text with an 8 pt footnote marker without splitting it in two.
        var lines = new List<List<TextFragment>>();

        foreach (var fragment in fragments.OrderByDescending(f => f.Y).ThenBy(f => f.X))
        {
            var tolerance = Math.Max(1.5, fragment.FontSize * 0.35);
            var line = lines.FirstOrDefault(l => Math.Abs(l[0].Y - fragment.Y) <= tolerance);

            if (line is null)
            {
                lines.Add([fragment]);
            }
            else
            {
                line.Add(fragment);
            }
        }

        var builder = new StringBuilder();

        foreach (var line in lines)
        {
            line.Sort((a, b) => a.X.CompareTo(b.X));

            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            for (var i = 0; i < line.Count; i++)
            {
                if (i > 0)
                {
                    var gap = line[i].X - line[i - 1].Right;

                    // A gap wider than a quarter of the font size is a space the producer never
                    // wrote. Smaller gaps are kerning. A negative gap means overlapping runs, which
                    // is how bold-by-overprinting is done and must not become a space.
                    var threshold = Math.Max(1.0, line[i].FontSize * 0.25);

                    if (gap > threshold && !line[i - 1].Text.EndsWith(' ') &&
                        !line[i].Text.StartsWith(' '))
                    {
                        builder.Append(' ');
                    }
                }

                builder.Append(line[i].Text);
            }
        }

        return builder.ToString();
    }
}

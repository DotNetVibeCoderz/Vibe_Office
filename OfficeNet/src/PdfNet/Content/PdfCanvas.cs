// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using OfficeNet.Core.Drawing;
using PdfNet.Document;
using PdfNet.Objects;

namespace PdfNet.Content;

/// <summary>How text is aligned inside a box.</summary>
public enum TextAlignment
{
    /// <summary>Aligned to the left edge.</summary>
    Left,

    /// <summary>Centred.</summary>
    Center,

    /// <summary>Aligned to the right edge.</summary>
    Right,

    /// <summary>Stretched to both edges by widening the spaces.</summary>
    Justify,
}

/// <summary>
/// A drawing surface over a PDF page: paths, text, and images, in page coordinates.
/// </summary>
/// <remarks>
/// <para>
/// The canvas writes content-stream operators. Everything it emits is appended to the page when the
/// canvas is disposed, so a canvas is a unit of work — <c>using var canvas = page.OpenCanvas();</c>
/// and the drawing lands in one stream.
/// </para>
/// <para>
/// Coordinates are PDF user space: origin bottom-left, y increasing upwards, units of 1/72 inch.
/// <see cref="TopDown"/> flips that for callers converting from a top-down layout such as
/// WordprocessingML, which is every caller inside OfficeNet.
/// </para>
/// </remarks>
public sealed class PdfCanvas : IDisposable
{
    private readonly PdfPage _page;
    private readonly StringBuilder _content = new(4096);
    private readonly Dictionary<string, PdfObject> _fonts = [];
    private readonly Dictionary<string, PdfObject> _xobjects = [];
    private readonly Dictionary<string, PdfObject> _extGStates = [];
    private readonly Dictionary<StandardFont, string> _standardFontNames = [];
    private readonly Dictionary<string, string> _imageNames = [];
    private int _nextResourceId = 1;
    private bool _inText;
    private bool _disposed;
    private StandardFont _currentFont = StandardFont.Helvetica;
    private double _currentFontSize = 11;
    private string? _currentFontResource;

    internal PdfCanvas(PdfPage page)
    {
        _page = page;
        PageHeight = page.MediaBox.Height;
    }

    /// <summary>The page's height, used to flip coordinates when <see cref="TopDown"/> is set.</summary>
    public double PageHeight { get; }

    /// <summary>
    /// When true, y coordinates are measured downwards from the top of the page.
    /// </summary>
    /// <remarks>
    /// Every OfficeNet exporter sets this. WordprocessingML, SpreadsheetML and PresentationML all
    /// measure from the top; converting each coordinate at the call site rather than once here is
    /// how a converter ends up flipping some elements and not others.
    /// </remarks>
    public bool TopDown { get; set; }

    private double Y(double y) => TopDown ? PageHeight - y : y;

    // ---- Graphics state -------------------------------------------------------------------------

    /// <summary>Saves the graphics state.</summary>
    public PdfCanvas Save()
    {
        EndText();
        _content.Append("q\n");
        return this;
    }

    /// <summary>Restores the graphics state.</summary>
    public PdfCanvas Restore()
    {
        EndText();
        _content.Append("Q\n");
        return this;
    }

    /// <summary>Concatenates a transformation matrix.</summary>
    public PdfCanvas Transform(double a, double b, double c, double d, double e, double f)
    {
        EndText();
        _content.Append(CultureInfo.InvariantCulture,
            $"{N(a)} {N(b)} {N(c)} {N(d)} {N(e)} {N(f)} cm\n");
        return this;
    }

    /// <summary>Translates the coordinate system.</summary>
    public PdfCanvas Translate(double dx, double dy) => Transform(1, 0, 0, 1, dx, TopDown ? -dy : dy);

    /// <summary>Scales the coordinate system.</summary>
    public PdfCanvas Scale(double sx, double sy) => Transform(sx, 0, 0, sy, 0, 0);

    /// <summary>Rotates the coordinate system, in degrees anticlockwise.</summary>
    public PdfCanvas Rotate(double degrees)
    {
        var radians = degrees * Math.PI / 180;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        return Transform(cos, sin, -sin, cos, 0, 0);
    }

    /// <summary>Sets the fill colour.</summary>
    public PdfCanvas SetFillColor(OfficeColor color)
    {
        EndTextIfPathPending();
        _content.Append(CultureInfo.InvariantCulture,
            $"{N(color.R / 255.0)} {N(color.G / 255.0)} {N(color.B / 255.0)} rg\n");
        return this;
    }

    /// <summary>Sets the stroke colour.</summary>
    public PdfCanvas SetStrokeColor(OfficeColor color)
    {
        EndTextIfPathPending();
        _content.Append(CultureInfo.InvariantCulture,
            $"{N(color.R / 255.0)} {N(color.G / 255.0)} {N(color.B / 255.0)} RG\n");
        return this;
    }

    /// <summary>Sets the line width in points.</summary>
    public PdfCanvas SetLineWidth(double width)
    {
        _content.Append(CultureInfo.InvariantCulture, $"{N(width)} w\n");
        return this;
    }

    /// <summary>Sets the line cap style: 0 butt, 1 round, 2 square.</summary>
    public PdfCanvas SetLineCap(int style)
    {
        _content.Append(CultureInfo.InvariantCulture, $"{style} J\n");
        return this;
    }

    /// <summary>Sets the line join style: 0 mitre, 1 round, 2 bevel.</summary>
    public PdfCanvas SetLineJoin(int style)
    {
        _content.Append(CultureInfo.InvariantCulture, $"{style} j\n");
        return this;
    }

    /// <summary>Sets a dash pattern; an empty array restores a solid line.</summary>
    public PdfCanvas SetDash(double[] pattern, double phase = 0)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        _content.Append(CultureInfo.InvariantCulture,
            $"[{string.Join(' ', pattern.Select(N))}] {N(phase)} d\n");
        return this;
    }

    /// <summary>
    /// Sets constant alpha for fills and strokes, between 0 (invisible) and 1 (opaque).
    /// </summary>
    /// <remarks>
    /// Alpha is not a graphics operator; it lives in an extended graphics state dictionary that has
    /// to be registered as a page resource and then selected by name. That indirection is why a
    /// watermark drawn with a "transparent grey" often comes out opaque in a hand-written PDF.
    /// </remarks>
    public PdfCanvas SetOpacity(double alpha)
    {
        alpha = Math.Clamp(alpha, 0, 1);

        var key = $"ca{alpha:0.###}";
        if (!_extGStates.TryGetValue(key, out _))
        {
            var state = new PdfDictionary();
            state.SetName(PdfName.Type, "ExtGState");
            state.Set(PdfName.Get("ca"), alpha);
            state.Set(PdfName.Get("CA"), alpha);
            _extGStates[key] = state;
        }

        EndText();
        _content.Append(CultureInfo.InvariantCulture, $"/{key} gs\n");
        return this;
    }

    // ---- Paths ---------------------------------------------------------------------------------

    /// <summary>Starts a subpath.</summary>
    public PdfCanvas MoveTo(double x, double y)
    {
        EndText();
        _content.Append(CultureInfo.InvariantCulture, $"{N(x)} {N(Y(y))} m\n");
        return this;
    }

    /// <summary>Adds a straight segment.</summary>
    public PdfCanvas LineTo(double x, double y)
    {
        _content.Append(CultureInfo.InvariantCulture, $"{N(x)} {N(Y(y))} l\n");
        return this;
    }

    /// <summary>Adds a cubic Bézier segment.</summary>
    public PdfCanvas CurveTo(double x1, double y1, double x2, double y2, double x3, double y3)
    {
        _content.Append(CultureInfo.InvariantCulture,
            $"{N(x1)} {N(Y(y1))} {N(x2)} {N(Y(y2))} {N(x3)} {N(Y(y3))} c\n");
        return this;
    }

    /// <summary>Closes the current subpath.</summary>
    public PdfCanvas ClosePath()
    {
        _content.Append("h\n");
        return this;
    }

    /// <summary>Adds a rectangle as a complete subpath.</summary>
    public PdfCanvas Rectangle(double x, double y, double width, double height)
    {
        EndText();

        // In top-down mode the caller's (x, y) is the top-left corner, so the rectangle's origin
        // is one height further down the flipped axis.
        var bottom = TopDown ? Y(y + height) : y;
        _content.Append(CultureInfo.InvariantCulture,
            $"{N(x)} {N(bottom)} {N(width)} {N(height)} re\n");
        return this;
    }

    /// <summary>Adds a rounded rectangle.</summary>
    public PdfCanvas RoundedRectangle(double x, double y, double width, double height, double radius)
    {
        radius = Math.Min(radius, Math.Min(width, height) / 2);

        // A quarter circle is approximated by a Bézier whose control points sit this fraction of
        // the radius along the tangents. The constant is 4/3 * (sqrt(2) - 1).
        const double K = 0.5522847498307936;
        var handle = radius * K;

        MoveTo(x + radius, y);
        LineTo(x + width - radius, y);
        CurveTo(x + width - radius + handle, y, x + width, y + radius - handle, x + width, y + radius);
        LineTo(x + width, y + height - radius);
        CurveTo(x + width, y + height - radius + handle, x + width - radius + handle, y + height,
            x + width - radius, y + height);
        LineTo(x + radius, y + height);
        CurveTo(x + radius - handle, y + height, x, y + height - radius + handle, x, y + height - radius);
        LineTo(x, y + radius);
        CurveTo(x, y + radius - handle, x + radius - handle, y, x + radius, y);
        ClosePath();
        return this;
    }

    /// <summary>Adds an ellipse inscribed in a rectangle.</summary>
    public PdfCanvas Ellipse(double x, double y, double width, double height)
    {
        const double K = 0.5522847498307936;
        var rx = width / 2;
        var ry = height / 2;
        var cx = x + rx;
        var cy = y + ry;
        var hx = rx * K;
        var hy = ry * K;

        MoveTo(cx - rx, cy);
        CurveTo(cx - rx, cy + hy, cx - hx, cy + ry, cx, cy + ry);
        CurveTo(cx + hx, cy + ry, cx + rx, cy + hy, cx + rx, cy);
        CurveTo(cx + rx, cy - hy, cx + hx, cy - ry, cx, cy - ry);
        CurveTo(cx - hx, cy - ry, cx - rx, cy - hy, cx - rx, cy);
        ClosePath();
        return this;
    }

    /// <summary>Adds a circle.</summary>
    public PdfCanvas Circle(double centerX, double centerY, double radius) =>
        Ellipse(centerX - radius, centerY - radius, radius * 2, radius * 2);

    /// <summary>Fills the current path.</summary>
    public PdfCanvas Fill()
    {
        _content.Append("f\n");
        return this;
    }

    /// <summary>Strokes the current path.</summary>
    public PdfCanvas Stroke()
    {
        _content.Append("S\n");
        return this;
    }

    /// <summary>Fills and strokes the current path.</summary>
    public PdfCanvas FillAndStroke()
    {
        _content.Append("B\n");
        return this;
    }

    /// <summary>Clips subsequent drawing to the current path.</summary>
    public PdfCanvas Clip()
    {
        // W sets the clip and n ends the path without painting it. Omitting n leaves the path
        // pending and the next operator applies to it.
        _content.Append("W n\n");
        return this;
    }

    /// <summary>Draws a line between two points.</summary>
    public PdfCanvas DrawLine(double x1, double y1, double x2, double y2) =>
        MoveTo(x1, y1).LineTo(x2, y2).Stroke();

    /// <summary>Fills a rectangle with a colour.</summary>
    public PdfCanvas FillRectangle(double x, double y, double width, double height, OfficeColor color) =>
        SetFillColor(color).Rectangle(x, y, width, height).Fill();

    // ---- Text ----------------------------------------------------------------------------------

    /// <summary>Selects one of the standard 14 fonts.</summary>
    public PdfCanvas SetFont(StandardFont font, double size)
    {
        _currentFont = font;
        _currentFontSize = size;
        _currentFontResource = EnsureStandardFont(font);

        if (_inText)
        {
            _content.Append(CultureInfo.InvariantCulture, $"/{_currentFontResource} {N(size)} Tf\n");
        }

        return this;
    }

    /// <summary>Selects a standard font by family name and style.</summary>
    public PdfCanvas SetFont(string familyName, double size, bool bold = false, bool italic = false) =>
        SetFont(StandardFonts.Match(familyName, bold, italic), size);

    /// <summary>Draws a single line of text with its left end at the baseline point.</summary>
    public PdfCanvas DrawText(string text, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length == 0)
        {
            return this;
        }

        BeginText();
        _content.Append(CultureInfo.InvariantCulture, $"1 0 0 1 {N(x)} {N(Y(y))} Tm\n");
        _content.Append(EscapeString(StandardFonts.EncodeWinAnsi(text))).Append(" Tj\n");
        return this;
    }

    /// <summary>Draws text aligned inside a horizontal span.</summary>
    public PdfCanvas DrawText(string text, double x, double y, double width, TextAlignment alignment)
    {
        ArgumentNullException.ThrowIfNull(text);

        var textWidth = MeasureText(text);

        var offset = alignment switch
        {
            TextAlignment.Center => (width - textWidth) / 2,
            TextAlignment.Right => width - textWidth,
            _ => 0,
        };

        return DrawText(text, x + Math.Max(0, offset), y);
    }

    /// <summary>The width of a string in points at the current font and size.</summary>
    public double MeasureText(string text) =>
        StandardFonts.MeasurePoints(_currentFont, text, _currentFontSize);

    /// <summary>
    /// Draws wrapped text inside a box and returns the height it used.
    /// </summary>
    /// <param name="text">The text; existing newlines are honoured as hard breaks.</param>
    /// <param name="x">The box's left edge.</param>
    /// <param name="y">The box's top edge when <see cref="TopDown"/>, otherwise its baseline start.</param>
    /// <param name="width">The wrapping width in points.</param>
    /// <param name="lineHeight">Line spacing in points; defaults to 1.2 times the font size.</param>
    /// <param name="alignment">How each line is aligned in the box.</param>
    public double DrawWrappedText(string text, double x, double y, double width,
        double lineHeight = 0, TextAlignment alignment = TextAlignment.Left)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (lineHeight <= 0)
        {
            lineHeight = _currentFontSize * 1.2;
        }

        var lines = WrapText(text, width);
        var cursor = y;

        // The first baseline sits one ascent below the box top, not at the top itself — otherwise
        // the first line is drawn above the box.
        var ascent = _currentFontSize * 0.8;

        foreach (var line in lines)
        {
            var baseline = TopDown ? cursor + ascent : cursor;

            if (alignment == TextAlignment.Justify && line.CanJustify)
            {
                DrawJustifiedLine(line.Text, x, baseline, width);
            }
            else
            {
                DrawText(line.Text, x, baseline, width, alignment);
            }

            cursor += TopDown ? lineHeight : -lineHeight;
        }

        return lines.Count * lineHeight;
    }

    private void DrawJustifiedLine(string line, double x, double baseline, double width)
    {
        var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (words.Length <= 1)
        {
            DrawText(line, x, baseline);
            return;
        }

        var wordsWidth = words.Sum(MeasureText);
        var extra = (width - wordsWidth) / (words.Length - 1);

        // A negative gap means the line already overflows; falling back to a plain space stops the
        // words overlapping.
        if (extra < 0)
        {
            DrawText(line, x, baseline);
            return;
        }

        var cursor = x;
        foreach (var word in words)
        {
            DrawText(word, cursor, baseline);
            cursor += MeasureText(word) + extra;
        }
    }

    private readonly record struct WrappedLine(string Text, bool CanJustify);

    private List<WrappedLine> WrapText(string text, double width)
    {
        var result = new List<WrappedLine>();

        foreach (var paragraph in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (paragraph.Length == 0)
            {
                result.Add(new WrappedLine(string.Empty, false));
                continue;
            }

            var words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var current = new StringBuilder();

            foreach (var word in words)
            {
                var candidate = current.Length == 0 ? word : $"{current} {word}";

                if (MeasureText(candidate) <= width || current.Length == 0)
                {
                    current.Clear().Append(candidate);
                    continue;
                }

                result.Add(new WrappedLine(current.ToString(), true));
                current.Clear().Append(word);
            }

            if (current.Length > 0)
            {
                // The last line of a paragraph is never justified — that is what makes justified
                // text look like typesetting rather than like a bug.
                result.Add(new WrappedLine(current.ToString(), false));
            }
        }

        return result;
    }

    private void BeginText()
    {
        if (_inText)
        {
            return;
        }

        _content.Append("BT\n");
        _inText = true;

        _currentFontResource ??= EnsureStandardFont(_currentFont);
        _content.Append(CultureInfo.InvariantCulture,
            $"/{_currentFontResource} {N(_currentFontSize)} Tf\n");
    }

    private void EndText()
    {
        if (!_inText)
        {
            return;
        }

        _content.Append("ET\n");
        _inText = false;
    }

    private void EndTextIfPathPending()
    {
        // A colour change inside a text object is legal, so this deliberately does not end it.
    }

    // ---- Images --------------------------------------------------------------------------------

    /// <summary>Draws an image into a rectangle.</summary>
    /// <param name="imageBytes">A PNG, JPEG or BMP file's bytes.</param>
    /// <param name="x">The left edge.</param>
    /// <param name="y">The top edge when <see cref="TopDown"/> is set, otherwise the bottom.</param>
    /// <param name="width">The drawn width in points.</param>
    /// <param name="height">The drawn height in points.</param>
    public PdfCanvas DrawImage(byte[] imageBytes, double x, double y, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);

        var name = EnsureImage(imageBytes);
        EndText();

        // An image XObject draws into the unit square, so the transform is what gives it a size and
        // a position. The y offset is the image's bottom edge.
        var bottom = TopDown ? Y(y + height) : y;

        _content.Append("q\n");
        _content.Append(CultureInfo.InvariantCulture,
            $"{N(width)} 0 0 {N(height)} {N(x)} {N(bottom)} cm\n");
        _content.Append(CultureInfo.InvariantCulture, $"/{name} Do\n");
        _content.Append("Q\n");

        return this;
    }

    /// <summary>Draws an image at its natural size in points.</summary>
    public PdfCanvas DrawImage(byte[] imageBytes, double x, double y)
    {
        var info = ImageInfo.Read(imageBytes);
        return DrawImage(imageBytes, x, y, info.NaturalWidth.Points, info.NaturalHeight.Points);
    }

    // ---- Resources -----------------------------------------------------------------------------

    private string EnsureStandardFont(StandardFont font)
    {
        if (_standardFontNames.TryGetValue(font, out var existing))
        {
            return existing;
        }

        var name = "F" + _nextResourceId++;
        _fonts[name] = _page.Document.AddObject(StandardFonts.CreateFontDictionary(font));
        _standardFontNames[font] = name;
        return name;
    }

    private string EnsureImage(byte[] imageBytes)
    {
        // Deduplicating by content hash matters more here than anywhere else: a header logo drawn
        // on every page of a 200-page report is one XObject, not two hundred.
        var hash = Convert.ToHexString(SHA256.HashData(imageBytes));

        if (_imageNames.TryGetValue(hash, out var existing))
        {
            return existing;
        }

        var name = "Im" + _nextResourceId++;
        var xobject = PdfImageBuilder.Build(imageBytes, _page.Document);
        _xobjects[name] = _page.Document.AddObject(xobject);
        _imageNames[hash] = name;
        return name;
    }

    private static string N(double value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);

    private static string EscapeString(byte[] bytes)
    {
        var builder = new StringBuilder(bytes.Length + 2);
        builder.Append('(');

        foreach (var b in bytes)
        {
            switch (b)
            {
                case (byte)'(':
                case (byte)')':
                case (byte)'\\':
                    builder.Append('\\').Append((char)b);
                    break;
                case (byte)'\r':
                    builder.Append("\\r");
                    break;
                case (byte)'\n':
                    builder.Append("\\n");
                    break;
                default:
                    builder.Append((char)b);
                    break;
            }
        }

        builder.Append(')');
        return builder.ToString();
    }

    /// <summary>Appends the drawing to the page and merges the resources it needs.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        EndText();

        if (_content.Length == 0)
        {
            return;
        }

        MergeResource(PdfName.Font, _fonts);
        MergeResource(PdfName.XObject, _xobjects);
        MergeResource(PdfName.Get("ExtGState"), _extGStates);

        // The whole drawing is wrapped in q/Q so it cannot leak graphics state into whatever the
        // page already had, or inherit an unbalanced state from it.
        _page.AppendContent(Encoding.Latin1.GetBytes("q\n" + _content + "Q\n"));
    }

    private void MergeResource(PdfName category, Dictionary<string, PdfObject> entries)
    {
        if (entries.Count == 0)
        {
            return;
        }

        var resources = _page.Resources;

        if (resources.Get(category) is not PdfDictionary target)
        {
            target = new PdfDictionary();
            resources[category] = target;
        }

        foreach (var (name, value) in entries)
        {
            target[PdfName.Get(name)] = value;
        }
    }
}

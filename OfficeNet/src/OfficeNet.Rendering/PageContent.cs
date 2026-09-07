// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Globalization;
using System.Text;
using OfficeNet.Core;
using PdfNet.Document;
using PdfNet.Text;
using SkiaSharp;

namespace OfficeNet.Rendering;

/// <summary>A filled and/or stroked path, in PDF user space.</summary>
internal sealed class PaintedPath(SKPath path, SKColor? fill, SKColor? stroke, float lineWidth, bool evenOdd)
    : IDisposable
{
    public SKPath Path { get; } = path;

    public SKColor? Fill { get; } = fill;

    public SKColor? Stroke { get; } = stroke;

    public float LineWidth { get; } = lineWidth;

    public bool EvenOdd { get; } = evenOdd;

    public void Dispose() => Path.Dispose();
}

/// <summary>An image placed by the content stream, with the matrix that positions it.</summary>
/// <remarks>
/// The matrix maps the PDF image space — the unit square, origin bottom-left — onto user space.
/// That is the whole placement: a PDF never stores an image's position on the image itself.
/// </remarks>
internal readonly record struct PlacedImage(string Name, SKMatrix Matrix);

/// <summary>The fill colour in effect where a text-showing operator began.</summary>
internal readonly record struct TextColorMark(double X, double Y, SKColor Color);

/// <summary>
/// What the renderer needs from a page's content stream: paths, image placements, and the colour
/// each text run was drawn in.
/// </summary>
/// <remarks>
/// <para>
/// Glyphs are deliberately absent. Turning the bytes of a <c>Tj</c> into characters means resolving
/// the font's encoding, its <c>ToUnicode</c> CMap and its widths — which
/// <see cref="TextExtractor"/> already does well. Duplicating it here to gain a colour would be a
/// second implementation of the hardest part of text extraction; instead the position of each run
/// is recorded and the extractor's fragments are matched to it.
/// </para>
/// <para>
/// Everything is reported in PDF user space, with the CTM already applied. The caller owns the
/// flip to device space, so this type stays independent of output resolution.
/// </para>
/// </remarks>
internal sealed class PageContent : IDisposable
{
    private readonly List<PaintedPath> _paths = [];
    private readonly List<PlacedImage> _images = [];
    private readonly List<TextColorMark> _textColors = [];

    private PageContent()
    {
    }

    public IReadOnlyList<PaintedPath> Paths => _paths;

    public IReadOnlyList<PlacedImage> Images => _images;

    public IReadOnlyList<TextColorMark> TextColors => _textColors;

    public void Dispose()
    {
        foreach (var path in _paths)
        {
            path.Dispose();
        }

        _paths.Clear();
    }

    /// <summary>
    /// Interprets a page's content stream, returning empty content when it cannot be read.
    /// </summary>
    public static PageContent Read(PdfPage page)
    {
        var content = new PageContent();

        try
        {
            new Interpreter(content).Run(page.GetContent());
        }
        catch (OfficeNetException)
        {
            // A page this library cannot decode should degrade to "no vectors", not take the whole
            // render down: the text pass still produces a usable preview.
        }

        return content;
    }

    /// <summary>The colour of the text run nearest a point, or black when nothing was recorded.</summary>
    /// <remarks>
    /// Nearest-position matching rather than index matching, because the extractor merges adjacent
    /// runs into one fragment and the two lists therefore do not line up one to one.
    /// </remarks>
    public SKColor ColorNear(double x, double y)
    {
        var best = SKColors.Black;
        var bestDistance = double.MaxValue;

        foreach (var mark in _textColors)
        {
            // Squared distance: the comparison is all that matters, so the square root is waste.
            var dx = mark.X - x;
            var dy = mark.Y - y;
            var distance = (dx * dx) + (dy * dy);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = mark.Color;
            }
        }

        // Beyond a few points the nearest run is a different line, and inheriting its colour would
        // be a guess. Black is the PDF default and the safer answer.
        return bestDistance <= 4 * 4 ? best : SKColors.Black;
    }

    private sealed class Interpreter(PageContent output)
    {
        private readonly List<double> _numbers = [];
        private readonly Stack<State> _stack = new();
        private readonly List<string> _pendingNames = [];

        private State _state = State.Initial;
        private SKPathBuilder? _builder;
        private SKMatrix _textMatrix = SKMatrix.CreateIdentity();
        private SKMatrix _lineMatrix = SKMatrix.CreateIdentity();
        private double _leading;

        // The current point is tracked separately because PDF's v and y curve operators are defined
        // in terms of it and SKPath does not expose it before the first point is added.
        private float _x;
        private float _y;
        private float _startX;
        private float _startY;
        private bool _hasCurrent;

        public void Run(byte[] content)
        {
            var lexer = new Lexer(content);

            while (lexer.Next() is { } token)
            {
                switch (token.Kind)
                {
                    case TokenKind.Number:
                        _numbers.Add(token.Number);
                        break;

                    case TokenKind.Name:
                        _pendingNames.Add(token.Text!);
                        break;

                    case TokenKind.Operator:
                        Execute(token.Text!);
                        _numbers.Clear();
                        _pendingNames.Clear();
                        break;

                    default:
                        // Strings, arrays and dictionaries are consumed by the lexer and carry
                        // nothing this interpreter needs.
                        break;
                }
            }
        }

        private double Number(int indexFromEnd) =>
            _numbers.Count > indexFromEnd ? _numbers[^(indexFromEnd + 1)] : 0;

        private void Execute(string op)
        {
            switch (op)
            {
                // ---- Graphics state ----------------------------------------------------------
                case "q":
                    _stack.Push(_state);
                    break;

                case "Q":
                    if (_stack.Count > 0)
                    {
                        _state = _stack.Pop();
                    }

                    break;

                case "cm" when _numbers.Count >= 6:
                    _state.Ctm = SKMatrix.Concat(_state.Ctm, Matrix(
                        Number(5), Number(4), Number(3), Number(2), Number(1), Number(0)));
                    break;

                case "w":
                    _state.LineWidth = (float)Number(0);
                    break;

                // ---- Colour ------------------------------------------------------------------
                case "g":
                    _state.Fill = Gray(Number(0));
                    break;

                case "G":
                    _state.Stroke = Gray(Number(0));
                    break;

                case "rg" when _numbers.Count >= 3:
                    _state.Fill = Rgb(Number(2), Number(1), Number(0));
                    break;

                case "RG" when _numbers.Count >= 3:
                    _state.Stroke = Rgb(Number(2), Number(1), Number(0));
                    break;

                case "k" when _numbers.Count >= 4:
                    _state.Fill = Cmyk(Number(3), Number(2), Number(1), Number(0));
                    break;

                case "K" when _numbers.Count >= 4:
                    _state.Stroke = Cmyk(Number(3), Number(2), Number(1), Number(0));
                    break;

                case "sc" or "scn":
                    if (Components() is { } fill)
                    {
                        _state.Fill = fill;
                    }

                    break;

                case "SC" or "SCN":
                    if (Components() is { } stroke)
                    {
                        _state.Stroke = stroke;
                    }

                    break;

                // ---- Path construction -------------------------------------------------------
                case "m" when _numbers.Count >= 2:
                    MoveTo((float)Number(1), (float)Number(0));
                    break;

                case "l" when _numbers.Count >= 2:
                    LineTo((float)Number(1), (float)Number(0));
                    break;

                case "c" when _numbers.Count >= 6:
                    CurveTo(
                        (float)Number(5), (float)Number(4),
                        (float)Number(3), (float)Number(2),
                        (float)Number(1), (float)Number(0));
                    break;

                case "v" when _numbers.Count >= 4:
                    // The first control point is the current point.
                    CurveTo(_x, _y, (float)Number(3), (float)Number(2), (float)Number(1), (float)Number(0));
                    break;

                case "y" when _numbers.Count >= 4:
                    // The second control point is the endpoint.
                    CurveTo(
                        (float)Number(3), (float)Number(2),
                        (float)Number(1), (float)Number(0),
                        (float)Number(1), (float)Number(0));
                    break;

                case "h":
                    ClosePath();
                    break;

                case "re" when _numbers.Count >= 4:
                    Rectangle(
                        (float)Number(3), (float)Number(2), (float)Number(1), (float)Number(0));
                    break;

                // ---- Path painting -----------------------------------------------------------
                case "f" or "F" or "f*":
                    Paint(filled: true, stroked: false, evenOdd: op == "f*");
                    break;

                case "S":
                    Paint(filled: false, stroked: true, evenOdd: false);
                    break;

                case "s":
                    ClosePath();
                    Paint(filled: false, stroked: true, evenOdd: false);
                    break;

                case "B" or "B*":
                    Paint(filled: true, stroked: true, evenOdd: op == "B*");
                    break;

                case "b" or "b*":
                    ClosePath();
                    Paint(filled: true, stroked: true, evenOdd: op == "b*");
                    break;

                case "n":
                    // A path used only to set a clip. Clipping is not applied, so it is discarded —
                    // drawing it would put a stray outline on the page.
                    Paint(filled: false, stroked: false, evenOdd: false);
                    break;

                // ---- Text --------------------------------------------------------------------
                case "BT":
                    _textMatrix = SKMatrix.CreateIdentity();
                    _lineMatrix = _textMatrix;
                    break;

                case "TL":
                    _leading = Number(0);
                    break;

                case "Td" when _numbers.Count >= 2:
                    TranslateLine(Number(1), Number(0));
                    break;

                case "TD" when _numbers.Count >= 2:
                    _leading = -Number(0);
                    TranslateLine(Number(1), Number(0));
                    break;

                case "Tm" when _numbers.Count >= 6:
                    _lineMatrix = Matrix(
                        Number(5), Number(4), Number(3), Number(2), Number(1), Number(0));
                    _textMatrix = _lineMatrix;
                    break;

                case "T*":
                    TranslateLine(0, -_leading);
                    break;

                case "Tj" or "TJ":
                    MarkText();
                    break;

                case "'":
                    TranslateLine(0, -_leading);
                    MarkText();
                    break;

                case "\"":
                    TranslateLine(0, -_leading);
                    MarkText();
                    break;

                // ---- XObjects ----------------------------------------------------------------
                case "Do" when _pendingNames.Count > 0:
                    output._images.Add(new PlacedImage(_pendingNames[^1], _state.Ctm));
                    break;

                default:
                    break;
            }
        }

        private SKColor? Components() => _numbers.Count switch
        {
            1 => Gray(Number(0)),
            3 => Rgb(Number(2), Number(1), Number(0)),
            4 => Cmyk(Number(3), Number(2), Number(1), Number(0)),

            // A pattern or an ICC space with a component count this renderer does not model.
            // Leaving the colour alone beats painting an arbitrary one.
            _ => null,
        };

        private void MarkText()
        {
            var origin = SKMatrix.Concat(_state.Ctm, _textMatrix).MapPoint(0, 0);
            output._textColors.Add(new TextColorMark(origin.X, origin.Y, _state.Fill));
        }

        private void TranslateLine(double tx, double ty)
        {
            _lineMatrix = SKMatrix.Concat(_lineMatrix, SKMatrix.CreateTranslation((float)tx, (float)ty));
            _textMatrix = _lineMatrix;
        }

        private SKPathBuilder Builder() => _builder ??= new SKPathBuilder();

        private SKPoint Map(float x, float y) => _state.Ctm.MapPoint(x, y);

        private void MoveTo(float x, float y)
        {
            var p = Map(x, y);
            Builder().MoveTo(p);
            _x = x;
            _y = y;
            _startX = x;
            _startY = y;
            _hasCurrent = true;
        }

        private void LineTo(float x, float y)
        {
            if (!_hasCurrent)
            {
                MoveTo(x, y);
                return;
            }

            Builder().LineTo(Map(x, y));
            _x = x;
            _y = y;
        }

        private void CurveTo(float x1, float y1, float x2, float y2, float x3, float y3)
        {
            if (!_hasCurrent)
            {
                MoveTo(x1, y1);
            }

            Builder().CubicTo(Map(x1, y1), Map(x2, y2), Map(x3, y3));
            _x = x3;
            _y = y3;
        }

        private void ClosePath()
        {
            if (_hasCurrent)
            {
                Builder().Close();
                _x = _startX;
                _y = _startY;
            }
        }

        private void Rectangle(float x, float y, float width, float height)
        {
            var builder = Builder();
            builder.MoveTo(Map(x, y));
            builder.LineTo(Map(x + width, y));
            builder.LineTo(Map(x + width, y + height));
            builder.LineTo(Map(x, y + height));
            builder.Close();

            _x = x;
            _y = y;
            _startX = x;
            _startY = y;
            _hasCurrent = true;
        }

        private void Paint(bool filled, bool stroked, bool evenOdd)
        {
            var builder = _builder;
            _builder = null;
            _hasCurrent = false;

            if (builder is null)
            {
                return;
            }

            using (builder)
            {
                if (!filled && !stroked)
                {
                    return;
                }

                Emit(builder.Detach(), filled, stroked, evenOdd);
            }
        }

        private void Emit(SKPath path, bool filled, bool stroked, bool evenOdd)
        {

            // The line width is in user space, so a scaled CTM scales it too. The geometric mean of
            // the axis scales is the usual approximation for a non-uniform matrix.
            var scale = Math.Sqrt(Math.Abs(
                (_state.Ctm.ScaleX * _state.Ctm.ScaleY) - (_state.Ctm.SkewX * _state.Ctm.SkewY)));

            var width = (float)Math.Max(_state.LineWidth * scale, 0.1);

            output._paths.Add(new PaintedPath(
                path,
                filled ? _state.Fill : null,
                stroked ? _state.Stroke : null,
                width,
                evenOdd));
        }

        private static SKMatrix Matrix(double a, double b, double c, double d, double e, double f) =>
            new((float)a, (float)c, (float)e, (float)b, (float)d, (float)f, 0, 0, 1);

        private static SKColor Gray(double value)
        {
            var v = Channel(value);
            return new SKColor(v, v, v);
        }

        private static SKColor Rgb(double r, double g, double b) =>
            new(Channel(r), Channel(g), Channel(b));

        private static SKColor Cmyk(double c, double m, double y, double k) => new(
            Channel((1 - c) * (1 - k)),
            Channel((1 - m) * (1 - k)),
            Channel((1 - y) * (1 - k)));

        private static byte Channel(double value) =>
            (byte)Math.Clamp(Math.Round(value * 255), 0, 255);

        private struct State
        {
            public static State Initial => new()
            {
                Ctm = SKMatrix.CreateIdentity(),
                Fill = SKColors.Black,
                Stroke = SKColors.Black,
                LineWidth = 1,
            };

            public SKMatrix Ctm;
            public SKColor Fill;
            public SKColor Stroke;
            public float LineWidth;
        }
    }

    private enum TokenKind
    {
        Number,
        Name,
        String,
        Operator,
    }

    private readonly record struct Token(TokenKind Kind, double Number, string? Text);

    /// <summary>
    /// A content-stream tokenizer.
    /// </summary>
    /// <remarks>
    /// Strings, arrays and dictionaries are skipped rather than returned, but they still have to be
    /// scanned correctly: a <c>(</c> string may contain unbalanced-looking bytes and escapes, and
    /// treating its contents as operators is how a lexer starts executing text as code.
    /// </remarks>
    private sealed class Lexer(byte[] data)
    {
        private int _position;

        public Token? Next()
        {
            while (true)
            {
                SkipWhitespaceAndComments();

                if (_position >= data.Length)
                {
                    return null;
                }

                var c = data[_position];

                switch (c)
                {
                    case (byte)'/':
                        return new Token(TokenKind.Name, 0, ReadName());

                    case (byte)'(':
                        SkipLiteralString();
                        continue;

                    case (byte)'<':
                        if (_position + 1 < data.Length && data[_position + 1] == '<')
                        {
                            _position += 2;
                        }
                        else
                        {
                            SkipHexString();
                        }

                        continue;

                    case (byte)'>':
                        _position += _position + 1 < data.Length && data[_position + 1] == '>' ? 2 : 1;
                        continue;

                    case (byte)'[' or (byte)']' or (byte)'{' or (byte)'}':
                        _position++;
                        continue;

                    default:
                        if (IsNumberStart(c))
                        {
                            return ReadNumber();
                        }

                        return new Token(TokenKind.Operator, 0, ReadOperator());
                }
            }
        }

        private static bool IsNumberStart(byte c) =>
            c is >= (byte)'0' and <= (byte)'9' or (byte)'+' or (byte)'-' or (byte)'.';

        private static bool IsWhitespace(byte c) =>
            c is 0 or 9 or 10 or 12 or 13 or 32;

        private static bool IsDelimiter(byte c) =>
            c is (byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or (byte)'[' or (byte)']'
                or (byte)'{' or (byte)'}' or (byte)'/' or (byte)'%';

        private void SkipWhitespaceAndComments()
        {
            while (_position < data.Length)
            {
                var c = data[_position];

                if (IsWhitespace(c))
                {
                    _position++;
                }
                else if (c == '%')
                {
                    while (_position < data.Length && data[_position] is not 10 and not 13)
                    {
                        _position++;
                    }
                }
                else
                {
                    return;
                }
            }
        }

        private string ReadName()
        {
            _position++;
            var builder = new StringBuilder();

            while (_position < data.Length)
            {
                var c = data[_position];

                if (IsWhitespace(c) || IsDelimiter(c))
                {
                    break;
                }

                // #xx is the escape for a byte that cannot appear literally in a name.
                if (c == '#' && _position + 2 < data.Length &&
                    Uri.IsHexDigit((char)data[_position + 1]) && Uri.IsHexDigit((char)data[_position + 2]))
                {
                    builder.Append((char)Convert.ToInt32(
                        $"{(char)data[_position + 1]}{(char)data[_position + 2]}", 16));
                    _position += 3;
                    continue;
                }

                builder.Append((char)c);
                _position++;
            }

            return builder.ToString();
        }

        private Token ReadNumber()
        {
            var start = _position;

            while (_position < data.Length &&
                   (IsNumberStart(data[_position]) || data[_position] is (byte)'e' or (byte)'E'))
            {
                _position++;
            }

            var text = Encoding.ASCII.GetString(data, start, _position - start);

            // A malformed number is not worth aborting the page for; it becomes zero and the
            // operator that consumes it gets a harmless operand.
            return new Token(TokenKind.Number,
                double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                    ? value
                    : 0,
                null);
        }

        private string ReadOperator()
        {
            var start = _position;

            while (_position < data.Length &&
                   !IsWhitespace(data[_position]) && !IsDelimiter(data[_position]))
            {
                _position++;
            }

            if (_position == start)
            {
                // Not a legal operator character; step over it so the loop cannot stall.
                _position++;
                return string.Empty;
            }

            return Encoding.ASCII.GetString(data, start, _position - start);
        }

        private void SkipLiteralString()
        {
            _position++;
            var depth = 1;

            while (_position < data.Length && depth > 0)
            {
                var c = data[_position];

                if (c == '\\')
                {
                    // Skip the escape and whatever it escapes, so an escaped parenthesis does not
                    // change the nesting depth.
                    _position += 2;
                    continue;
                }

                depth += c switch
                {
                    (byte)'(' => 1,
                    (byte)')' => -1,
                    _ => 0,
                };

                _position++;
            }
        }

        private void SkipHexString()
        {
            _position++;

            while (_position < data.Length && data[_position] != '>')
            {
                _position++;
            }

            _position++;
        }
    }
}

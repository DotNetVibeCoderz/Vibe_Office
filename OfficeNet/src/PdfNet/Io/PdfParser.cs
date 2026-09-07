// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Globalization;
using System.Text;
using OfficeNet.Core;
using PdfNet.Objects;

namespace PdfNet.Io;

/// <summary>
/// A recursive-descent parser over PDF's COS syntax.
/// </summary>
/// <remarks>
/// <para>
/// PDF is a byte grammar, not a text one: a string literal can contain any byte including a null,
/// and a stream's payload is delimited by a length that may itself be an indirect reference. The
/// parser therefore works over a <see cref="byte"/> span throughout and never decodes to
/// <see cref="string"/> except for keywords.
/// </para>
/// <para>
/// It is deliberately permissive. Real PDFs violate the specification constantly — missing
/// <c>endobj</c>, a <c>/Length</c> that disagrees with where <c>endstream</c> actually is,
/// unbalanced dictionaries at the end of a truncated file. Every one of those is recovered from
/// rather than thrown on, because the alternative is refusing to open files that every other reader
/// opens.
/// </para>
/// </remarks>
public sealed class PdfParser
{
    private readonly byte[] _data;
    private readonly IPdfObjectResolver? _resolver;

    /// <summary>Creates a parser over a buffer.</summary>
    public PdfParser(byte[] data, IPdfObjectResolver? resolver = null)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        _resolver = resolver;
    }

    /// <summary>The current read position.</summary>
    public int Position { get; set; }

    /// <summary>The buffer being parsed.</summary>
    public ReadOnlySpan<byte> Data => _data;

    /// <summary>True when a byte is PDF whitespace.</summary>
    public static bool IsWhitespace(byte b) =>
        b is 0 or 9 or 10 or 12 or 13 or 32;

    /// <summary>True when a byte ends a token.</summary>
    public static bool IsDelimiter(byte b) =>
        b is (byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or (byte)'[' or (byte)']'
            or (byte)'{' or (byte)'}' or (byte)'/' or (byte)'%';

    /// <summary>Advances past whitespace and comments.</summary>
    public void SkipWhitespace()
    {
        while (Position < _data.Length)
        {
            var b = _data[Position];

            if (IsWhitespace(b))
            {
                Position++;
                continue;
            }

            // A comment runs to the end of the line and can appear anywhere a token can.
            if (b == (byte)'%')
            {
                while (Position < _data.Length && _data[Position] is not ((byte)'\r' or (byte)'\n'))
                {
                    Position++;
                }

                continue;
            }

            return;
        }
    }

    /// <summary>Peeks at the next non-whitespace byte, or -1 at the end of the buffer.</summary>
    public int PeekByte()
    {
        SkipWhitespace();
        return Position < _data.Length ? _data[Position] : -1;
    }

    /// <summary>Reads the next bare keyword (a run of regular characters).</summary>
    public string ReadKeyword()
    {
        SkipWhitespace();
        var start = Position;

        while (Position < _data.Length && !IsWhitespace(_data[Position]) && !IsDelimiter(_data[Position]))
        {
            Position++;
        }

        return Position == start ? string.Empty : Encoding.ASCII.GetString(_data, start, Position - start);
    }

    /// <summary>Consumes a keyword when it is the next token; returns false without moving otherwise.</summary>
    public bool TryConsumeKeyword(string keyword)
    {
        var saved = Position;
        if (ReadKeyword() == keyword)
        {
            return true;
        }

        Position = saved;
        return false;
    }

    /// <summary>
    /// Parses the object at the current position.
    /// </summary>
    /// <returns>The object, or <c>null</c> at the end of the buffer or on an unrecognised token.</returns>
    public PdfObject? ParseObject()
    {
        SkipWhitespace();

        if (Position >= _data.Length)
        {
            return null;
        }

        var b = _data[Position];

        switch (b)
        {
            case (byte)'/':
                return ParseName();

            case (byte)'(':
                return ParseLiteralString();

            case (byte)'[':
                return ParseArray();

            case (byte)'<':
                // "<<" opens a dictionary, a single "<" opens a hex string.
                return Position + 1 < _data.Length && _data[Position + 1] == (byte)'<'
                    ? ParseDictionaryOrStream()
                    : ParseHexString();

            case (byte)']':
            case (byte)'>':
            case (byte)'}':
            case (byte)')':
                // A stray closing delimiter means the producer wrote something malformed. Consuming
                // it lets the caller's loop make progress instead of spinning.
                Position++;
                return null;
        }

        if (b is (byte)'+' or (byte)'-' or (byte)'.' || char.IsAsciiDigit((char)b))
        {
            return ParseNumberOrReference();
        }

        var keyword = ReadKeyword();
        return keyword switch
        {
            "true" => PdfBoolean.True,
            "false" => PdfBoolean.False,
            "null" => PdfNull.Instance,
            "" => null,
            _ => null,
        };
    }

    private PdfName ParseName()
    {
        Position++; // '/'
        var builder = new StringBuilder(16);

        while (Position < _data.Length)
        {
            var b = _data[Position];

            if (IsWhitespace(b) || IsDelimiter(b))
            {
                break;
            }

            if (b == (byte)'#' && Position + 2 < _data.Length &&
                TryHex(_data[Position + 1], out var hi) && TryHex(_data[Position + 2], out var lo))
            {
                builder.Append((char)(hi << 4 | lo));
                Position += 3;
                continue;
            }

            builder.Append((char)b);
            Position++;
        }

        return PdfName.Get(builder.ToString());
    }

    private static bool TryHex(byte b, out int value)
    {
        value = b switch
        {
            >= (byte)'0' and <= (byte)'9' => b - '0',
            >= (byte)'A' and <= (byte)'F' => b - 'A' + 10,
            >= (byte)'a' and <= (byte)'f' => b - 'a' + 10,
            _ => -1,
        };

        return value >= 0;
    }

    private PdfString ParseLiteralString()
    {
        Position++; // '('
        using var buffer = new MemoryStream();
        var depth = 1;

        while (Position < _data.Length)
        {
            var b = _data[Position++];

            if (b == (byte)'\\')
            {
                if (Position >= _data.Length)
                {
                    break;
                }

                var escape = _data[Position++];
                switch (escape)
                {
                    case (byte)'n': buffer.WriteByte((byte)'\n'); break;
                    case (byte)'r': buffer.WriteByte((byte)'\r'); break;
                    case (byte)'t': buffer.WriteByte((byte)'\t'); break;
                    case (byte)'b': buffer.WriteByte(8); break;
                    case (byte)'f': buffer.WriteByte(12); break;
                    case (byte)'(': buffer.WriteByte((byte)'('); break;
                    case (byte)')': buffer.WriteByte((byte)')'); break;
                    case (byte)'\\': buffer.WriteByte((byte)'\\'); break;

                    case (byte)'\r':
                        // A backslash before a line break is a line continuation and contributes
                        // nothing; CRLF counts as one break.
                        if (Position < _data.Length && _data[Position] == (byte)'\n')
                        {
                            Position++;
                        }

                        break;

                    case (byte)'\n':
                        break;

                    case >= (byte)'0' and <= (byte)'7':
                    {
                        var value = escape - '0';
                        for (var i = 0; i < 2 && Position < _data.Length; i++)
                        {
                            var next = _data[Position];
                            if (next is < (byte)'0' or > (byte)'7')
                            {
                                break;
                            }

                            value = value * 8 + (next - '0');
                            Position++;
                        }

                        buffer.WriteByte((byte)value);
                        break;
                    }

                    default:
                        // An unknown escape means the backslash was literal.
                        buffer.WriteByte(escape);
                        break;
                }

                continue;
            }

            // Parentheses nest, and an unescaped balanced pair is legal inside a string.
            if (b == (byte)'(')
            {
                depth++;
            }
            else if (b == (byte)')')
            {
                depth--;
                if (depth == 0)
                {
                    break;
                }
            }

            buffer.WriteByte(b);
        }

        return new PdfString(buffer.ToArray());
    }

    private PdfString ParseHexString()
    {
        Position++; // '<'
        using var buffer = new MemoryStream();
        var high = -1;

        while (Position < _data.Length)
        {
            var b = _data[Position++];

            if (b == (byte)'>')
            {
                break;
            }

            if (!TryHex(b, out var digit))
            {
                continue;
            }

            if (high < 0)
            {
                high = digit;
            }
            else
            {
                buffer.WriteByte((byte)(high << 4 | digit));
                high = -1;
            }
        }

        if (high >= 0)
        {
            buffer.WriteByte((byte)(high << 4));
        }

        return new PdfString(buffer.ToArray()) { IsHex = true };
    }

    private PdfArray ParseArray()
    {
        Position++; // '['
        var array = new PdfArray();

        while (true)
        {
            SkipWhitespace();

            if (Position >= _data.Length)
            {
                break;
            }

            if (_data[Position] == (byte)']')
            {
                Position++;
                break;
            }

            var before = Position;
            var item = ParseObject();

            if (item is not null)
            {
                array.Add(item);
                continue;
            }

            // A null result that consumed nothing would loop forever on malformed input.
            if (Position == before)
            {
                Position++;
            }
        }

        return array;
    }

    private PdfObject ParseDictionaryOrStream()
    {
        Position += 2; // '<<'
        var dictionary = new PdfDictionary();

        while (true)
        {
            SkipWhitespace();

            if (Position >= _data.Length)
            {
                break;
            }

            if (_data[Position] == (byte)'>')
            {
                Position++;
                if (Position < _data.Length && _data[Position] == (byte)'>')
                {
                    Position++;
                }

                break;
            }

            if (_data[Position] != (byte)'/')
            {
                // Not a key. Skip one object's worth of input and try again — some producers write
                // a stray value inside a dictionary.
                var before = Position;
                ParseObject();
                if (Position == before)
                {
                    Position++;
                }

                continue;
            }

            var key = ParseName();
            var value = ParseObject();
            dictionary[key] = value ?? PdfNull.Instance;
        }

        // A stream keyword immediately after the dictionary makes this a stream object.
        var saved = Position;
        SkipWhitespace();

        if (!MatchesAt(Position, "stream"u8))
        {
            Position = saved;
            return dictionary;
        }

        Position += 6;

        // The specification requires CRLF or LF after "stream" — never CR alone — but files with a
        // bare CR exist, and treating its LF-less form as data shifts the whole payload by a byte.
        if (Position < _data.Length && _data[Position] == (byte)'\r')
        {
            Position++;
        }

        if (Position < _data.Length && _data[Position] == (byte)'\n')
        {
            Position++;
        }

        var start = Position;
        var declaredLength = ResolveLength(dictionary);
        var end = -1;

        if (declaredLength >= 0 && start + declaredLength <= _data.Length)
        {
            // Trust /Length only when what follows really is endstream. A wrong /Length is common
            // enough that verifying costs less than the corruption it prevents.
            var candidate = start + declaredLength;
            var probe = candidate;
            while (probe < _data.Length && IsWhitespace(_data[probe]))
            {
                probe++;
            }

            if (MatchesAt(probe, "endstream"u8))
            {
                end = candidate;
                Position = probe + 9;
            }
        }

        if (end < 0)
        {
            var found = IndexOf("endstream"u8, start);
            if (found < 0)
            {
                end = _data.Length;
                Position = _data.Length;
            }
            else
            {
                end = found;

                // The EOL before endstream belongs to the delimiter, not to the data.
                if (end > start && _data[end - 1] == (byte)'\n')
                {
                    end--;
                }

                if (end > start && _data[end - 1] == (byte)'\r')
                {
                    end--;
                }

                Position = found + 9;
            }
        }

        var payload = new byte[end - start];
        Array.Copy(_data, start, payload, 0, payload.Length);

        return new PdfStream(dictionary, payload);
    }

    private int ResolveLength(PdfDictionary dictionary)
    {
        var raw = dictionary[PdfName.Length];

        if (raw is PdfNumber direct)
        {
            return direct.IntValue;
        }

        if (raw is PdfReference reference && _resolver is not null)
        {
            // /Length as an indirect reference is legal and common in linearised files. It can only
            // be resolved when a reader is already available; during the initial xref-less scan it
            // is not, and the endstream search covers that case.
            return reference.WithResolver(_resolver).Resolve() is PdfNumber n ? n.IntValue : -1;
        }

        return -1;
    }

    private PdfObject ParseNumberOrReference()
    {
        var first = ReadNumber();

        // "12 0 R" is a reference and "12 0 obj" starts an indirect object; both begin as two
        // integers, so the decision needs two tokens of lookahead.
        if (first.IsInteger && first.LongValue >= 0)
        {
            var saved = Position;
            SkipWhitespace();

            if (Position < _data.Length && char.IsAsciiDigit((char)_data[Position]))
            {
                var second = ReadNumber();

                if (second.IsInteger && second.LongValue >= 0)
                {
                    var afterSecond = Position;
                    SkipWhitespace();

                    if (Position < _data.Length && _data[Position] == (byte)'R' &&
                        (Position + 1 >= _data.Length || IsWhitespace(_data[Position + 1]) ||
                         IsDelimiter(_data[Position + 1])))
                    {
                        Position++;
                        return new PdfReference((int)first.LongValue, (int)second.LongValue, _resolver);
                    }

                    Position = afterSecond;
                }
            }

            Position = saved;
        }

        return first;
    }

    private PdfNumber ReadNumber()
    {
        SkipWhitespace();
        var start = Position;
        var isReal = false;

        if (Position < _data.Length && _data[Position] is (byte)'+' or (byte)'-')
        {
            Position++;
        }

        while (Position < _data.Length)
        {
            var b = _data[Position];

            if (char.IsAsciiDigit((char)b))
            {
                Position++;
                continue;
            }

            if (b == (byte)'.')
            {
                isReal = true;
                Position++;
                continue;
            }

            // Some producers write a second sign mid-number ("1-2"); PDF's own grammar disallows it
            // and Acrobat reads it as the leading part.
            if (b is (byte)'-' or (byte)'+' && Position > start)
            {
                break;
            }

            break;
        }

        var text = Encoding.ASCII.GetString(_data, start, Position - start);

        if (!isReal && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
        {
            return new PdfNumber(integer);
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var real)
            ? new PdfNumber(real)
            : new PdfNumber(0L);
    }

    /// <summary>True when the buffer matches a byte sequence at a position.</summary>
    public bool MatchesAt(int position, ReadOnlySpan<byte> pattern) =>
        position >= 0 && position + pattern.Length <= _data.Length &&
        _data.AsSpan(position, pattern.Length).SequenceEqual(pattern);

    /// <summary>Finds the next occurrence of a byte sequence, or -1.</summary>
    public int IndexOf(ReadOnlySpan<byte> pattern, int from)
    {
        if (from < 0)
        {
            from = 0;
        }

        var index = _data.AsSpan(from).IndexOf(pattern);
        return index < 0 ? -1 : from + index;
    }

    /// <summary>Finds the last occurrence of a byte sequence at or before a position, or -1.</summary>
    public int LastIndexOf(ReadOnlySpan<byte> pattern, int before)
    {
        var limit = Math.Min(before, _data.Length);
        var index = _data.AsSpan(0, limit).LastIndexOf(pattern);
        return index;
    }

    /// <summary>
    /// Parses an indirect object header (<c>12 0 obj</c>) and the object that follows it.
    /// </summary>
    /// <returns>The object with its number set, or <c>null</c> when there is no valid header here.</returns>
    public PdfObject? ParseIndirectObject(out int objectNumber, out int generationNumber)
    {
        objectNumber = 0;
        generationNumber = 0;

        SkipWhitespace();
        var saved = Position;

        var numberToken = ReadKeyword();
        if (!int.TryParse(numberToken, out var number))
        {
            Position = saved;
            return null;
        }

        var generationToken = ReadKeyword();
        if (!int.TryParse(generationToken, out var generation))
        {
            Position = saved;
            return null;
        }

        if (!TryConsumeKeyword("obj"))
        {
            Position = saved;
            return null;
        }

        objectNumber = number;
        generationNumber = generation;

        var value = ParseObject();
        if (value is not null)
        {
            value.ObjectNumber = number;
            value.GenerationNumber = generation;
        }

        return value;
    }

    /// <summary>Raises a parse error naming the byte offset, which is what makes one diagnosable.</summary>
    public OfficeNetException Error(string message) =>
        new($"{message} (at byte offset {Position}).");
}

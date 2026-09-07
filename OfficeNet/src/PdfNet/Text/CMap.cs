// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Globalization;
using System.Text;
using PdfNet.Objects;

namespace PdfNet.Text;

/// <summary>
/// A character map: how a composite font's byte string splits into codes, and what those codes mean.
/// </summary>
/// <remarks>
/// A CMap is a PostScript program in principle and a small set of declarative operators in
/// practice. Only the operators that appear in embedded CMaps are handled —
/// <c>codespacerange</c>, <c>cidrange</c>, <c>cidchar</c>, <c>bfrange</c> and <c>bfchar</c> — which
/// is enough for every <c>/ToUnicode</c> and every embedded encoding a real producer writes.
/// </remarks>
public sealed class CMap
{
    private readonly List<(int Low, int High, int Bytes)> _codespaces = [];
    private readonly Dictionary<int, int> _singleMappings = [];
    private readonly List<(int Low, int High, int Start)> _rangeMappings = [];

    private CMap()
    {
    }

    /// <summary>The Identity-H/V CMap: every code is two big-endian bytes and maps to itself.</summary>
    public static CMap IdentityTwoByte { get; } = CreateIdentity();

    private static CMap CreateIdentity()
    {
        var cmap = new CMap();
        cmap._codespaces.Add((0x0000, 0xFFFF, 2));
        cmap._rangeMappings.Add((0x0000, 0xFFFF, 0));
        return cmap;
    }

    /// <summary>Looks up one of the predefined CMap names; <c>null</c> when it is not known.</summary>
    /// <remarks>
    /// Only the Identity CMaps are built in. The CJK collections (UniJIS-UCS2-H and its siblings)
    /// are large external data files; a font using one and providing no <c>/ToUnicode</c> is
    /// reported as undecodable rather than guessed at.
    /// </remarks>
    public static CMap? Predefined(string name) => name switch
    {
        "Identity-H" or "Identity-V" => IdentityTwoByte,
        _ => null,
    };

    /// <summary>
    /// Reads the next character code from a string, returning it and how many bytes it consumed.
    /// </summary>
    /// <remarks>
    /// Code length is variable and is determined by the codespace ranges, not by a fixed width. A
    /// CMap can declare one-byte codes for 0x00-0x80 and two-byte codes for 0x8140-0xFCFC in the
    /// same font — which is exactly what Shift-JIS-derived encodings do. Assuming two bytes
    /// everywhere silently doubles the character count on such a font.
    /// </remarks>
    public (int Code, int BytesConsumed) NextCode(byte[] bytes, int offset)
    {
        for (var width = 1; width <= 4 && offset + width <= bytes.Length; width++)
        {
            var code = 0;
            for (var i = 0; i < width; i++)
            {
                code = code << 8 | bytes[offset + i];
            }

            foreach (var (low, high, codeBytes) in _codespaces)
            {
                if (codeBytes == width && code >= low && code <= high)
                {
                    return (code, width);
                }
            }
        }

        // Nothing matched. Fall back to the narrowest declared codespace width, or one byte.
        var fallbackWidth = _codespaces.Count > 0 ? _codespaces.Min(c => c.Bytes) : 1;
        fallbackWidth = Math.Min(fallbackWidth, bytes.Length - offset);

        if (fallbackWidth <= 0)
        {
            return (0, 1);
        }

        var fallback = 0;
        for (var i = 0; i < fallbackWidth; i++)
        {
            fallback = fallback << 8 | bytes[offset + i];
        }

        return (fallback, fallbackWidth);
    }

    /// <summary>Maps a code to a CID.</summary>
    public int ToCid(int code)
    {
        if (_singleMappings.TryGetValue(code, out var single))
        {
            return single;
        }

        foreach (var (low, high, start) in _rangeMappings)
        {
            if (code >= low && code <= high)
            {
                return start + (code - low);
            }
        }

        return code;
    }

    /// <summary>Parses an embedded CMap stream used as a font's <c>/Encoding</c>.</summary>
    public static CMap Parse(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var cmap = new CMap();
        var tokens = Tokenize(data);

        for (var i = 0; i < tokens.Count; i++)
        {
            switch (tokens[i])
            {
                case Keyword { Value: "begincodespacerange" }:
                    i = ReadCodespaceRanges(tokens, i + 1, cmap);
                    break;

                case Keyword { Value: "begincidrange" }:
                    i = ReadCidRanges(tokens, i + 1, cmap);
                    break;

                case Keyword { Value: "begincidchar" }:
                    i = ReadCidChars(tokens, i + 1, cmap);
                    break;
            }
        }

        if (cmap._codespaces.Count == 0)
        {
            cmap._codespaces.Add((0x0000, 0xFFFF, 2));
        }

        return cmap;
    }

    /// <summary>
    /// Parses a <c>/ToUnicode</c> CMap into a code-to-text map.
    /// </summary>
    /// <remarks>
    /// The value side is UTF-16BE and may be more than one code unit: a ligature glyph maps to the
    /// several characters it stands for, so "ﬁ" comes back as "fi" and the extracted text is
    /// searchable. Truncating to one char, which a naive reader does, silently drops the second
    /// letter of every ligature.
    /// </remarks>
    public static Dictionary<int, string> ParseToUnicode(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var result = new Dictionary<int, string>();
        var tokens = Tokenize(data);

        for (var i = 0; i < tokens.Count; i++)
        {
            switch (tokens[i])
            {
                case Keyword { Value: "beginbfchar" }:
                    i = ReadBfChars(tokens, i + 1, result);
                    break;

                case Keyword { Value: "beginbfrange" }:
                    i = ReadBfRanges(tokens, i + 1, result);
                    break;
            }
        }

        return result;
    }

    private static int ReadCodespaceRanges(List<object> tokens, int start, CMap cmap)
    {
        var i = start;

        while (i + 1 < tokens.Count)
        {
            if (tokens[i] is Keyword)
            {
                return i;
            }

            if (tokens[i] is not HexToken low || tokens[i + 1] is not HexToken high)
            {
                return i;
            }

            // The number of bytes in the hex literal is what declares the code width — <00> is a
            // one-byte codespace and <0000> a two-byte one, even though both are numerically zero.
            cmap._codespaces.Add((low.Value, high.Value, low.ByteLength));
            i += 2;
        }

        return i;
    }

    private static int ReadCidRanges(List<object> tokens, int start, CMap cmap)
    {
        var i = start;

        while (i + 2 < tokens.Count)
        {
            if (tokens[i] is Keyword)
            {
                return i;
            }

            if (tokens[i] is not HexToken low || tokens[i + 1] is not HexToken high ||
                tokens[i + 2] is not NumberToken cid)
            {
                return i;
            }

            cmap._rangeMappings.Add((low.Value, high.Value, cid.Value));
            i += 3;
        }

        return i;
    }

    private static int ReadCidChars(List<object> tokens, int start, CMap cmap)
    {
        var i = start;

        while (i + 1 < tokens.Count)
        {
            if (tokens[i] is Keyword)
            {
                return i;
            }

            if (tokens[i] is not HexToken code || tokens[i + 1] is not NumberToken cid)
            {
                return i;
            }

            cmap._singleMappings[code.Value] = cid.Value;
            i += 2;
        }

        return i;
    }

    private static int ReadBfChars(List<object> tokens, int start, Dictionary<int, string> result)
    {
        var i = start;

        while (i + 1 < tokens.Count)
        {
            if (tokens[i] is Keyword)
            {
                return i;
            }

            if (tokens[i] is not HexToken code)
            {
                return i;
            }

            switch (tokens[i + 1])
            {
                case HexToken value:
                    result[code.Value] = DecodeUtf16(value.Bytes);
                    break;

                case NameToken glyph:
                    var resolved = GlyphList.Resolve(glyph.Value);
                    if (resolved is not null)
                    {
                        result[code.Value] = resolved.Value.ToString();
                    }

                    break;

                default:
                    return i;
            }

            i += 2;
        }

        return i;
    }

    private static int ReadBfRanges(List<object> tokens, int start, Dictionary<int, string> result)
    {
        var i = start;

        while (i + 2 < tokens.Count)
        {
            if (tokens[i] is Keyword)
            {
                return i;
            }

            if (tokens[i] is not HexToken low || tokens[i + 1] is not HexToken high)
            {
                return i;
            }

            var from = low.Value;
            var to = high.Value;

            // A corrupt range could span the whole code space; capping it keeps a broken font from
            // allocating gigabytes.
            if (to < from || to - from > 65535)
            {
                i += 3;
                continue;
            }

            switch (tokens[i + 2])
            {
                case HexToken value:
                {
                    // The destination increments with the code. Incrementing the *last* UTF-16 code
                    // unit is what the specification says, which matters for surrogate pairs.
                    var bytes = value.Bytes;
                    for (var code = from; code <= to; code++)
                    {
                        var offset = code - from;
                        result[code] = IncrementUtf16(bytes, offset);
                    }

                    break;
                }

                case ArrayToken array:
                {
                    for (var code = from; code <= to && code - from < array.Items.Count; code++)
                    {
                        if (array.Items[code - from] is HexToken item)
                        {
                            result[code] = DecodeUtf16(item.Bytes);
                        }
                    }

                    break;
                }

                default:
                    return i;
            }

            i += 3;
        }

        return i;
    }

    private static string DecodeUtf16(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        // An odd length means the producer wrote single bytes, which some do for Latin text.
        if (bytes.Length % 2 != 0)
        {
            return Encoding.Latin1.GetString(bytes);
        }

        return Encoding.BigEndianUnicode.GetString(bytes);
    }

    private static string IncrementUtf16(byte[] bytes, int offset)
    {
        if (offset == 0 || bytes.Length < 2)
        {
            return DecodeUtf16(bytes);
        }

        var copy = (byte[])bytes.Clone();
        var last = (copy[^2] << 8 | copy[^1]) + offset;
        copy[^2] = (byte)(last >> 8);
        copy[^1] = (byte)last;
        return DecodeUtf16(copy);
    }

    // ---- Tokenizer ------------------------------------------------------------------------------

    private sealed record Keyword(string Value);

    private sealed record NumberToken(int Value);

    private sealed record NameToken(string Value);

    private sealed record HexToken(int Value, byte[] Bytes, int ByteLength);

    private sealed record ArrayToken(List<object> Items);

    private static List<object> Tokenize(byte[] data)
    {
        var tokens = new List<object>(256);
        var i = 0;

        while (i < data.Length)
        {
            var b = data[i];

            if (b is 0 or 9 or 10 or 12 or 13 or 32)
            {
                i++;
                continue;
            }

            if (b == (byte)'%')
            {
                while (i < data.Length && data[i] is not ((byte)'\r' or (byte)'\n'))
                {
                    i++;
                }

                continue;
            }

            if (b == (byte)'<')
            {
                // "<<" opens a dictionary, which a CMap's header contains and which carries no
                // mapping information — skipping to its close keeps its keys out of the token list.
                if (i + 1 < data.Length && data[i + 1] == (byte)'<')
                {
                    i = SkipDictionary(data, i);
                    continue;
                }

                i = ReadHex(data, i, out var hex);
                tokens.Add(hex);
                continue;
            }

            if (b == (byte)'[')
            {
                i = ReadArray(data, i + 1, out var array);
                tokens.Add(array);
                continue;
            }

            if (b == (byte)']')
            {
                i++;
                continue;
            }

            if (b == (byte)'/')
            {
                i = ReadName(data, i + 1, out var name);
                tokens.Add(name);
                continue;
            }

            if (b == (byte)'(')
            {
                i = SkipLiteralString(data, i);
                continue;
            }

            if (char.IsAsciiDigit((char)b) || b is (byte)'-' or (byte)'+' or (byte)'.')
            {
                var start = i;
                while (i < data.Length &&
                       (char.IsAsciiDigit((char)data[i]) || data[i] is (byte)'-' or (byte)'+' or (byte)'.'))
                {
                    i++;
                }

                var text = Encoding.ASCII.GetString(data, start, i - start);
                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                {
                    tokens.Add(new NumberToken(value));
                }

                continue;
            }

            var keywordStart = i;
            while (i < data.Length && !IsCMapDelimiter(data[i]))
            {
                i++;
            }

            if (i == keywordStart)
            {
                i++;
                continue;
            }

            tokens.Add(new Keyword(Encoding.ASCII.GetString(data, keywordStart, i - keywordStart)));
        }

        return tokens;
    }

    private static bool IsCMapDelimiter(byte b) =>
        b is 0 or 9 or 10 or 12 or 13 or 32 or (byte)'<' or (byte)'>' or (byte)'[' or (byte)']' or
            (byte)'/' or (byte)'(' or (byte)')' or (byte)'%' or (byte)'{' or (byte)'}';

    private static int SkipDictionary(byte[] data, int i)
    {
        var depth = 0;

        while (i < data.Length)
        {
            if (data[i] == (byte)'<' && i + 1 < data.Length && data[i + 1] == (byte)'<')
            {
                depth++;
                i += 2;
                continue;
            }

            if (data[i] == (byte)'>' && i + 1 < data.Length && data[i + 1] == (byte)'>')
            {
                depth--;
                i += 2;
                if (depth <= 0)
                {
                    return i;
                }

                continue;
            }

            i++;
        }

        return i;
    }

    private static int SkipLiteralString(byte[] data, int i)
    {
        i++; // '('
        var depth = 1;

        while (i < data.Length && depth > 0)
        {
            if (data[i] == (byte)'\\')
            {
                i += 2;
                continue;
            }

            if (data[i] == (byte)'(')
            {
                depth++;
            }
            else if (data[i] == (byte)')')
            {
                depth--;
            }

            i++;
        }

        return i;
    }

    private static int ReadHex(byte[] data, int i, out HexToken token)
    {
        i++; // '<'
        var digits = new List<byte>(8);

        while (i < data.Length && data[i] != (byte)'>')
        {
            var b = data[i++];
            var value = b switch
            {
                >= (byte)'0' and <= (byte)'9' => b - '0',
                >= (byte)'A' and <= (byte)'F' => b - 'A' + 10,
                >= (byte)'a' and <= (byte)'f' => b - 'a' + 10,
                _ => -1,
            };

            if (value >= 0)
            {
                digits.Add((byte)value);
            }
        }

        if (i < data.Length)
        {
            i++; // '>'
        }

        // An odd digit count is padded on the right, as the specification says for hex strings.
        if (digits.Count % 2 != 0)
        {
            digits.Add(0);
        }

        var bytes = new byte[digits.Count / 2];
        for (var k = 0; k < bytes.Length; k++)
        {
            bytes[k] = (byte)(digits[k * 2] << 4 | digits[k * 2 + 1]);
        }

        var numeric = 0;
        foreach (var b in bytes.Length > 4 ? bytes[..4] : bytes)
        {
            numeric = numeric << 8 | b;
        }

        token = new HexToken(numeric, bytes, bytes.Length);
        return i;
    }

    private static int ReadName(byte[] data, int i, out NameToken token)
    {
        var start = i;
        while (i < data.Length && !IsCMapDelimiter(data[i]))
        {
            i++;
        }

        token = new NameToken(Encoding.ASCII.GetString(data, start, i - start));
        return i;
    }

    private static int ReadArray(byte[] data, int i, out ArrayToken token)
    {
        var items = new List<object>();

        while (i < data.Length && data[i] != (byte)']')
        {
            if (data[i] is 0 or 9 or 10 or 12 or 13 or 32)
            {
                i++;
                continue;
            }

            if (data[i] == (byte)'<')
            {
                i = ReadHex(data, i, out var hex);
                items.Add(hex);
                continue;
            }

            i++;
        }

        if (i < data.Length)
        {
            i++; // ']'
        }

        token = new ArrayToken(items);
        return i;
    }
}

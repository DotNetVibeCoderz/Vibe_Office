// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using PdfNet.Io;
using PdfNet.Objects;

namespace PdfNet.Text;

/// <summary>One operator and the operands that preceded it.</summary>
/// <param name="Operator">The operator's name, for example <c>Tj</c> or <c>re</c>.</param>
/// <param name="Operands">The operands, in the order they appeared.</param>
public readonly record struct ContentOperation(string Operator, IReadOnlyList<PdfObject> Operands)
{
    /// <summary>An operand as a double, or <paramref name="fallback"/>.</summary>
    public double Number(int index, double fallback = 0) =>
        index < Operands.Count && Operands[index] is PdfNumber n ? n.DoubleValue : fallback;

    /// <summary>An operand as a name's text, or <c>null</c>.</summary>
    public string? Name(int index) =>
        index < Operands.Count && Operands[index] is PdfName n ? n.Value : null;

    /// <summary>An operand as string bytes, or <c>null</c>.</summary>
    public byte[]? String(int index) =>
        index < Operands.Count && Operands[index] is PdfString s ? s.Value : null;

    /// <summary>An operand as an array, or <c>null</c>.</summary>
    public PdfArray? Array(int index) =>
        index < Operands.Count ? Operands[index] as PdfArray : null;

    public override string ToString() => $"{string.Join(' ', Operands)} {Operator}";
}

/// <summary>
/// Splits a content stream into operator/operand groups.
/// </summary>
/// <remarks>
/// <para>
/// A content stream is postfix: operands come first, then the operator that consumes them. The
/// parser therefore accumulates objects until it meets a bare keyword, emits that as an operation,
/// and clears the accumulator.
/// </para>
/// <para>
/// Two operators break the pure-postfix rule and need handling here rather than by the caller.
/// <c>BI</c> starts an inline image whose binary payload runs to <c>EI</c> and is not COS syntax at
/// all; scanning past it is the only way to keep the stream in sync. And <c>true</c>/<c>false</c>/
/// <c>null</c> look like keywords but are operands.
/// </para>
/// </remarks>
public static class ContentStreamParser
{
    /// <summary>Parses a content stream into operations.</summary>
    public static IEnumerable<ContentOperation> Parse(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var parser = new PdfParser(content);
        var operands = new List<PdfObject>(8);

        while (true)
        {
            parser.SkipWhitespace();

            if (parser.Position >= content.Length)
            {
                yield break;
            }

            var b = content[parser.Position];

            // An operand always starts with one of these.
            if (b is (byte)'/' or (byte)'(' or (byte)'[' or (byte)'<' or (byte)'+' or (byte)'-' or (byte)'.' ||
                char.IsAsciiDigit((char)b))
            {
                var before = parser.Position;
                var value = parser.ParseObject();

                if (value is not null)
                {
                    operands.Add(value);
                }
                else if (parser.Position == before)
                {
                    parser.Position++;
                }

                continue;
            }

            if (b is (byte)']' or (byte)'>' or (byte)')' or (byte)'}' or (byte)'{')
            {
                parser.Position++;
                continue;
            }

            var keyword = parser.ReadKeyword();

            if (keyword.Length == 0)
            {
                parser.Position++;
                continue;
            }

            switch (keyword)
            {
                case "true":
                    operands.Add(PdfBoolean.True);
                    continue;
                case "false":
                    operands.Add(PdfBoolean.False);
                    continue;
                case "null":
                    operands.Add(PdfNull.Instance);
                    continue;

                case "BI":
                {
                    var image = ReadInlineImage(parser, content);
                    if (image is not null)
                    {
                        yield return new ContentOperation("BI", [image]);
                    }

                    operands.Clear();
                    continue;
                }
            }

            yield return new ContentOperation(keyword, operands.ToArray());
            operands.Clear();
        }
    }

    private static PdfObject? ReadInlineImage(PdfParser parser, byte[] content)
    {
        var dictionary = new PdfDictionary();

        // Key/value pairs run until the ID operator.
        while (true)
        {
            parser.SkipWhitespace();

            if (parser.Position >= content.Length)
            {
                return null;
            }

            if (content[parser.Position] == (byte)'/')
            {
                var key = parser.ParseObject() as PdfName;
                var value = parser.ParseObject();

                if (key is not null && value is not null)
                {
                    dictionary[key] = value;
                }

                continue;
            }

            var keyword = parser.ReadKeyword();

            if (keyword == "ID")
            {
                break;
            }

            if (keyword.Length == 0)
            {
                parser.Position++;
            }

            if (keyword == "EI")
            {
                return dictionary;
            }
        }

        // Exactly one whitespace byte separates ID from the data.
        if (parser.Position < content.Length && PdfParser.IsWhitespace(content[parser.Position]))
        {
            parser.Position++;
        }

        var start = parser.Position;

        // EI must be found by scanning, because the payload length is not declared. A false
        // positive inside binary data is possible, so the match is only accepted when it is
        // followed by whitespace or the end of the stream — which is what the specification's own
        // recommended heuristic says.
        var end = -1;
        for (var i = start; i + 1 < content.Length; i++)
        {
            if (content[i] != (byte)'E' || content[i + 1] != (byte)'I')
            {
                continue;
            }

            var precededByWhitespace = i > start && PdfParser.IsWhitespace(content[i - 1]);
            var followedByWhitespace = i + 2 >= content.Length || PdfParser.IsWhitespace(content[i + 2]);

            if (precededByWhitespace && followedByWhitespace)
            {
                end = i;
                break;
            }
        }

        if (end < 0)
        {
            parser.Position = content.Length;
            return dictionary;
        }

        var length = end - start;
        while (length > 0 && PdfParser.IsWhitespace(content[start + length - 1]))
        {
            length--;
        }

        var payload = new byte[length];
        Array.Copy(content, start, payload, 0, length);
        parser.Position = end + 2;

        return new PdfStream(dictionary, payload);
    }
}

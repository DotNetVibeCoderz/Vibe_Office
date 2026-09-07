// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Buffers;
using System.IO.Compression;
using OfficeNet.Core;
using PdfNet.Objects;

namespace PdfNet.Io.Filters;

/// <summary>A PDF stream filter: an encoding applied to stream data.</summary>
public interface IPdfFilter
{
    /// <summary>The filter's PDF name.</summary>
    string Name { get; }

    /// <summary>
    /// True when the filter's output is an image codec this library passes through rather than
    /// decodes (DCT, JPX, JBIG2, CCITT). Text extraction skips those streams; image extraction
    /// hands the bytes over as a file.
    /// </summary>
    bool IsImageCodec => false;

    /// <summary>Decodes data.</summary>
    byte[] Decode(byte[] data, PdfDictionary? parameters);

    /// <summary>Encodes data. Filters that cannot encode throw.</summary>
    byte[] Encode(byte[] data, PdfDictionary? parameters) =>
        throw new OfficeNetNotSupportedException($"The {Name} filter cannot encode.");
}

/// <summary>The filter registry, keyed by PDF name and abbreviation.</summary>
public static class PdfFilters
{
    private static readonly Dictionary<string, IPdfFilter> Registry = new(StringComparer.Ordinal);

    static PdfFilters()
    {
        Register(new FlateFilter());
        Register(new LzwFilter());
        Register(new AsciiHexFilter());
        Register(new Ascii85Filter());
        Register(new RunLengthFilter());
        Register(new PassthroughFilter("DCTDecode", "DCT"));
        Register(new PassthroughFilter("JPXDecode"));
        Register(new PassthroughFilter("JBIG2Decode"));
        Register(new PassthroughFilter("CCITTFaxDecode", "CCF"));
        Register(new CryptIdentityFilter());
    }

    /// <summary>Registers a filter under its name and any abbreviations.</summary>
    public static void Register(IPdfFilter filter, params string[] aliases)
    {
        ArgumentNullException.ThrowIfNull(filter);
        Registry[filter.Name] = filter;
        foreach (var alias in aliases)
        {
            Registry[alias] = filter;
        }
    }

    /// <summary>Looks a filter up by name, or <c>null</c> when it is not implemented.</summary>
    public static IPdfFilter? Find(string name) => Registry.GetValueOrDefault(name);

    /// <summary>True when the named filter is an image codec passed through rather than decoded.</summary>
    public static bool IsImageCodec(string name) => Find(name)?.IsImageCodec ?? false;

    private static void Register(PassthroughFilter filter) => Register(filter, filter.Aliases);

    private static void Register(FlateFilter filter) => Register(filter, "Fl");

    private static void Register(LzwFilter filter) => Register(filter, "LZW");

    private static void Register(AsciiHexFilter filter) => Register(filter, "AHx");

    private static void Register(Ascii85Filter filter) => Register(filter, "A85");

    private static void Register(RunLengthFilter filter) => Register(filter, "RL");

    private static void Register(CryptIdentityFilter filter) => Register(filter, []);
}

/// <summary>Zlib/deflate, the filter almost every modern PDF stream uses.</summary>
public sealed class FlateFilter : IPdfFilter
{
    /// <inheritdoc />
    public string Name => "FlateDecode";

    /// <inheritdoc />
    public byte[] Decode(byte[] data, PdfDictionary? parameters)
    {
        var raw = Inflate(data);
        return Predictor.Undo(raw, parameters);
    }

    /// <inheritdoc />
    public byte[] Encode(byte[] data, PdfDictionary? parameters)
    {
        using var output = new MemoryStream();
        using (var deflate = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(data, 0, data.Length);
        }

        return output.ToArray();
    }

    private static byte[] Inflate(byte[] data)
    {
        if (data.Length == 0)
        {
            return [];
        }

        // Producers disagree about whether to write the two-byte zlib header. Acrobat accepts both,
        // so both are tried: zlib first because it is the specified form, raw deflate second.
        // Skipping this fallback is why some scanned PDFs "have no text" in other libraries.
        foreach (var mode in (ReadOnlySpan<int>)[0, 1, 2])
        {
            try
            {
                using var input = new MemoryStream(data, writable: false);

                // Mode 2 skips leading whitespace, which a few producers emit before the data.
                if (mode == 2)
                {
                    while (input.Position < input.Length)
                    {
                        var b = input.ReadByte();
                        if (b is not (' ' or '\r' or '\n' or '\t' or 0))
                        {
                            input.Position--;
                            break;
                        }
                    }
                }

                using Stream decompressor = mode == 1
                    ? new DeflateStream(input, CompressionMode.Decompress)
                    : new ZLibStream(input, CompressionMode.Decompress);

                using var output = new MemoryStream(data.Length * 4);
                decompressor.CopyTo(output);

                if (output.Length > 0)
                {
                    return output.ToArray();
                }
            }
            catch (InvalidDataException)
            {
                // Try the next framing.
            }
        }

        // A truncated stream is common in damaged files. Recovering the prefix that did inflate is
        // better than losing the whole page, so one more pass reads until the error and keeps
        // whatever arrived.
        return InflatePartial(data);
    }

    private static byte[] InflatePartial(byte[] data)
    {
        using var output = new MemoryStream();

        foreach (var skip in (ReadOnlySpan<int>)[2, 0])
        {
            if (data.Length <= skip)
            {
                continue;
            }

            try
            {
                using var input = new MemoryStream(data, skip, data.Length - skip, writable: false);
                using var decompressor = new DeflateStream(input, CompressionMode.Decompress);

                var buffer = ArrayPool<byte>.Shared.Rent(8192);
                try
                {
                    int read;
                    while ((read = decompressor.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        output.Write(buffer, 0, read);
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
            catch (InvalidDataException)
            {
                // Whatever was written to output before the failure is the recovered prefix.
            }

            if (output.Length > 0)
            {
                return output.ToArray();
            }
        }

        return [];
    }
}

/// <summary>LZW, the filter PDFs written before 1.2 and many scanners still use.</summary>
public sealed class LzwFilter : IPdfFilter
{
    /// <inheritdoc />
    public string Name => "LZWDecode";

    /// <inheritdoc />
    public byte[] Decode(byte[] data, PdfDictionary? parameters)
    {
        var earlyChange = parameters?.GetInt(PdfName.Get("EarlyChange"), 1) ?? 1;
        var raw = Decompress(data, earlyChange != 0);
        return Predictor.Undo(raw, parameters);
    }

    private static byte[] Decompress(byte[] data, bool earlyChange)
    {
        const int ClearCode = 256;
        const int EodCode = 257;

        var table = new List<byte[]>(4096);

        void ResetTable()
        {
            table.Clear();
            for (var i = 0; i < 256; i++)
            {
                table.Add([(byte)i]);
            }

            // 256 and 257 are the clear and end-of-data codes; they occupy slots but hold no data.
            table.Add([]);
            table.Add([]);
        }

        ResetTable();

        using var output = new MemoryStream(data.Length * 3);
        var codeLength = 9;
        var bitBuffer = 0;
        var bitCount = 0;
        byte[]? previous = null;

        foreach (var b in data)
        {
            bitBuffer = bitBuffer << 8 | b;
            bitCount += 8;

            while (bitCount >= codeLength)
            {
                var code = bitBuffer >> bitCount - codeLength & (1 << codeLength) - 1;
                bitCount -= codeLength;

                if (code == EodCode)
                {
                    return output.ToArray();
                }

                if (code == ClearCode)
                {
                    ResetTable();
                    codeLength = 9;
                    previous = null;
                    continue;
                }

                byte[] entry;
                if (code < table.Count)
                {
                    entry = table[code];
                }
                else if (previous is not null)
                {
                    // The KwKwK case: a code that names the entry being defined right now.
                    entry = [.. previous, previous[0]];
                }
                else
                {
                    return output.ToArray();
                }

                output.Write(entry, 0, entry.Length);

                if (previous is not null && table.Count < 4096)
                {
                    table.Add([.. previous, entry[0]]);
                }

                previous = entry;

                // EarlyChange=1 (the default) widens the code one entry before the table would
                // otherwise require it. Getting this backwards decodes the first few hundred bytes
                // correctly and then produces garbage, which is why the parameter is honoured
                // rather than assumed.
                var next = table.Count + (earlyChange ? 1 : 0);
                codeLength = next >= 2048 ? 12
                    : next >= 1024 ? 11
                    : next >= 512 ? 10
                    : 9;
            }
        }

        return output.ToArray();
    }
}

/// <summary>Hexadecimal text encoding.</summary>
public sealed class AsciiHexFilter : IPdfFilter
{
    /// <inheritdoc />
    public string Name => "ASCIIHexDecode";

    /// <inheritdoc />
    public byte[] Decode(byte[] data, PdfDictionary? parameters)
    {
        using var output = new MemoryStream(data.Length / 2 + 1);
        var high = -1;

        foreach (var b in data)
        {
            if (b == (byte)'>')
            {
                break;
            }

            var digit = HexValue(b);
            if (digit < 0)
            {
                continue;
            }

            if (high < 0)
            {
                high = digit;
            }
            else
            {
                output.WriteByte((byte)(high << 4 | digit));
                high = -1;
            }
        }

        // An odd number of digits means the last byte's low nibble is zero, per the specification.
        if (high >= 0)
        {
            output.WriteByte((byte)(high << 4));
        }

        return output.ToArray();
    }

    /// <inheritdoc />
    public byte[] Encode(byte[] data, PdfDictionary? parameters)
    {
        var hex = Convert.ToHexString(data);
        var result = new byte[hex.Length + 1];
        for (var i = 0; i < hex.Length; i++)
        {
            result[i] = (byte)hex[i];
        }

        result[^1] = (byte)'>';
        return result;
    }

    private static int HexValue(byte b) => b switch
    {
        >= (byte)'0' and <= (byte)'9' => b - '0',
        >= (byte)'A' and <= (byte)'F' => b - 'A' + 10,
        >= (byte)'a' and <= (byte)'f' => b - 'a' + 10,
        _ => -1,
    };
}

/// <summary>Base-85 text encoding.</summary>
public sealed class Ascii85Filter : IPdfFilter
{
    /// <inheritdoc />
    public string Name => "ASCII85Decode";

    /// <inheritdoc />
    public byte[] Decode(byte[] data, PdfDictionary? parameters)
    {
        using var output = new MemoryStream(data.Length * 4 / 5 + 4);
        Span<byte> group = stackalloc byte[5];
        var count = 0;
        var start = 0;

        // A leading "<~" is legal and some producers write it.
        if (data.Length >= 2 && data[0] == '<' && data[1] == '~')
        {
            start = 2;
        }

        for (var i = start; i < data.Length; i++)
        {
            var b = data[i];

            if (b is (byte)'~')
            {
                break;
            }

            if (b is (byte)' ' or (byte)'\n' or (byte)'\r' or (byte)'\t' or 0 or (byte)'\f')
            {
                continue;
            }

            // 'z' abbreviates four zero bytes, but only at a group boundary.
            if (b == (byte)'z' && count == 0)
            {
                output.Write("\0\0\0\0"u8);
                continue;
            }

            if (b is < (byte)'!' or > (byte)'u')
            {
                continue;
            }

            group[count++] = (byte)(b - '!');

            if (count == 5)
            {
                WriteGroup(output, group, 5);
                count = 0;
            }
        }

        if (count > 0)
        {
            // A partial group is padded with 'u' (84) and yields count-1 bytes.
            for (var i = count; i < 5; i++)
            {
                group[i] = 84;
            }

            WriteGroup(output, group, count);
        }

        return output.ToArray();
    }

    /// <inheritdoc />
    public byte[] Encode(byte[] data, PdfDictionary? parameters)
    {
        using var output = new MemoryStream(data.Length * 5 / 4 + 8);
        var i = 0;

        while (i + 4 <= data.Length)
        {
            var value = (uint)(data[i] << 24 | data[i + 1] << 16 | data[i + 2] << 8 | data[i + 3]);
            if (value == 0)
            {
                output.WriteByte((byte)'z');
            }
            else
            {
                WriteBase85(output, value, 5);
            }

            i += 4;
        }

        var remaining = data.Length - i;
        if (remaining > 0)
        {
            uint value = 0;
            for (var j = 0; j < 4; j++)
            {
                value = value << 8 | (j < remaining ? data[i + j] : 0u);
            }

            WriteBase85(output, value, remaining + 1);
        }

        output.Write("~>"u8);
        return output.ToArray();
    }

    private static void WriteBase85(Stream output, uint value, int count)
    {
        Span<byte> digits = stackalloc byte[5];
        for (var i = 4; i >= 0; i--)
        {
            digits[i] = (byte)(value % 85 + '!');
            value /= 85;
        }

        output.Write(digits[..count]);
    }

    private static void WriteGroup(Stream output, ReadOnlySpan<byte> group, int count)
    {
        uint value = 0;
        for (var i = 0; i < 5; i++)
        {
            value = value * 85 + group[i];
        }

        Span<byte> bytes = [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];
        output.Write(bytes[..(count - 1)]);
    }
}

/// <summary>Byte-oriented run-length encoding.</summary>
public sealed class RunLengthFilter : IPdfFilter
{
    /// <inheritdoc />
    public string Name => "RunLengthDecode";

    /// <inheritdoc />
    public byte[] Decode(byte[] data, PdfDictionary? parameters)
    {
        using var output = new MemoryStream(data.Length * 2);
        var i = 0;

        while (i < data.Length)
        {
            var length = data[i++];

            if (length == 128)
            {
                break;
            }

            if (length < 128)
            {
                var count = length + 1;
                if (i + count > data.Length)
                {
                    count = data.Length - i;
                }

                output.Write(data, i, count);
                i += count;
            }
            else
            {
                if (i >= data.Length)
                {
                    break;
                }

                var repeat = 257 - length;
                var value = data[i++];
                for (var j = 0; j < repeat; j++)
                {
                    output.WriteByte(value);
                }
            }
        }

        return output.ToArray();
    }

    /// <inheritdoc />
    public byte[] Encode(byte[] data, PdfDictionary? parameters)
    {
        using var output = new MemoryStream(data.Length + data.Length / 128 + 2);
        var i = 0;

        while (i < data.Length)
        {
            var runLength = 1;
            while (i + runLength < data.Length && runLength < 128 && data[i + runLength] == data[i])
            {
                runLength++;
            }

            if (runLength >= 2)
            {
                output.WriteByte((byte)(257 - runLength));
                output.WriteByte(data[i]);
                i += runLength;
                continue;
            }

            var literalStart = i;
            var literalLength = 0;
            while (i < data.Length && literalLength < 128)
            {
                if (i + 2 < data.Length && data[i] == data[i + 1] && data[i] == data[i + 2])
                {
                    break;
                }

                i++;
                literalLength++;
            }

            output.WriteByte((byte)(literalLength - 1));
            output.Write(data, literalStart, literalLength);
        }

        output.WriteByte(128);
        return output.ToArray();
    }
}

/// <summary>An image codec whose bytes are handed through untouched.</summary>
public sealed class PassthroughFilter : IPdfFilter
{
    /// <summary>Creates a passthrough filter.</summary>
    public PassthroughFilter(string name, params string[] aliases)
    {
        Name = name;
        Aliases = aliases;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <summary>Additional names this filter is registered under.</summary>
    public string[] Aliases { get; }

    /// <inheritdoc />
    public bool IsImageCodec => true;

    /// <inheritdoc />
    public byte[] Decode(byte[] data, PdfDictionary? parameters) => data;

    /// <inheritdoc />
    public byte[] Encode(byte[] data, PdfDictionary? parameters) => data;
}

/// <summary>
/// The <c>/Crypt</c> filter with the Identity name, which means "this stream is not encrypted".
/// </summary>
public sealed class CryptIdentityFilter : IPdfFilter
{
    /// <inheritdoc />
    public string Name => "Crypt";

    /// <inheritdoc />
    public byte[] Decode(byte[] data, PdfDictionary? parameters) => data;

    /// <inheritdoc />
    public byte[] Encode(byte[] data, PdfDictionary? parameters) => data;
}

/// <summary>
/// The PNG and TIFF predictors that Flate and LZW streams optionally apply before compression.
/// </summary>
/// <remarks>
/// A predictor stores each byte as a difference from a neighbour, which makes the compressor's job
/// far easier on image and cross-reference data. Ignoring <c>/Predictor</c> yields data that
/// inflates without error and is completely wrong — which, in an xref stream, means every object
/// offset is garbage and the file appears to have no pages.
/// </remarks>
public static class Predictor
{
    /// <summary>Reverses the predictor named by a stream's decode parameters.</summary>
    public static byte[] Undo(byte[] data, PdfDictionary? parameters)
    {
        if (parameters is null)
        {
            return data;
        }

        var predictor = parameters.GetInt(PdfName.Get("Predictor"), 1);
        if (predictor <= 1)
        {
            return data;
        }

        var colors = Math.Max(1, parameters.GetInt(PdfName.Get("Colors"), 1));
        var bitsPerComponent = Math.Max(1, parameters.GetInt(PdfName.Get("BitsPerComponent"), 8));
        var columns = Math.Max(1, parameters.GetInt(PdfName.Get("Columns"), 1));

        var bytesPerPixel = Math.Max(1, colors * bitsPerComponent / 8);
        var rowLength = (columns * colors * bitsPerComponent + 7) / 8;

        return predictor == 2
            ? UndoTiff(data, colors, bitsPerComponent, columns)
            : UndoPng(data, rowLength, bytesPerPixel);
    }

    private static byte[] UndoPng(byte[] data, int rowLength, int bytesPerPixel)
    {
        // Each PNG-predicted row is prefixed with a one-byte filter type.
        var stride = rowLength + 1;
        var rows = data.Length / stride;
        var output = new byte[rows * rowLength];

        var previous = new byte[rowLength];
        var current = new byte[rowLength];

        for (var row = 0; row < rows; row++)
        {
            var offset = row * stride;
            var filterType = data[offset];
            Array.Copy(data, offset + 1, current, 0, rowLength);

            switch (filterType)
            {
                case 0:
                    break;

                case 1: // Sub
                    for (var i = bytesPerPixel; i < rowLength; i++)
                    {
                        current[i] = (byte)(current[i] + current[i - bytesPerPixel]);
                    }

                    break;

                case 2: // Up
                    for (var i = 0; i < rowLength; i++)
                    {
                        current[i] = (byte)(current[i] + previous[i]);
                    }

                    break;

                case 3: // Average
                    for (var i = 0; i < rowLength; i++)
                    {
                        var left = i >= bytesPerPixel ? current[i - bytesPerPixel] : 0;
                        current[i] = (byte)(current[i] + (left + previous[i]) / 2);
                    }

                    break;

                case 4: // Paeth
                    for (var i = 0; i < rowLength; i++)
                    {
                        var left = i >= bytesPerPixel ? current[i - bytesPerPixel] : (byte)0;
                        var up = previous[i];
                        var upLeft = i >= bytesPerPixel ? previous[i - bytesPerPixel] : (byte)0;
                        current[i] = (byte)(current[i] + Paeth(left, up, upLeft));
                    }

                    break;

                default:
                    // An unknown filter type means the stream is not actually PNG-predicted.
                    // Copying the row through keeps the rest of the data usable.
                    break;
            }

            Array.Copy(current, 0, output, row * rowLength, rowLength);
            (previous, current) = (current, previous);
        }

        return output;
    }

    private static byte Paeth(byte a, byte b, byte c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static byte[] UndoTiff(byte[] data, int colors, int bitsPerComponent, int columns)
    {
        if (bitsPerComponent != 8)
        {
            // Sub-byte TIFF prediction is legal but vanishingly rare; passing the data through
            // is better than corrupting it with an 8-bit assumption.
            return data;
        }

        var rowLength = columns * colors;
        var rows = data.Length / rowLength;
        var output = (byte[])data.Clone();

        for (var row = 0; row < rows; row++)
        {
            var offset = row * rowLength;
            for (var i = colors; i < rowLength; i++)
            {
                output[offset + i] = (byte)(output[offset + i] + output[offset + i - colors]);
            }
        }

        return output;
    }
}

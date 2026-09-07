// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using PdfNet.Objects;
using PdfNet.Security;

namespace PdfNet.Io;

/// <summary>
/// Serialises an object graph as a complete PDF file.
/// </summary>
/// <remarks>
/// <para>
/// The writer always produces a full file rather than an incremental update. Incremental saving is
/// smaller and preserves signatures, but it also preserves whatever was wrong with the original,
/// and the object graph this library hands back has already been rewritten in memory — so there is
/// no original left to append to.
/// </para>
/// <para>
/// A classic cross-reference table is written rather than an xref stream. It is larger, and it is
/// readable by every PDF consumer ever shipped including the ones embedded in printers and
/// scanners. The compressed form saves a few kilobytes on a file that is mostly fonts and images.
/// </para>
/// </remarks>
public sealed class PdfWriter
{
    private readonly Stream _stream;
    private readonly List<(int Number, PdfObject Object)> _objects = [];
    private long _position;

    /// <summary>Creates a writer over an output stream.</summary>
    public PdfWriter(Stream stream) => _stream = stream ?? throw new ArgumentNullException(nameof(stream));

    /// <summary>The PDF version to declare in the header.</summary>
    public string Version { get; set; } = "1.7";

    /// <summary>
    /// Compresses uncompressed content streams with Flate before writing.
    /// </summary>
    public bool CompressStreams { get; set; } = true;

    /// <summary>Adds an indirect object to be written.</summary>
    public void Add(int number, PdfObject value)
    {
        value.ObjectNumber = number;
        _objects.Add((number, value));
    }

    /// <summary>
    /// Writes the complete file.
    /// </summary>
    /// <param name="trailer">
    /// The trailer dictionary. <c>/Root</c> is required; <c>/Info</c>, <c>/ID</c> and
    /// <c>/Encrypt</c> are written when present.
    /// </param>
    /// <param name="encryption">The encryption to apply, or <c>null</c> for a plain file.</param>
    /// <param name="encryptDictionaryNumber">
    /// The object number holding the encryption dictionary, which must not itself be encrypted.
    /// </param>
    public void Write(PdfDictionary trailer, IPdfEncryption? encryption = null, int encryptDictionaryNumber = 0)
    {
        ArgumentNullException.ThrowIfNull(trailer);

        var context = new PdfWriteContext { Encryption = encryption };

        WriteHeader();

        var offsets = new Dictionary<int, long>(_objects.Count);
        var maxNumber = 0;

        foreach (var (number, value) in _objects.OrderBy(o => o.Number))
        {
            offsets[number] = _position;
            maxNumber = Math.Max(maxNumber, number);

            context.CurrentObjectNumber = number;
            context.CurrentGenerationNumber = value.GenerationNumber;
            context.SuppressEncryption = number == encryptDictionaryNumber;

            WriteAscii($"{number} {value.GenerationNumber} obj\n");

            using (var buffer = new MemoryStream())
            {
                value.Write(buffer, context);
                var bytes = buffer.ToArray();
                _stream.Write(bytes, 0, bytes.Length);
                _position += bytes.Length;
            }

            WriteAscii("\nendobj\n");
        }

        context.SuppressEncryption = false;

        var xrefOffset = _position;
        WriteXrefTable(offsets, maxNumber);
        WriteTrailer(trailer, maxNumber + 1, xrefOffset);
    }

    private void WriteHeader()
    {
        WriteAscii($"%PDF-{Version}\n");

        // A comment line of bytes above 127 tells file-transfer tools the file is binary. Without
        // it, an FTP client in text mode rewrites line endings inside streams and destroys them.
        _stream.WriteByte((byte)'%');
        _stream.WriteByte(0xE2);
        _stream.WriteByte(0xE3);
        _stream.WriteByte(0xCF);
        _stream.WriteByte(0xD3);
        _stream.WriteByte((byte)'\n');
        _position += 6;
    }

    private void WriteXrefTable(Dictionary<int, long> offsets, int maxNumber)
    {
        WriteAscii("xref\n");
        WriteAscii($"0 {maxNumber + 1}\n");

        // Object 0 always heads the free list with generation 65535. Every entry is exactly 20
        // bytes including the two-byte end-of-line, and a reader is entitled to seek by
        // multiplying — so a one-byte deviation makes every lookup after it wrong.
        WriteAscii("0000000000 65535 f \n");

        for (var number = 1; number <= maxNumber; number++)
        {
            if (offsets.TryGetValue(number, out var offset))
            {
                WriteAscii($"{offset:D10} 00000 n \n");
            }
            else
            {
                // A gap in the numbering is written as a free entry rather than skipped, because
                // the table is positional.
                WriteAscii("0000000000 65535 f \n");
            }
        }
    }

    private void WriteTrailer(PdfDictionary trailer, int size, long xrefOffset)
    {
        trailer.Set(PdfName.Size, size);

        WriteAscii("trailer\n");

        using (var buffer = new MemoryStream())
        {
            // The trailer is never encrypted, whatever the file's encryption.
            trailer.Write(buffer, new PdfWriteContext { SuppressEncryption = true });
            var bytes = buffer.ToArray();
            _stream.Write(bytes, 0, bytes.Length);
            _position += bytes.Length;
        }

        WriteAscii($"\nstartxref\n{xrefOffset.ToString(CultureInfo.InvariantCulture)}\n%%EOF\n");
    }

    private void WriteAscii(string text)
    {
        var bytes = Encoding.ASCII.GetBytes(text);
        _stream.Write(bytes, 0, bytes.Length);
        _position += bytes.Length;
    }

    /// <summary>
    /// Builds the two-element <c>/ID</c> array a PDF trailer should carry.
    /// </summary>
    /// <remarks>
    /// The first element identifies the document across its whole history and must be preserved
    /// across saves; the second identifies this particular revision. Both feed the legacy
    /// encryption key derivation, so a file whose <c>/ID</c> changes cannot be decrypted with the
    /// key derived before the change.
    /// </remarks>
    public static PdfArray CreateFileId(byte[]? existingFirst = null)
    {
        var first = existingFirst is { Length: > 0 } ? existingFirst : RandomNumberGenerator.GetBytes(16);
        var second = RandomNumberGenerator.GetBytes(16);

        return
        [
            new PdfString(first) { IsHex = true },
            new PdfString(second) { IsHex = true },
        ];
    }
}

// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Text;
using OfficeNet.Core;
using PdfNet.Io.Filters;

namespace PdfNet.Objects;

/// <summary>
/// A PDF stream: a dictionary plus an arbitrarily long byte payload.
/// </summary>
/// <remarks>
/// <para>
/// Streams hold everything large in a PDF — page content, embedded fonts, images, and in modern
/// files the cross-reference table itself. The payload is stored encoded and decoded on demand,
/// which is what allows a merge to copy a 40 MB image stream between documents without ever
/// inflating it.
/// </para>
/// <para>
/// <see cref="Data"/> is the raw, still-filtered bytes; <see cref="Decoded"/> applies the filter
/// chain. Writing through <see cref="SetDecoded"/> re-encodes, writing through <see cref="Data"/>
/// does not — conflating those is how a content stream ends up double-compressed and unreadable.
/// </para>
/// </remarks>
public sealed class PdfStream : PdfDictionary
{
    private byte[] _data;
    private byte[]? _decodedCache;

    /// <summary>Creates an empty stream.</summary>
    public PdfStream() => _data = [];

    /// <summary>Creates a stream with a dictionary and already-encoded data.</summary>
    public PdfStream(PdfDictionary dictionary, byte[] data)
        : base(dictionary)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
    }

    /// <summary>The raw payload, as stored — still filtered.</summary>
    public byte[] Data
    {
        get => _data;
        set
        {
            _data = value ?? throw new ArgumentNullException(nameof(value));
            _decodedCache = null;
        }
    }

    /// <summary>The filter names applied to this stream, outermost first.</summary>
    public IReadOnlyList<string> FilterNames =>
        [.. GetArray(PdfName.Filter).OfType<PdfName>().Select(n => n.Value)];

    /// <summary>
    /// True when the stream's outermost filter is an image codec, so the payload is compressed
    /// image data rather than anything this library decodes.
    /// </summary>
    public bool IsImageCodec => FilterNames.Count > 0 && PdfFilters.IsImageCodec(FilterNames[^1]);

    /// <summary>
    /// The payload with every filter reversed. Image-codec filters are left applied, because their
    /// output is a JPEG or JPEG 2000 file rather than raw bytes.
    /// </summary>
    public byte[] Decoded => _decodedCache ??= Decode();

    /// <summary>Replaces the payload, compressing it with Flate.</summary>
    public void SetDecoded(byte[] data, bool compress = true)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (compress)
        {
            var flate = new FlateFilter();
            _data = flate.Encode(data, null);
            this[PdfName.Filter] = PdfName.FlateDecode;
            Remove(PdfName.DecodeParms);
        }
        else
        {
            _data = data;
            Remove(PdfName.Filter);
            Remove(PdfName.DecodeParms);
        }

        _decodedCache = data;
        Set(PdfName.Length, _data.Length);
    }

    /// <summary>Replaces the payload with text, encoded as Latin-1 and Flate-compressed.</summary>
    public void SetText(string text, bool compress = true)
    {
        ArgumentNullException.ThrowIfNull(text);

        // Content streams are a byte grammar, not Unicode text — non-ASCII bytes inside string
        // literals must survive verbatim, which UTF-8 would re-encode. Latin-1 is a byte-preserving
        // round trip for the 0-255 range the operators use.
        _data = Encoding.Latin1.GetBytes(text);
        _decodedCache = _data;

        if (compress)
        {
            SetDecoded(_data);
        }
        else
        {
            Remove(PdfName.Filter);
            Remove(PdfName.DecodeParms);
            Set(PdfName.Length, _data.Length);
        }
    }

    /// <summary>The decoded payload as Latin-1 text — the form content-stream operators are in.</summary>
    public string DecodedText => Encoding.Latin1.GetString(Decoded);

    private byte[] Decode()
    {
        var filters = GetArray(PdfName.Filter).OfType<PdfName>().ToList();
        if (filters.Count == 0)
        {
            return _data;
        }

        // DecodeParms parallels Filter: either one dictionary for one filter, or an array with a
        // (possibly null) entry per filter. An off-by-one here silently applies an image
        // predictor to the wrong stage of the chain.
        var parms = GetArray(PdfName.DecodeParms);
        var result = _data;

        for (var i = 0; i < filters.Count; i++)
        {
            var filter = PdfFilters.Find(filters[i].Value);
            if (filter is null)
            {
                throw new OfficeNetNotSupportedException(
                    $"PDF stream filter '{filters[i].Value}' is not implemented. " +
                    "Supported: FlateDecode, LZWDecode, ASCIIHexDecode, ASCII85Decode, " +
                    "RunLengthDecode, and the image codecs as passthrough.");
            }

            if (filter.IsImageCodec)
            {
                // Stop: everything from here on is the codec's own container format.
                break;
            }

            PdfDictionary? parameters = null;
            if (i < parms.Count)
            {
                var candidate = parms[i];
                if (candidate is PdfReference reference)
                {
                    candidate = reference.Resolve();
                }

                parameters = candidate as PdfDictionary;
            }

            result = filter.Decode(result, parameters);
        }

        return result;
    }

    /// <inheritdoc />
    public override void Write(Stream stream, PdfWriteContext context)
    {
        var payload = context.Protect(_data);

        // /Length must match what is actually written, and encryption changes the length for the
        // AES handlers (they prepend a 16-byte initialisation vector). Writing the pre-encryption
        // length produces a file that opens and shows blank pages.
        Set(PdfName.Length, payload.Length);

        base.Write(stream, context);

        stream.Write("\nstream\n"u8);
        stream.Write(payload, 0, payload.Length);
        stream.Write("\nendstream"u8);
    }

    /// <summary>A copy of this stream with the same dictionary entries and payload.</summary>
    public new PdfStream Clone() => new(base.Clone(), (byte[])_data.Clone());

    public override string ToString() => $"stream({_data.Length} bytes) {base.ToString()}";
}

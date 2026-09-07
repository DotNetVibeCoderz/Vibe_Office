// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Text;
using OfficeNet.Core;
using PdfNet.Objects;
using PdfNet.Security;

namespace PdfNet.Io;

/// <summary>Where an object lives: at a byte offset, or inside a compressed object stream.</summary>
internal readonly record struct XrefEntry(long Offset, int ObjectStreamNumber, int IndexInStream, int Generation)
{
    public bool IsCompressed => ObjectStreamNumber > 0;
}

/// <summary>
/// Reads the object graph out of a PDF file: cross-reference tables, the trailer chain, object
/// streams and decryption.
/// </summary>
/// <remarks>
/// <para>
/// A PDF is read back-to-front. The last line points at the cross-reference table, that table
/// points at every object's byte offset, and its trailer points at the catalogue — and each
/// revision of an incrementally-saved file chains backwards through <c>/Prev</c> to the one before
/// it. Later revisions win, which is why the chain is walked newest-first and an entry already
/// present is never overwritten.
/// </para>
/// <para>
/// When any of that is damaged, <see cref="Rebuild"/> scans the whole file for
/// <c>N G obj</c> headers and reconstructs the table from what it finds. That path recovers files
/// that Acrobat reports as damaged and repairs, and it is the reason this reader opens
/// PDFs that stricter parsers reject outright.
/// </para>
/// </remarks>
public sealed class PdfReader : IPdfObjectResolver, IDisposable
{
    private readonly byte[] _data;
    private readonly Dictionary<int, XrefEntry> _xref = [];
    private readonly Dictionary<int, PdfObject> _cache = [];
    private readonly Dictionary<int, Dictionary<int, PdfObject>> _objectStreamCache = [];
    private readonly HashSet<int> _resolving = [];
    private IPdfEncryption? _encryption;
    private bool _rebuilt;

    private PdfReader(byte[] data) => _data = data;

    /// <summary>The file's trailer dictionary, merged across the revision chain.</summary>
    public PdfDictionary Trailer { get; private set; } = new();

    /// <summary>The document catalogue (<c>/Root</c>).</summary>
    public PdfDictionary Catalog { get; private set; } = new();

    /// <summary>The document information dictionary, when the file has one.</summary>
    public PdfDictionary? Info => Trailer.Get<PdfDictionary>(PdfName.Info);

    /// <summary>The PDF version from the header, for example <c>1.7</c>.</summary>
    public string Version { get; private set; } = "1.7";

    /// <summary>True when the file was encrypted and has been decrypted.</summary>
    public bool WasEncrypted => _encryption is not null;

    /// <summary>The security handler, when the file is encrypted.</summary>
    public StandardSecurityHandler? Security { get; private set; }

    /// <summary>True when the cross-reference table had to be rebuilt by scanning.</summary>
    public bool WasRepaired => _rebuilt;

    /// <summary>The highest object number the file defines.</summary>
    public int MaxObjectNumber => _xref.Count == 0 ? 0 : _xref.Keys.Max();

    /// <summary>Every object number the file defines.</summary>
    public IReadOnlyCollection<int> ObjectNumbers => _xref.Keys;

    /// <summary>Opens a PDF from a file.</summary>
    public static PdfReader Open(string path, string password = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Open(File.ReadAllBytes(path), password);
    }

    /// <summary>Opens a PDF from a stream. The stream is fully read and not retained.</summary>
    public static PdfReader Open(Stream stream, string password = "")
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return Open(buffer.ToArray(), password);
    }

    /// <summary>Opens a PDF from bytes.</summary>
    /// <exception cref="OfficeNetException">The bytes are not a PDF or are unrecoverably damaged.</exception>
    /// <exception cref="OfficeNetPasswordException">The file is encrypted and the password is wrong.</exception>
    public static PdfReader Open(byte[] data, string password = "")
    {
        ArgumentNullException.ThrowIfNull(data);

        var reader = new PdfReader(data);
        reader.Load(password);
        return reader;
    }

    private void Load(string password)
    {
        ReadHeader();

        try
        {
            ReadXrefChain();
        }
        catch (Exception ex) when (ex is OfficeNetException or InvalidOperationException or
                                       IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            Rebuild();
        }

        if (_xref.Count == 0)
        {
            Rebuild();
        }

        SetUpEncryption(password);

        var root = Trailer.Get<PdfDictionary>(PdfName.Root);

        // A trailer without a usable /Root means the xref chain led somewhere wrong even though it
        // parsed. Rebuilding finds the catalogue by scanning for it.
        if (root is null || root.Get(PdfName.Pages) is null && root.DictionaryType != "Catalog")
        {
            if (!_rebuilt)
            {
                Rebuild();
                SetUpEncryption(password);
                root = Trailer.Get<PdfDictionary>(PdfName.Root);
            }
        }

        Catalog = root ?? throw new OfficeNetException(
            "The file has no document catalogue. It is either not a PDF or is damaged beyond " +
            "what scanning for objects can recover.");
    }

    private void ReadHeader()
    {
        // The header is usually at offset 0, but the specification allows leading junk and some
        // web servers prepend it. Acrobat scans the first kilobyte, so this does too.
        var probeLength = Math.Min(_data.Length, 1024);
        var index = _data.AsSpan(0, probeLength).IndexOf("%PDF-"u8);

        if (index < 0)
        {
            throw new OfficeNetException(
                "No %PDF- header found in the first kilobyte. The file is not a PDF.");
        }

        var start = index + 5;
        var end = start;
        while (end < _data.Length && end < start + 8 &&
               (char.IsAsciiDigit((char)_data[end]) || _data[end] == (byte)'.'))
        {
            end++;
        }

        Version = Encoding.ASCII.GetString(_data, start, end - start);
    }

    private void ReadXrefChain()
    {
        var startXref = FindStartXref();
        if (startXref < 0)
        {
            throw new OfficeNetException("No startxref found.");
        }

        var visited = new HashSet<long>();
        var offset = startXref;

        while (offset >= 0 && offset < _data.Length && visited.Add(offset))
        {
            var trailer = ReadXrefSection(offset);
            if (trailer is null)
            {
                break;
            }

            MergeTrailer(trailer);

            // A hybrid-reference file has a classic table for old readers and an xref stream with
            // the objects only new readers can see. Missing /XRefStm loses every compressed object.
            var hybrid = trailer.Get(PdfName.Get("XRefStm"));
            if (hybrid is PdfNumber hybridOffset && visited.Add(hybridOffset.LongValue))
            {
                var hybridTrailer = ReadXrefSection(hybridOffset.LongValue);
                if (hybridTrailer is not null)
                {
                    MergeTrailer(hybridTrailer);
                }
            }

            offset = trailer.Get(PdfName.Prev) is PdfNumber prev ? prev.LongValue : -1;
        }
    }

    private long FindStartXref()
    {
        // startxref lives in the last kilobyte or so; scanning the tail is far cheaper than the
        // whole file and matches where the specification puts it.
        var tailLength = Math.Min(_data.Length, 2048);
        var tailStart = _data.Length - tailLength;
        var tail = _data.AsSpan(tailStart);

        var index = tail.LastIndexOf("startxref"u8);
        if (index < 0)
        {
            return -1;
        }

        var parser = new PdfParser(_data) { Position = tailStart + index + 9 };
        var keyword = parser.ReadKeyword();
        return long.TryParse(keyword, out var offset) ? offset : -1;
    }

    private PdfDictionary? ReadXrefSection(long offset)
    {
        if (offset < 0 || offset >= _data.Length)
        {
            return null;
        }

        var parser = new PdfParser(_data, this) { Position = (int)offset };
        parser.SkipWhitespace();

        if (parser.TryConsumeKeyword("xref"))
        {
            return ReadClassicXref(parser);
        }

        // Not a table, so it must be an xref stream: an indirect object whose /Type is /XRef.
        parser.Position = (int)offset;
        var obj = parser.ParseIndirectObject(out _, out _);

        if (obj is PdfStream xrefStream && xrefStream.DictionaryType == "XRef")
        {
            ReadXrefStream(xrefStream);
            return xrefStream;
        }

        return null;
    }

    private PdfDictionary? ReadClassicXref(PdfParser parser)
    {
        while (true)
        {
            parser.SkipWhitespace();

            if (parser.TryConsumeKeyword("trailer"))
            {
                return parser.ParseObject() as PdfDictionary;
            }

            var startToken = parser.ReadKeyword();
            if (!int.TryParse(startToken, out var firstObject))
            {
                return null;
            }

            var countToken = parser.ReadKeyword();
            if (!int.TryParse(countToken, out var count) || count < 0)
            {
                return null;
            }

            for (var i = 0; i < count; i++)
            {
                parser.SkipWhitespace();

                var offsetToken = parser.ReadKeyword();
                var generationToken = parser.ReadKeyword();
                var typeToken = parser.ReadKeyword();

                if (!long.TryParse(offsetToken, out var entryOffset) ||
                    !int.TryParse(generationToken, out var generation))
                {
                    break;
                }

                // 'f' marks a free (deleted) object. Recording it would shadow a live definition
                // from an earlier revision, which is the whole point of the free list.
                if (typeToken != "n")
                {
                    continue;
                }

                var number = firstObject + i;

                // Earlier entries win: the chain is walked newest-first.
                if (!_xref.ContainsKey(number) && entryOffset > 0)
                {
                    _xref[number] = new XrefEntry(entryOffset, 0, 0, generation);
                }
            }
        }
    }

    private void ReadXrefStream(PdfStream stream)
    {
        var widths = stream.GetArray(PdfName.W).AsDoubles();
        if (widths.Length < 3)
        {
            return;
        }

        var w0 = (int)widths[0];
        var w1 = (int)widths[1];
        var w2 = (int)widths[2];
        var entryWidth = w0 + w1 + w2;

        if (entryWidth <= 0)
        {
            return;
        }

        var data = stream.Decoded;

        // /Index lists (start, count) pairs; its absence means one range starting at zero.
        var index = stream.GetArray(PdfName.Index).AsDoubles();
        if (index.Length == 0)
        {
            index = [0, stream.GetInt(PdfName.Size, data.Length / entryWidth)];
        }

        var position = 0;

        for (var pair = 0; pair + 1 < index.Length; pair += 2)
        {
            var first = (int)index[pair];
            var count = (int)index[pair + 1];

            for (var i = 0; i < count && position + entryWidth <= data.Length; i++, position += entryWidth)
            {
                // A zero-width type field means type 1, the default, per the specification.
                var type = w0 == 0 ? 1L : ReadField(data, position, w0);
                var field2 = ReadField(data, position + w0, w1);
                var field3 = ReadField(data, position + w0 + w1, w2);

                var number = first + i;
                if (_xref.ContainsKey(number))
                {
                    continue;
                }

                switch (type)
                {
                    case 1:
                        if (field2 > 0)
                        {
                            _xref[number] = new XrefEntry(field2, 0, 0, (int)field3);
                        }

                        break;

                    case 2:
                        _xref[number] = new XrefEntry(0, (int)field2, (int)field3, 0);
                        break;

                    // Type 0 is a free entry.
                }
            }
        }
    }

    private static long ReadField(byte[] data, int offset, int width)
    {
        long value = 0;
        for (var i = 0; i < width; i++)
        {
            value = value << 8 | data[offset + i];
        }

        return value;
    }

    private void MergeTrailer(PdfDictionary trailer)
    {
        // Newest wins, and the chain is walked newest-first, so only absent keys are taken.
        foreach (var (key, value) in trailer)
        {
            if (!Trailer.ContainsKey(key))
            {
                Trailer[key] = value;
            }
        }
    }

    /// <summary>
    /// Reconstructs the cross-reference table by scanning the whole file for object headers.
    /// </summary>
    public void Rebuild()
    {
        _rebuilt = true;
        _xref.Clear();
        _cache.Clear();
        _objectStreamCache.Clear();

        var parser = new PdfParser(_data, this);
        var position = 0;

        while (position < _data.Length)
        {
            var found = parser.IndexOf("obj"u8, position);
            if (found < 0)
            {
                break;
            }

            // Walk backwards over " G N" to find the header's real start.
            var scan = found - 1;
            while (scan >= 0 && PdfParser.IsWhitespace(_data[scan]))
            {
                scan--;
            }

            var generationEnd = scan + 1;
            while (scan >= 0 && char.IsAsciiDigit((char)_data[scan]))
            {
                scan--;
            }

            var generationStart = scan + 1;

            while (scan >= 0 && PdfParser.IsWhitespace(_data[scan]))
            {
                scan--;
            }

            var numberEnd = scan + 1;
            while (scan >= 0 && char.IsAsciiDigit((char)_data[scan]))
            {
                scan--;
            }

            var numberStart = scan + 1;

            if (numberEnd > numberStart && generationEnd > generationStart &&
                int.TryParse(Encoding.ASCII.GetString(_data, numberStart, numberEnd - numberStart), out var number) &&
                int.TryParse(Encoding.ASCII.GetString(_data, generationStart, generationEnd - generationStart), out var generation))
            {
                // A later definition of the same object number supersedes an earlier one — an
                // incrementally saved file appends its updates, so the last one found is current.
                _xref[number] = new XrefEntry(numberStart, 0, 0, generation);
            }

            position = found + 3;
        }

        // Object streams found by the scan hold objects that have no header of their own.
        foreach (var number in _xref.Keys.ToList())
        {
            if (ResolveDirect(number) is PdfStream { DictionarySubtype: null } stream &&
                stream.DictionaryType == "ObjStm")
            {
                IndexObjectStream(number, stream);
            }
        }

        RecoverTrailer();
    }

    private void IndexObjectStream(int streamNumber, PdfStream stream)
    {
        var count = stream.GetInt(PdfName.N);
        var first = stream.GetInt(PdfName.First);
        var decoded = stream.Decoded;

        var headerParser = new PdfParser(decoded, this);

        for (var i = 0; i < count; i++)
        {
            var numberToken = headerParser.ReadKeyword();
            var offsetToken = headerParser.ReadKeyword();

            if (!int.TryParse(numberToken, out var objectNumber) ||
                !int.TryParse(offsetToken, out _) || headerParser.Position > first)
            {
                break;
            }

            _xref.TryAdd(objectNumber, new XrefEntry(0, streamNumber, i, 0));
        }
    }

    private void RecoverTrailer()
    {
        // Prefer a real trailer dictionary when one survives, because it carries /Encrypt and /ID
        // which cannot be reconstructed from the objects.
        var parser = new PdfParser(_data, this);
        var position = _data.Length;

        while (position > 0)
        {
            var found = parser.LastIndexOf("trailer"u8, position);
            if (found < 0)
            {
                break;
            }

            parser.Position = found + 7;
            if (parser.ParseObject() is PdfDictionary trailer)
            {
                MergeTrailer(trailer);
                if (Trailer.ContainsKey(PdfName.Root))
                {
                    return;
                }
            }

            position = found;
        }

        if (Trailer.Get<PdfDictionary>(PdfName.Root) is not null)
        {
            return;
        }

        // No usable trailer: find the catalogue among the objects. An xref stream's dictionary is
        // also a trailer, so those are checked before falling back to a /Type /Catalog scan.
        foreach (var number in _xref.Keys.OrderByDescending(n => n))
        {
            if (ResolveDirect(number) is PdfStream { } stream && stream.DictionaryType == "XRef" &&
                stream.ContainsKey(PdfName.Root))
            {
                MergeTrailer(stream);
                if (Trailer.Get<PdfDictionary>(PdfName.Root) is not null)
                {
                    return;
                }
            }
        }

        foreach (var number in _xref.Keys.OrderBy(n => n))
        {
            if (ResolveDirect(number) is PdfDictionary dictionary &&
                dictionary.DictionaryType == "Catalog")
            {
                Trailer[PdfName.Root] = new PdfReference(number, 0, this);
                return;
            }
        }

        // Last resort: a /Type /Pages node with no parent is the page tree root, and a catalogue
        // can be synthesised around it. This recovers files whose catalogue object is gone.
        foreach (var number in _xref.Keys.OrderBy(n => n))
        {
            if (ResolveDirect(number) is PdfDictionary dictionary &&
                dictionary.DictionaryType == "Pages" && dictionary.Get(PdfName.Parent) is null)
            {
                var catalog = new PdfDictionary();
                catalog[PdfName.Type] = PdfName.Catalog;
                catalog[PdfName.Pages] = new PdfReference(number, 0, this);
                Trailer[PdfName.Root] = catalog;
                return;
            }
        }
    }

    private void SetUpEncryption(string password)
    {
        var encryptEntry = Trailer[PdfName.Encrypt];
        if (encryptEntry is null)
        {
            return;
        }

        // The /Encrypt dictionary is itself unencrypted, and resolving it must happen before the
        // handler exists — which is why _encryption is still null at this point.
        var encryptDictionary = encryptEntry switch
        {
            PdfDictionary direct => direct,
            PdfReference reference => reference.WithResolver(this).Resolve() as PdfDictionary,
            _ => null,
        };

        if (encryptDictionary is null)
        {
            return;
        }

        var idArray = Trailer.GetArray(PdfName.Id);
        var firstId = idArray.Count > 0 && idArray[0] is PdfString id ? id.Value : [];

        Security = StandardSecurityHandler.Open(encryptDictionary, firstId, password);
        _encryption = Security;

        // Anything already parsed was parsed without decryption and is wrong.
        _cache.Clear();
        _objectStreamCache.Clear();

        if (Trailer.Get<PdfDictionary>(PdfName.Root) is { } root)
        {
            Catalog = root;
        }
    }

    /// <inheritdoc />
    public PdfObject Resolve(int objectNumber, int generationNumber)
    {
        if (_cache.TryGetValue(objectNumber, out var cached))
        {
            return cached;
        }

        // A self-referential object (an /Length pointing at its own object, a cyclic /Parent in a
        // damaged file) would recurse without bound.
        if (!_resolving.Add(objectNumber))
        {
            return PdfNull.Instance;
        }

        try
        {
            var value = ResolveDirect(objectNumber) ?? PdfNull.Instance;
            _cache[objectNumber] = value;
            return value;
        }
        finally
        {
            _resolving.Remove(objectNumber);
        }
    }

    private PdfObject? ResolveDirect(int objectNumber)
    {
        if (!_xref.TryGetValue(objectNumber, out var entry))
        {
            return null;
        }

        return entry.IsCompressed
            ? ResolveFromObjectStream(entry, objectNumber)
            : ResolveFromOffset(entry, objectNumber);
    }

    private PdfObject? ResolveFromOffset(XrefEntry entry, int objectNumber)
    {
        if (entry.Offset < 0 || entry.Offset >= _data.Length)
        {
            return null;
        }

        var parser = new PdfParser(_data, this) { Position = (int)entry.Offset };
        var value = parser.ParseIndirectObject(out var parsedNumber, out var parsedGeneration);

        // A wrong offset gives the wrong object, silently. Verifying the header is what catches an
        // xref table that is off by a few bytes — common in files edited by hand or by a broken
        // incremental save — and triggers the rebuild that fixes it.
        if (value is null || parsedNumber != objectNumber)
        {
            if (!_rebuilt)
            {
                Rebuild();
                return _xref.TryGetValue(objectNumber, out var repaired) && !repaired.IsCompressed
                    ? ResolveFromOffsetNoRepair(repaired, objectNumber)
                    : null;
            }

            return null;
        }

        return Decrypt(value, objectNumber, parsedGeneration);
    }

    private PdfObject? ResolveFromOffsetNoRepair(XrefEntry entry, int objectNumber)
    {
        var parser = new PdfParser(_data, this) { Position = (int)entry.Offset };
        var value = parser.ParseIndirectObject(out var parsedNumber, out var parsedGeneration);
        return value is null || parsedNumber != objectNumber
            ? null
            : Decrypt(value, objectNumber, parsedGeneration);
    }

    private PdfObject? ResolveFromObjectStream(XrefEntry entry, int objectNumber)
    {
        if (!_objectStreamCache.TryGetValue(entry.ObjectStreamNumber, out var objects))
        {
            objects = ParseObjectStream(entry.ObjectStreamNumber);
            _objectStreamCache[entry.ObjectStreamNumber] = objects;
        }

        return objects.GetValueOrDefault(objectNumber);
    }

    private Dictionary<int, PdfObject> ParseObjectStream(int streamNumber)
    {
        var result = new Dictionary<int, PdfObject>();

        if (Resolve(streamNumber, 0) is not PdfStream stream)
        {
            return result;
        }

        var count = stream.GetInt(PdfName.N);
        var first = stream.GetInt(PdfName.First);
        var decoded = stream.Decoded;

        if (first <= 0 || first > decoded.Length)
        {
            return result;
        }

        // The header is a list of (object number, offset-from-/First) pairs.
        var headerParser = new PdfParser(decoded, this);
        var entries = new List<(int Number, int Offset)>(count);

        for (var i = 0; i < count; i++)
        {
            var numberToken = headerParser.ReadKeyword();
            var offsetToken = headerParser.ReadKeyword();

            if (!int.TryParse(numberToken, out var number) || !int.TryParse(offsetToken, out var offset))
            {
                break;
            }

            entries.Add((number, offset));
        }

        foreach (var (number, offset) in entries)
        {
            var absolute = first + offset;
            if (absolute < 0 || absolute >= decoded.Length)
            {
                continue;
            }

            var parser = new PdfParser(decoded, this) { Position = absolute };
            var value = parser.ParseObject();

            if (value is null)
            {
                continue;
            }

            value.ObjectNumber = number;

            // Objects inside an object stream are NOT individually encrypted — the containing
            // stream already was. Decrypting them again produces garbage that still parses.
            result[number] = value;
        }

        return result;
    }

    private PdfObject Decrypt(PdfObject value, int objectNumber, int generationNumber)
    {
        if (_encryption is null)
        {
            return value;
        }

        DecryptInPlace(value, objectNumber, generationNumber, 0);
        return value;
    }

    private void DecryptInPlace(PdfObject value, int objectNumber, int generationNumber, int depth)
    {
        if (depth > 64)
        {
            return;
        }

        switch (value)
        {
            case PdfStream stream:
                foreach (var key in stream.Keys.ToList())
                {
                    if (stream[key] is { } child)
                    {
                        DecryptInPlace(child, objectNumber, generationNumber, depth + 1);
                    }
                }

                // An XRef stream is written before encryption is applied and is never encrypted;
                // decrypting it destroys the table this reader just used.
                if (stream.DictionaryType != "XRef")
                {
                    stream.Data = _encryption!.Decrypt(stream.Data, objectNumber, generationNumber);
                }

                break;

            case PdfDictionary dictionary:
                foreach (var key in dictionary.Keys.ToList())
                {
                    if (dictionary[key] is { } child)
                    {
                        DecryptInPlace(child, objectNumber, generationNumber, depth + 1);
                    }
                }

                break;

            case PdfArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    DecryptInPlace(array[i], objectNumber, generationNumber, depth + 1);
                }

                break;

            case PdfString text:
            {
                var plain = _encryption!.Decrypt(text.Value, objectNumber, generationNumber);
                Array.Resize(ref plain, plain.Length);

                // PdfString.Value is init-only by design, so the decrypted bytes are copied over
                // the original buffer. They are the same length for RC4 and shorter for AES.
                if (plain.Length <= text.Value.Length)
                {
                    plain.CopyTo(text.Value, 0);
                    if (plain.Length < text.Value.Length)
                    {
                        Array.Clear(text.Value, plain.Length, text.Value.Length - plain.Length);
                    }
                }

                break;
            }
        }
    }

    /// <summary>Resolves a reference, or returns the object unchanged when it is direct.</summary>
    public PdfObject? Follow(PdfObject? value)
    {
        for (var depth = 0; value is PdfReference reference && depth < 32; depth++)
        {
            value = reference.WithResolver(this).Resolve();
        }

        return value is PdfNull ? null : value;
    }

    /// <summary>Every indirect object in the file, resolved. Used by merge and by rewriting.</summary>
    public IEnumerable<(int Number, PdfObject Object)> AllObjects()
    {
        foreach (var number in _xref.Keys.OrderBy(n => n))
        {
            var value = Resolve(number, 0);
            if (value is not PdfNull)
            {
                yield return (number, value);
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _cache.Clear();
        _objectStreamCache.Clear();
        _xref.Clear();
    }
}

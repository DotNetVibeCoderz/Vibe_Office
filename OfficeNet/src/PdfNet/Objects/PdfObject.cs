// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Collections;
using System.Globalization;
using System.Text;

namespace PdfNet.Objects;

/// <summary>
/// The base of the eight COS object types a PDF file is built from (ISO 32000-1 §7.3).
/// </summary>
/// <remarks>
/// PDF's object model is small and closed — null, boolean, number, string, name, array, dictionary,
/// stream — plus indirect references between them. Everything else in a PDF, from a page tree to a
/// digital signature, is those eight types arranged by convention. Modelling them faithfully is what
/// lets this library round-trip a file it does not fully understand.
/// </remarks>
public abstract class PdfObject
{
    /// <summary>The object number when this object is indirect; 0 when it is direct.</summary>
    public int ObjectNumber { get; internal set; }

    /// <summary>The generation number for an indirect object.</summary>
    public int GenerationNumber { get; internal set; }

    /// <summary>True when the object is stored as a numbered indirect object.</summary>
    public bool IsIndirect => ObjectNumber > 0;

    /// <summary>Writes the object's PDF syntax to a stream.</summary>
    public abstract void Write(Stream stream, PdfWriteContext context);

    /// <summary>Wraps a CLR value as the matching PDF object.</summary>
    /// <exception cref="ArgumentException">The value has no PDF equivalent.</exception>
    public static PdfObject From(object? value) => value switch
    {
        null => PdfNull.Instance,
        PdfObject pdf => pdf,
        bool b => PdfBoolean.Get(b),
        int i => new PdfNumber(i),
        long l => new PdfNumber(l),
        float f => new PdfNumber(f),
        double d => new PdfNumber(d),
        decimal m => new PdfNumber((double)m),
        string s => new PdfString(s),
        DateTime dt => PdfString.FromDate(dt),
        byte[] bytes => new PdfString(bytes),
        IEnumerable<object?> items => new PdfArray(items.Select(From)),
        _ => throw new ArgumentException(
            $"A {value.GetType().Name} has no PDF object equivalent.", nameof(value)),
    };
}

/// <summary>State shared across one serialisation pass: offsets, encryption, indirect numbering.</summary>
public sealed class PdfWriteContext
{
    /// <summary>The object currently being written, used by encryption to derive the object key.</summary>
    public int CurrentObjectNumber { get; set; }

    /// <summary>The generation of the object currently being written.</summary>
    public int CurrentGenerationNumber { get; set; }

    /// <summary>The encryption in force, or <c>null</c> for an unencrypted file.</summary>
    public Security.IPdfEncryption? Encryption { get; set; }

    /// <summary>
    /// True while writing the encryption dictionary itself, which must never be encrypted.
    /// </summary>
    public bool SuppressEncryption { get; set; }

    /// <summary>Encrypts a string or stream payload when encryption is in force.</summary>
    public byte[] Protect(byte[] data)
    {
        if (Encryption is null || SuppressEncryption)
        {
            return data;
        }

        return Encryption.Encrypt(data, CurrentObjectNumber, CurrentGenerationNumber);
    }
}

/// <summary>The PDF <c>null</c> object.</summary>
public sealed class PdfNull : PdfObject
{
    /// <summary>The single instance; null carries no state.</summary>
    public static readonly PdfNull Instance = new();

    private PdfNull()
    {
    }

    /// <inheritdoc />
    public override void Write(Stream stream, PdfWriteContext context) => stream.Write("null"u8);

    public override string ToString() => "null";
}

/// <summary>A PDF boolean.</summary>
public sealed class PdfBoolean : PdfObject
{
    /// <summary>The shared <c>true</c>.</summary>
    public static readonly PdfBoolean True = new(true);

    /// <summary>The shared <c>false</c>.</summary>
    public static readonly PdfBoolean False = new(false);

    private PdfBoolean(bool value) => Value = value;

    /// <summary>The value.</summary>
    public bool Value { get; }

    /// <summary>The shared instance for a value.</summary>
    public static PdfBoolean Get(bool value) => value ? True : False;

    /// <inheritdoc />
    public override void Write(Stream stream, PdfWriteContext context) =>
        stream.Write(Value ? "true"u8 : "false"u8);

    public override string ToString() => Value ? "true" : "false";
}

/// <summary>A PDF number, integer or real.</summary>
public sealed class PdfNumber : PdfObject
{
    /// <summary>Creates an integer.</summary>
    public PdfNumber(long value)
    {
        LongValue = value;
        DoubleValue = value;
        IsInteger = true;
    }

    /// <summary>Creates a real, collapsing to an integer when the value is whole.</summary>
    public PdfNumber(double value)
    {
        DoubleValue = value;

        // A whole-valued real must serialise without a decimal point: several PDF constructs
        // (array indices, /Length, xref offsets) are integers by specification and a consumer is
        // entitled to reject "10.0" where it expects "10".
        if (Math.Abs(value % 1) < double.Epsilon && Math.Abs(value) < long.MaxValue)
        {
            LongValue = (long)value;
            IsInteger = true;
        }
        else
        {
            LongValue = (long)Math.Round(value);
            IsInteger = false;
        }
    }

    /// <summary>True when the number serialises without a fractional part.</summary>
    public bool IsInteger { get; }

    /// <summary>The value as a long.</summary>
    public long LongValue { get; }

    /// <summary>The value as a double.</summary>
    public double DoubleValue { get; }

    /// <summary>The value as an int.</summary>
    public int IntValue => (int)LongValue;

    /// <inheritdoc />
    public override void Write(Stream stream, PdfWriteContext context) =>
        stream.Write(Encoding.ASCII.GetBytes(ToString()));

    public override string ToString() =>
        IsInteger
            ? LongValue.ToString(CultureInfo.InvariantCulture)
            // Six decimals is what the specification's own examples use and is well beyond the
            // precision of any coordinate space; "R" would emit exponent notation, which PDF's
            // grammar does not accept at all.
            : DoubleValue.ToString("0.######", CultureInfo.InvariantCulture);
}

/// <summary>
/// A PDF name such as <c>/Type</c> — an atom, not a string.
/// </summary>
public sealed class PdfName : PdfObject, IEquatable<PdfName>
{
    private static readonly Dictionary<string, PdfName> Interned = [];
    private static readonly Lock InternLock = new();

    /// <summary>Creates a name from its value, without the leading slash.</summary>
    public PdfName(string value) => Value = value ?? throw new ArgumentNullException(nameof(value));

    /// <summary>The name's text, without the leading slash.</summary>
    public string Value { get; }

    /// <summary>Returns a shared instance for a name, so equality checks are usually reference checks.</summary>
    public static PdfName Get(string value)
    {
        lock (InternLock)
        {
            if (Interned.TryGetValue(value, out var existing))
            {
                return existing;
            }

            var created = new PdfName(value);
            Interned[value] = created;
            return created;
        }
    }

    /// <inheritdoc />
    public override void Write(Stream stream, PdfWriteContext context)
    {
        stream.WriteByte((byte)'/');

        foreach (var b in Encoding.UTF8.GetBytes(Value))
        {
            // Delimiters, whitespace and '#' itself must be hex-escaped. A name containing a raw
            // space parses as two tokens and silently truncates the name.
            var mustEscape = b < 0x21 || b > 0x7E || b is (byte)'#' or (byte)'/' or (byte)'%' or
                (byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or (byte)'[' or (byte)']' or
                (byte)'{' or (byte)'}';

            if (mustEscape)
            {
                stream.WriteByte((byte)'#');
                stream.Write(Encoding.ASCII.GetBytes(b.ToString("X2")));
            }
            else
            {
                stream.WriteByte(b);
            }
        }
    }

    public bool Equals(PdfName? other) => other is not null && Value == other.Value;
    public override bool Equals(object? obj) => obj is PdfName other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);
    public override string ToString() => "/" + Value;

    public static bool operator ==(PdfName? a, PdfName? b) => a?.Value == b?.Value;
    public static bool operator !=(PdfName? a, PdfName? b) => a?.Value != b?.Value;

    public static implicit operator PdfName(string value) => Get(value);

    // The names used often enough that interning them at startup is worth it.

    /// <summary><c>/Type</c>.</summary>
    public static readonly PdfName Type = Get("Type");

    /// <summary><c>/Subtype</c>.</summary>
    public static readonly PdfName Subtype = Get("Subtype");

    /// <summary><c>/Length</c>.</summary>
    public static readonly PdfName Length = Get("Length");

    /// <summary><c>/Filter</c>.</summary>
    public static readonly PdfName Filter = Get("Filter");

    /// <summary><c>/DecodeParms</c>.</summary>
    public static readonly PdfName DecodeParms = Get("DecodeParms");

    /// <summary><c>/Root</c>.</summary>
    public static readonly PdfName Root = Get("Root");

    /// <summary><c>/Pages</c>.</summary>
    public static readonly PdfName Pages = Get("Pages");

    /// <summary><c>/Kids</c>.</summary>
    public static readonly PdfName Kids = Get("Kids");

    /// <summary><c>/Count</c>.</summary>
    public static readonly PdfName Count = Get("Count");

    /// <summary><c>/Page</c>.</summary>
    public static readonly PdfName Page = Get("Page");

    /// <summary><c>/Parent</c>.</summary>
    public static readonly PdfName Parent = Get("Parent");

    /// <summary><c>/MediaBox</c>.</summary>
    public static readonly PdfName MediaBox = Get("MediaBox");

    /// <summary><c>/CropBox</c>.</summary>
    public static readonly PdfName CropBox = Get("CropBox");

    /// <summary><c>/Resources</c>.</summary>
    public static readonly PdfName Resources = Get("Resources");

    /// <summary><c>/Contents</c>.</summary>
    public static readonly PdfName Contents = Get("Contents");

    /// <summary><c>/Rotate</c>.</summary>
    public static readonly PdfName Rotate = Get("Rotate");

    /// <summary><c>/Annots</c>.</summary>
    public static readonly PdfName Annots = Get("Annots");

    /// <summary><c>/Font</c>.</summary>
    public static readonly PdfName Font = Get("Font");

    /// <summary><c>/XObject</c>.</summary>
    public static readonly PdfName XObject = Get("XObject");

    /// <summary><c>/Encrypt</c>.</summary>
    public static readonly PdfName Encrypt = Get("Encrypt");

    /// <summary><c>/Info</c>.</summary>
    public static readonly PdfName Info = Get("Info");

    /// <summary><c>/ID</c>.</summary>
    public static readonly PdfName Id = Get("ID");

    /// <summary><c>/Size</c>.</summary>
    public static readonly PdfName Size = Get("Size");

    /// <summary><c>/Prev</c>.</summary>
    public static readonly PdfName Prev = Get("Prev");

    /// <summary><c>/W</c>.</summary>
    public static readonly PdfName W = Get("W");

    /// <summary><c>/Index</c>.</summary>
    public static readonly PdfName Index = Get("Index");

    /// <summary><c>/ObjStm</c>.</summary>
    public static readonly PdfName ObjStm = Get("ObjStm");

    /// <summary><c>/XRef</c>.</summary>
    public static readonly PdfName XRef = Get("XRef");

    /// <summary><c>/N</c>.</summary>
    public static readonly PdfName N = Get("N");

    /// <summary><c>/First</c>.</summary>
    public static readonly PdfName First = Get("First");

    /// <summary><c>/FlateDecode</c>.</summary>
    public static readonly PdfName FlateDecode = Get("FlateDecode");

    /// <summary><c>/Catalog</c>.</summary>
    public static readonly PdfName Catalog = Get("Catalog");
}

/// <summary>
/// A PDF string: a byte sequence, not text, until an encoding says otherwise.
/// </summary>
/// <remarks>
/// The distinction matters. A string in a content stream is bytes indexed through the current
/// font's encoding; a string in a document-information dictionary is PDFDocEncoding or UTF-16BE
/// depending on whether it starts with a byte-order mark. Decoding at parse time would lose the
/// bytes needed for the first case, so the bytes are what is stored and decoding is explicit.
/// </remarks>
public sealed class PdfString : PdfObject
{
    /// <summary>Creates a string from raw bytes.</summary>
    public PdfString(byte[] value) => Value = value ?? throw new ArgumentNullException(nameof(value));

    /// <summary>
    /// Creates a string from text, choosing PDFDocEncoding when the text is representable and
    /// UTF-16BE with a byte-order mark when it is not.
    /// </summary>
    public PdfString(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var isLatin1 = text.All(c => c <= 0xFF);
        if (isLatin1)
        {
            Value = new byte[text.Length];
            for (var i = 0; i < text.Length; i++)
            {
                Value[i] = (byte)text[i];
            }
        }
        else
        {
            // The BOM is what tells a consumer this is UTF-16BE rather than PDFDocEncoding;
            // without it, Indonesian and CJK text renders as mojibake in every reader.
            var utf16 = Encoding.BigEndianUnicode.GetBytes(text);
            Value = new byte[utf16.Length + 2];
            Value[0] = 0xFE;
            Value[1] = 0xFF;
            utf16.CopyTo(Value, 2);
        }
    }

    /// <summary>The raw bytes.</summary>
    public byte[] Value { get; }

    /// <summary>True when the string should be written in hex form (<c>&lt;...&gt;</c>).</summary>
    public bool IsHex { get; init; }

    /// <summary>
    /// The string decoded as text: UTF-16BE when it carries a BOM, otherwise PDFDocEncoding.
    /// </summary>
    public string AsText()
    {
        if (Value.Length >= 2 && Value[0] == 0xFE && Value[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(Value, 2, Value.Length - 2);
        }

        if (Value.Length >= 3 && Value[0] == 0xEF && Value[1] == 0xBB && Value[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(Value, 3, Value.Length - 3);
        }

        return Text.PdfDocEncoding.GetString(Value);
    }

    /// <summary>Parses a PDF date string (<c>D:YYYYMMDDHHmmSSOHH'mm</c>).</summary>
    public DateTimeOffset? AsDate() => ParseDate(AsText());

    /// <summary>Creates a PDF date string.</summary>
    public static PdfString FromDate(DateTimeOffset value)
    {
        var offset = value.Offset;
        var sign = offset < TimeSpan.Zero ? '-' : '+';
        var text = string.Create(CultureInfo.InvariantCulture,
            $"D:{value:yyyyMMddHHmmss}{sign}{Math.Abs(offset.Hours):00}'{Math.Abs(offset.Minutes):00}'");
        return new PdfString(text);
    }

    /// <summary>Parses a PDF date string, returning <c>null</c> when it is not one.</summary>
    public static DateTimeOffset? ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var s = text.Trim();
        if (s.StartsWith("D:", StringComparison.Ordinal))
        {
            s = s[2..];
        }

        // Every field after the year is optional, so the string is parsed by progressive
        // truncation rather than against one fixed format.
        static int Slice(string s, int at, int len, int fallback) =>
            at + len <= s.Length && int.TryParse(s.AsSpan(at, len), out var v) ? v : fallback;

        if (s.Length < 4 || !int.TryParse(s.AsSpan(0, 4), out var year))
        {
            return null;
        }

        var month = Math.Clamp(Slice(s, 4, 2, 1), 1, 12);
        var day = Math.Clamp(Slice(s, 6, 2, 1), 1, DateTime.DaysInMonth(year, month));
        var hour = Math.Clamp(Slice(s, 8, 2, 0), 0, 23);
        var minute = Math.Clamp(Slice(s, 10, 2, 0), 0, 59);
        var second = Math.Clamp(Slice(s, 12, 2, 0), 0, 59);

        var offset = TimeSpan.Zero;
        if (s.Length > 14)
        {
            var sign = s[14];
            if (sign is '+' or '-')
            {
                var offsetHours = Slice(s, 15, 2, 0);
                var offsetMinutes = Slice(s, 18, 2, 0);
                offset = new TimeSpan(offsetHours, offsetMinutes, 0);
                if (sign == '-')
                {
                    offset = -offset;
                }
            }
        }

        try
        {
            return new DateTimeOffset(year, month, day, hour, minute, second, offset);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public override void Write(Stream stream, PdfWriteContext context)
    {
        var data = context.Protect(Value);

        if (IsHex)
        {
            stream.WriteByte((byte)'<');
            stream.Write(Encoding.ASCII.GetBytes(Convert.ToHexString(data)));
            stream.WriteByte((byte)'>');
            return;
        }

        stream.WriteByte((byte)'(');
        foreach (var b in data)
        {
            switch (b)
            {
                case (byte)'(':
                case (byte)')':
                case (byte)'\\':
                    stream.WriteByte((byte)'\\');
                    stream.WriteByte(b);
                    break;
                case (byte)'\r':
                    stream.Write("\\r"u8);
                    break;
                case (byte)'\n':
                    stream.Write("\\n"u8);
                    break;
                default:
                    stream.WriteByte(b);
                    break;
            }
        }

        stream.WriteByte((byte)')');
    }

    public override string ToString() => AsText();
}

/// <summary>A PDF array.</summary>
public sealed class PdfArray : PdfObject, IList<PdfObject>
{
    private readonly List<PdfObject> _items;

    /// <summary>Creates an empty array.</summary>
    public PdfArray() => _items = [];

    /// <summary>Creates an array from items.</summary>
    public PdfArray(IEnumerable<PdfObject> items) => _items = [.. items];

    /// <summary>Creates an array of numbers, the shape rectangles and matrices take.</summary>
    public PdfArray(params double[] numbers) => _items = [.. numbers.Select(n => (PdfObject)new PdfNumber(n))];

    /// <inheritdoc />
    public PdfObject this[int index]
    {
        get => _items[index];
        set => _items[index] = value;
    }

    /// <inheritdoc />
    public int Count => _items.Count;

    /// <inheritdoc />
    public bool IsReadOnly => false;

    /// <inheritdoc />
    public void Add(PdfObject item) => _items.Add(item);

    /// <summary>Adds a CLR value, wrapping it as a PDF object.</summary>
    public void AddValue(object? value) => _items.Add(From(value));

    /// <inheritdoc />
    public void Clear() => _items.Clear();

    /// <inheritdoc />
    public bool Contains(PdfObject item) => _items.Contains(item);

    /// <inheritdoc />
    public void CopyTo(PdfObject[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

    /// <inheritdoc />
    public IEnumerator<PdfObject> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc />
    public int IndexOf(PdfObject item) => _items.IndexOf(item);

    /// <inheritdoc />
    public void Insert(int index, PdfObject item) => _items.Insert(index, item);

    /// <inheritdoc />
    public bool Remove(PdfObject item) => _items.Remove(item);

    /// <inheritdoc />
    public void RemoveAt(int index) => _items.RemoveAt(index);

    /// <summary>The array's items as doubles, for rectangles and matrices.</summary>
    public double[] AsDoubles() => [.. _items.Select(i => i is PdfNumber n ? n.DoubleValue : 0)];

    /// <inheritdoc />
    public override void Write(Stream stream, PdfWriteContext context)
    {
        stream.WriteByte((byte)'[');
        for (var i = 0; i < _items.Count; i++)
        {
            if (i > 0)
            {
                stream.WriteByte((byte)' ');
            }

            _items[i].Write(stream, context);
        }

        stream.WriteByte((byte)']');
    }

    public override string ToString() => $"[{string.Join(' ', _items)}]";
}

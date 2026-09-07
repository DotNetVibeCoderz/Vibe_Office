// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Collections;
using System.Text;

namespace PdfNet.Objects;

/// <summary>Resolves indirect references to the objects they name.</summary>
public interface IPdfObjectResolver
{
    /// <summary>Returns the object a reference names, or <see cref="PdfNull.Instance"/>.</summary>
    PdfObject Resolve(int objectNumber, int generationNumber);
}

/// <summary>
/// An indirect reference (<c>12 0 R</c>).
/// </summary>
/// <remarks>
/// References are what make a PDF a graph rather than a tree, and they must stay unresolved in the
/// model: a page's <c>/Parent</c> points back at the page tree, so eagerly following references
/// would loop forever on the first real document. Resolution happens on demand through the
/// document's resolver, and the same reference resolves to the same object instance because the
/// reader caches by object number.
/// </remarks>
public sealed class PdfReference : PdfObject, IEquatable<PdfReference>
{
    private readonly IPdfObjectResolver? _resolver;

    /// <summary>Creates a reference bound to a resolver.</summary>
    public PdfReference(int objectNumber, int generationNumber, IPdfObjectResolver? resolver = null)
    {
        Number = objectNumber;
        Generation = generationNumber;
        _resolver = resolver;
    }

    /// <summary>The referenced object number.</summary>
    public int Number { get; }

    /// <summary>The referenced generation number.</summary>
    public int Generation { get; }

    /// <summary>Follows the reference. Returns <see cref="PdfNull.Instance"/> when unresolvable.</summary>
    public PdfObject Resolve() => _resolver?.Resolve(Number, Generation) ?? PdfNull.Instance;

    /// <summary>Rebinds this reference to a different resolver, for objects moved between documents.</summary>
    public PdfReference WithResolver(IPdfObjectResolver resolver) => new(Number, Generation, resolver);

    /// <inheritdoc />
    public override void Write(Stream stream, PdfWriteContext context) =>
        stream.Write(Encoding.ASCII.GetBytes($"{Number} {Generation} R"));

    public bool Equals(PdfReference? other) =>
        other is not null && Number == other.Number && Generation == other.Generation;

    public override bool Equals(object? obj) => obj is PdfReference other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Number, Generation);
    public override string ToString() => $"{Number} {Generation} R";
}

/// <summary>A PDF dictionary: a map from names to objects.</summary>
public class PdfDictionary : PdfObject, IEnumerable<KeyValuePair<PdfName, PdfObject>>
{
    private readonly Dictionary<PdfName, PdfObject> _entries;

    /// <summary>Creates an empty dictionary.</summary>
    public PdfDictionary() => _entries = [];

    /// <summary>Creates a dictionary from entries.</summary>
    public PdfDictionary(IEnumerable<KeyValuePair<PdfName, PdfObject>> entries) =>
        _entries = new Dictionary<PdfName, PdfObject>(entries);

    /// <summary>The number of entries.</summary>
    public int Count => _entries.Count;

    /// <summary>The keys, in no particular order.</summary>
    public IEnumerable<PdfName> Keys => _entries.Keys;

    /// <summary>
    /// Reads or writes an entry <em>without</em> following indirect references. Reading an absent
    /// key gives <c>null</c>; assigning <c>null</c> removes the key.
    /// </summary>
    public PdfObject? this[PdfName key]
    {
        get => _entries.GetValueOrDefault(key);
        set
        {
            if (value is null)
            {
                _entries.Remove(key);
            }
            else
            {
                _entries[key] = value;
            }
        }
    }

    /// <summary>True when the key is present.</summary>
    public bool ContainsKey(PdfName key) => _entries.ContainsKey(key);

    /// <summary>Removes an entry.</summary>
    public bool Remove(PdfName key) => _entries.Remove(key);

    /// <summary>
    /// Reads an entry and follows it through any chain of indirect references.
    /// </summary>
    /// <remarks>
    /// Almost every read wants this rather than the indexer. <c>/Length</c> in a stream dictionary
    /// is routinely an indirect reference, and so is <c>/Kids</c>; code that takes the indexer's
    /// result and casts it to <see cref="PdfNumber"/> works on files produced by one library and
    /// fails on files produced by another.
    /// </remarks>
    public PdfObject? Get(PdfName key)
    {
        var value = _entries.GetValueOrDefault(key);

        // A malformed file can make references cycle. The depth cap turns an infinite loop into a
        // null, which every caller already handles.
        for (var depth = 0; value is PdfReference reference && depth < 32; depth++)
        {
            value = reference.Resolve();
        }

        return value is PdfNull ? null : value;
    }

    /// <summary>Reads an entry as a specific object type, or <c>null</c> when absent or a different type.</summary>
    public T? Get<T>(PdfName key) where T : PdfObject => Get(key) as T;

    /// <summary>Reads an entry as an integer.</summary>
    public int GetInt(PdfName key, int fallback = 0) =>
        Get(key) is PdfNumber number ? number.IntValue : fallback;

    /// <summary>Reads an entry as a long.</summary>
    public long GetLong(PdfName key, long fallback = 0) =>
        Get(key) is PdfNumber number ? number.LongValue : fallback;

    /// <summary>Reads an entry as a double.</summary>
    public double GetDouble(PdfName key, double fallback = 0) =>
        Get(key) is PdfNumber number ? number.DoubleValue : fallback;

    /// <summary>Reads an entry as a boolean.</summary>
    public bool GetBool(PdfName key, bool fallback = false) =>
        Get(key) is PdfBoolean b ? b.Value : fallback;

    /// <summary>Reads an entry as a name's text.</summary>
    public string? GetName(PdfName key) => Get(key) is PdfName name ? name.Value : null;

    /// <summary>Reads an entry as decoded string text.</summary>
    public string? GetText(PdfName key) => Get(key) is PdfString s ? s.AsText() : null;

    /// <summary>
    /// Reads an entry as an array, wrapping a lone object in a one-element array.
    /// </summary>
    /// <remarks>
    /// PDF allows several keys to hold either a single object or an array of them —
    /// <c>/Contents</c>, <c>/Filter</c>, <c>/DecodeParms</c>, <c>/Annots</c> in practice. Handling
    /// only the array form is the most common way a PDF parser fails on real files.
    /// </remarks>
    public PdfArray GetArray(PdfName key)
    {
        var value = Get(key);
        return value switch
        {
            PdfArray array => array,
            null => [],
            _ => [value],
        };
    }

    /// <summary>Sets an entry to a CLR value.</summary>
    public void Set(PdfName key, object? value)
    {
        if (value is null)
        {
            _entries.Remove(key);
            return;
        }

        _entries[key] = From(value);
    }

    /// <summary>Sets an entry to a name.</summary>
    public void SetName(PdfName key, string value) => _entries[key] = PdfName.Get(value);

    /// <summary>The <c>/Type</c> entry's name, or <c>null</c>.</summary>
    public string? DictionaryType => GetName(PdfName.Type);

    /// <summary>The <c>/Subtype</c> entry's name, or <c>null</c>.</summary>
    public string? DictionarySubtype => GetName(PdfName.Subtype);

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<PdfName, PdfObject>> GetEnumerator() => _entries.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>A shallow copy.</summary>
    public PdfDictionary Clone() => new(_entries);

    /// <inheritdoc />
    public override void Write(Stream stream, PdfWriteContext context)
    {
        stream.Write("<<"u8);

        foreach (var (key, value) in _entries)
        {
            key.Write(stream, context);
            // A name is self-delimiting against '/', '[', '<' and '(' but not against a number or
            // another name's letters, so one space always separates key from value.
            stream.WriteByte((byte)' ');
            value.Write(stream, context);
        }

        stream.Write(">>"u8);
    }

    public override string ToString() =>
        $"<<{string.Join(' ', _entries.Select(e => $"{e.Key} {e.Value}"))}>>";
}

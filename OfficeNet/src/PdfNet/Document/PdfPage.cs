// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Collections;
using System.Text;
using OfficeNet.Core;
using PdfNet.Objects;

namespace PdfNet.Document;

/// <summary>One page of a PDF document.</summary>
public sealed class PdfPage
{
    private readonly PdfDocument _document;

    internal PdfPage(PdfDocument document, PdfDictionary dictionary)
    {
        _document = document;
        Dictionary = dictionary;
    }

    /// <summary>The underlying page dictionary. Use it for entries this library does not model.</summary>
    public PdfDictionary Dictionary { get; }

    /// <summary>The document this page belongs to.</summary>
    public PdfDocument Document => _document;

    /// <summary>
    /// Reads an attribute from this page or, failing that, from its ancestors in the page tree.
    /// </summary>
    /// <remarks>
    /// <c>/MediaBox</c>, <c>/CropBox</c>, <c>/Resources</c> and <c>/Rotate</c> are inheritable: a
    /// document that sets the page size once on the root and never on a page is completely normal,
    /// and reading <c>page.Dictionary[MediaBox]</c> on such a file returns null. Every consumer of
    /// those four keys must walk the <c>/Parent</c> chain, which is what this does.
    /// </remarks>
    public PdfObject? GetInherited(PdfName key)
    {
        var node = Dictionary;

        for (var depth = 0; node is not null && depth < 64; depth++)
        {
            var value = node.Get(key);
            if (value is not null)
            {
                return value;
            }

            node = node.Get<PdfDictionary>(PdfName.Parent);
        }

        return null;
    }

    /// <summary>
    /// The page's size and position in user space, from <c>/MediaBox</c>. Defaults to US Letter,
    /// which is what the specification says to assume.
    /// </summary>
    public PdfRectangle MediaBox
    {
        get => GetInherited(PdfName.MediaBox) is PdfArray array
            ? PdfRectangle.FromArray(array)
            : PageSize.Letter;
        set => Dictionary[PdfName.MediaBox] = value.ToArray();
    }

    /// <summary>The visible region, from <c>/CropBox</c>; equal to <see cref="MediaBox"/> when unset.</summary>
    public PdfRectangle CropBox
    {
        get => GetInherited(PdfName.CropBox) is PdfArray array
            ? PdfRectangle.FromArray(array)
            : MediaBox;
        set => Dictionary[PdfName.CropBox] = value.ToArray();
    }

    /// <summary>The page width in points, accounting for rotation.</summary>
    public double Width => Rotation is 90 or 270 ? CropBox.Height : CropBox.Width;

    /// <summary>The page height in points, accounting for rotation.</summary>
    public double Height => Rotation is 90 or 270 ? CropBox.Width : CropBox.Height;

    /// <summary>
    /// The clockwise display rotation in degrees: 0, 90, 180 or 270.
    /// </summary>
    /// <remarks>
    /// Rotation is display-only — it does not change the content stream's coordinate space. Text
    /// extracted from a rotated page comes out in the order it was drawn, which is the unrotated
    /// order, and that is correct: rotating the page did not reflow the text.
    /// </remarks>
    public int Rotation
    {
        get
        {
            var raw = GetInherited(PdfName.Rotate) is PdfNumber number ? number.IntValue : 0;

            // Normalise: the specification allows any multiple of 90 including negatives, and
            // producers write -90 and 450 in practice.
            var normalized = raw % 360;
            if (normalized < 0)
            {
                normalized += 360;
            }

            return normalized / 90 * 90;
        }
        set
        {
            var normalized = value % 360;
            if (normalized < 0)
            {
                normalized += 360;
            }

            normalized = normalized / 90 * 90;

            if (normalized == 0)
            {
                Dictionary.Remove(PdfName.Rotate);
            }
            else
            {
                Dictionary.Set(PdfName.Rotate, normalized);
            }
        }
    }

    /// <summary>Rotates the page by a further quarter-turn multiple, clockwise.</summary>
    public void Rotate(int degrees) => Rotation += degrees;

    /// <summary>The page's resource dictionary, created when the page has none.</summary>
    public PdfDictionary Resources
    {
        get
        {
            if (GetInherited(PdfName.Resources) is PdfDictionary existing)
            {
                return existing;
            }

            var created = new PdfDictionary();
            Dictionary[PdfName.Resources] = created;
            return created;
        }
    }

    /// <summary>The page's annotations.</summary>
    public PdfArray Annotations
    {
        get
        {
            if (Dictionary.Get(PdfName.Annots) is PdfArray existing)
            {
                return existing;
            }

            var created = new PdfArray();
            Dictionary[PdfName.Annots] = created;
            return created;
        }
    }

    /// <summary>
    /// The page's content streams, concatenated and decoded.
    /// </summary>
    /// <remarks>
    /// <c>/Contents</c> may be one stream or an array of them, and the split between them is
    /// arbitrary — a single operator's tokens can straddle two streams. Concatenating with a
    /// newline between them, as the specification requires, is the only correct way to read them.
    /// </remarks>
    public byte[] GetContent()
    {
        var contents = Dictionary.Get(PdfName.Contents);

        switch (contents)
        {
            case PdfStream single:
                return single.Decoded;

            case PdfArray array:
            {
                using var buffer = new MemoryStream();
                foreach (var item in array)
                {
                    if (_document.Follow(item) is PdfStream stream)
                    {
                        var decoded = stream.Decoded;
                        buffer.Write(decoded, 0, decoded.Length);
                        buffer.WriteByte((byte)'\n');
                    }
                }

                return buffer.ToArray();
            }

            default:
                return [];
        }
    }

    /// <summary>Replaces the page's content with a single compressed stream.</summary>
    public void SetContent(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var stream = new PdfStream();
        stream.SetDecoded(content);
        Dictionary[PdfName.Contents] = _document.AddObject(stream);
    }

    /// <summary>Appends a content stream to the page, after everything already drawn.</summary>
    public void AppendContent(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var stream = new PdfStream();
        stream.SetDecoded(content);
        var reference = _document.AddObject(stream);

        var existing = Dictionary[PdfName.Contents];

        Dictionary[PdfName.Contents] = existing switch
        {
            null => reference,
            PdfArray array => Append(array, reference),
            _ => new PdfArray([existing, reference]),
        };

        static PdfArray Append(PdfArray array, PdfReference reference)
        {
            array.Add(reference);
            return array;
        }
    }

    /// <summary>
    /// Prepends a content stream, so it draws underneath everything already on the page.
    /// </summary>
    /// <remarks>
    /// Used for watermarks that must sit behind the text, and for the <c>q</c> half of a
    /// save/restore pair wrapped around existing content — a page whose content stream leaves the
    /// graphics state unbalanced would otherwise corrupt whatever is appended after it.
    /// </remarks>
    public void PrependContent(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var stream = new PdfStream();
        stream.SetDecoded(content);
        var reference = _document.AddObject(stream);

        var existing = Dictionary[PdfName.Contents];

        Dictionary[PdfName.Contents] = existing switch
        {
            null => reference,
            PdfArray array => Prepend(array, reference),
            _ => new PdfArray([reference, existing]),
        };

        static PdfArray Prepend(PdfArray array, PdfReference reference)
        {
            array.Insert(0, reference);
            return array;
        }
    }

    /// <summary>Extracts the page's text.</summary>
    public string ExtractText() => Text.TextExtractor.Extract(this);

    /// <summary>Extracts the page's text with positions, for layout-aware processing.</summary>
    public IReadOnlyList<Text.TextFragment> ExtractTextFragments() =>
        Text.TextExtractor.ExtractFragments(this);

    /// <summary>
    /// The images the page draws, as their stored bytes plus enough metadata to save them.
    /// </summary>
    public IEnumerable<Text.PdfImage> ExtractImages() => Text.ImageExtractor.Extract(this);

    /// <summary>Opens a drawing surface that appends to this page.</summary>
    public Content.PdfCanvas OpenCanvas() => new(this);

    public override string ToString() => $"Page {Width:0.#}x{Height:0.#}pt rot={Rotation}";
}

/// <summary>
/// A document's pages, held as a flat list and written back as a flat page tree.
/// </summary>
/// <remarks>
/// PDF allows the page tree to be an arbitrarily deep balanced tree, and readers of very large
/// documents benefit from that. This library flattens it on load and writes one <c>/Pages</c> node
/// with every page as a direct kid — which is simpler, is what most producers emit for documents
/// under a few thousand pages, and makes insertion and reordering O(1) instead of a rebalance.
/// </remarks>
public sealed class PdfPageCollection : IReadOnlyList<PdfPage>
{
    private readonly PdfDocument _document;
    private readonly List<PdfPage> _pages = [];

    internal PdfPageCollection(PdfDocument document) => _document = document;

    /// <inheritdoc />
    public int Count => _pages.Count;

    /// <inheritdoc />
    public PdfPage this[int index] => _pages[index];

    /// <summary>The pages as a list.</summary>
    public IReadOnlyList<PdfPage> All => _pages;

    internal void Reload()
    {
        _pages.Clear();

        var root = _document.Catalog.Get<PdfDictionary>(PdfName.Pages);
        if (root is null)
        {
            return;
        }

        var visited = new HashSet<PdfDictionary>();
        Walk(root, visited, 0);
    }

    private void Walk(PdfDictionary node, HashSet<PdfDictionary> visited, int depth)
    {
        // A malformed tree can point a node at one of its own ancestors, and depth alone does not
        // catch a two-node cycle.
        if (depth > 64 || !visited.Add(node))
        {
            return;
        }

        var type = node.DictionaryType;

        // Some producers omit /Type on leaves. A node with /Kids is a branch whatever it says; a
        // node without is a leaf.
        var kids = node.Get(PdfName.Kids) as PdfArray;

        if (kids is null || type == "Page")
        {
            _pages.Add(new PdfPage(_document, node));
            return;
        }

        foreach (var kid in kids)
        {
            if (_document.Follow(kid) is PdfDictionary child)
            {
                Walk(child, visited, depth + 1);
            }
        }
    }

    /// <summary>Adds a new empty page of the given size.</summary>
    public PdfPage Add(PdfRectangle size)
    {
        var dictionary = new PdfDictionary();
        dictionary[PdfName.Type] = PdfName.Page;
        dictionary[PdfName.MediaBox] = size.ToArray();
        dictionary[PdfName.Resources] = new PdfDictionary();

        _document.AddObject(dictionary);

        var page = new PdfPage(_document, dictionary);
        _pages.Add(page);
        return page;
    }

    /// <summary>Adds a new empty A4 page.</summary>
    public PdfPage Add() => Add(PageSize.A4);

    /// <summary>Inserts a new empty page at an index.</summary>
    public PdfPage Insert(int index, PdfRectangle size)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, _pages.Count);

        var page = Add(size);
        _pages.RemoveAt(_pages.Count - 1);
        _pages.Insert(index, page);
        return page;
    }

    internal PdfPage AppendImported(PdfDictionary dictionary)
    {
        if (!dictionary.IsIndirect)
        {
            _document.AddObject(dictionary);
        }

        var page = new PdfPage(_document, dictionary);
        _pages.Add(page);
        return page;
    }

    /// <summary>Removes a page.</summary>
    public void RemoveAt(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _pages.Count);

        // The page dictionary is left in the object store deliberately. Removing it would orphan
        // anything else that still points at it — a named destination, an outline entry — and a few
        // unreferenced objects cost far less than a broken link.
        _pages.RemoveAt(index);
    }

    /// <summary>Removes a range of pages.</summary>
    public void RemoveRange(int index, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index + count, _pages.Count);

        _pages.RemoveRange(index, count);
    }

    /// <summary>Moves a page to a different position.</summary>
    public void Move(int fromIndex, int toIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fromIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(fromIndex, _pages.Count);
        ArgumentOutOfRangeException.ThrowIfNegative(toIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(toIndex, _pages.Count);

        var page = _pages[fromIndex];
        _pages.RemoveAt(fromIndex);
        _pages.Insert(toIndex, page);
    }

    /// <summary>Reverses the page order.</summary>
    public void Reverse() => _pages.Reverse();

    /// <summary>Rotates every page by a quarter-turn multiple.</summary>
    public void RotateAll(int degrees)
    {
        foreach (var page in _pages)
        {
            page.Rotate(degrees);
        }
    }

    internal void Flush()
    {
        var root = _document.Catalog.Get<PdfDictionary>(PdfName.Pages);

        if (root is null)
        {
            root = new PdfDictionary();
            root[PdfName.Type] = PdfName.Pages;
            _document.Catalog[PdfName.Pages] = _document.AddObject(root);
        }

        var rootReference = _document.ReferenceTo(root);

        var kids = new PdfArray();
        foreach (var page in _pages)
        {
            page.Dictionary[PdfName.Type] = PdfName.Page;

            // Every page must point back at the tree it belongs to. An imported page still points
            // at its old document's tree, and leaving that in place makes inherited-attribute
            // lookup follow a reference into an object that no longer exists.
            page.Dictionary[PdfName.Parent] = rootReference;

            kids.Add(_document.ReferenceTo(page.Dictionary));
        }

        root[PdfName.Kids] = kids;
        root.Set(PdfName.Count, _pages.Count);
    }

    /// <inheritdoc />
    public IEnumerator<PdfPage> GetEnumerator() => _pages.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>The document information dictionary: title, author, producer, dates.</summary>
public sealed class PdfDocumentInfo
{
    private readonly PdfDocument _document;

    private PdfDocumentInfo(PdfDocument document, PdfDictionary dictionary)
    {
        _document = document;
        Dictionary = dictionary;
    }

    /// <summary>The underlying dictionary.</summary>
    public PdfDictionary Dictionary { get; }

    internal static PdfDocumentInfo CreateFor(PdfDocument document)
    {
        var dictionary = new PdfDictionary();
        document.AddObject(dictionary);
        return new PdfDocumentInfo(document, dictionary);
    }

    internal static PdfDocumentInfo OpenOrCreate(PdfDocument document, PdfDictionary? existing)
    {
        if (existing is not null)
        {
            return new PdfDocumentInfo(document, existing);
        }

        return CreateFor(document);
    }

    private string? Get(string key) => Dictionary.GetText(PdfName.Get(key));

    private void Set(string key, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            Dictionary.Remove(PdfName.Get(key));
        }
        else
        {
            Dictionary[PdfName.Get(key)] = new PdfString(value);
        }
    }

    private DateTimeOffset? GetDate(string key) =>
        (Dictionary.Get(PdfName.Get(key)) as PdfString)?.AsDate();

    private void SetDate(string key, DateTimeOffset? value)
    {
        if (value is null)
        {
            Dictionary.Remove(PdfName.Get(key));
        }
        else
        {
            Dictionary[PdfName.Get(key)] = PdfString.FromDate(value.Value);
        }
    }

    /// <summary>The document title.</summary>
    public string? Title
    {
        get => Get("Title");
        set => Set("Title", value);
    }

    /// <summary>The author.</summary>
    public string? Author
    {
        get => Get("Author");
        set => Set("Author", value);
    }

    /// <summary>The subject.</summary>
    public string? Subject
    {
        get => Get("Subject");
        set => Set("Subject", value);
    }

    /// <summary>Keywords.</summary>
    public string? Keywords
    {
        get => Get("Keywords");
        set => Set("Keywords", value);
    }

    /// <summary>The application that authored the original document.</summary>
    public string? Creator
    {
        get => Get("Creator");
        set => Set("Creator", value);
    }

    /// <summary>The application that produced the PDF.</summary>
    public string? Producer
    {
        get => Get("Producer");
        set => Set("Producer", value);
    }

    /// <summary>When the document was created.</summary>
    public DateTimeOffset? CreationDate
    {
        get => GetDate("CreationDate");
        set => SetDate("CreationDate", value);
    }

    /// <summary>When the document was last modified.</summary>
    public DateTimeOffset? ModificationDate
    {
        get => GetDate("ModDate");
        set => SetDate("ModDate", value);
    }

    /// <summary>Copies every entry from another document's information dictionary.</summary>
    public void CopyFrom(PdfDocumentInfo other)
    {
        ArgumentNullException.ThrowIfNull(other);

        foreach (var (key, value) in other.Dictionary)
        {
            Dictionary[key] = value;
        }
    }

    internal void Flush()
    {
        Producer = "OfficeNet PdfNet by Gravicode Studios";
        CreationDate ??= DateTimeOffset.Now;
        ModificationDate = DateTimeOffset.Now;
    }
}

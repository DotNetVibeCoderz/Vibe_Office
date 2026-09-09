// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Text;
using OfficeNet.Core;
using PdfNet.Io;
using PdfNet.Objects;
using PdfNet.Security;

namespace PdfNet.Document;

/// <summary>
/// A PDF document: pages, metadata, form fields, annotations, and everything needed to read one,
/// change it, and write it back.
/// </summary>
/// <remarks>
/// <para>
/// This is the type PyPDF2's <c>PdfReader</c> and <c>PdfWriter</c> collapse into. Keeping them
/// separate forces every edit into a copy loop; a single mutable document means
/// <c>doc.Pages.RemoveAt(3); doc.Save(path)</c> is the whole operation.
/// </para>
/// <para>
/// Objects are held by number in one store. Opening a file loads every indirect object eagerly —
/// which sounds expensive and is not, because the streams inside them stay compressed until
/// something asks for their content. A 200-page report loads as a few thousand small dictionaries.
/// </para>
/// </remarks>
public sealed class PdfDocument : IPdfObjectResolver, IDisposable
{
    private readonly Dictionary<int, PdfObject> _objects = [];
    private readonly PdfReader? _reader;
    private int _nextObjectNumber = 1;
    private byte[]? _fileId;
    private PdfDictionary? _encryptDictionary;
    private IPdfEncryption? _encryption;
    private bool _disposed;

    private PdfDocument(PdfReader? reader)
    {
        _reader = reader;
        Pages = new PdfPageCollection(this);
    }

    /// <summary>The PDF version written in the header.</summary>
    public string Version { get; set; } = "1.7";

    /// <summary>The document catalogue.</summary>
    public PdfDictionary Catalog { get; private set; } = new();

    /// <summary>The document's pages.</summary>
    public PdfPageCollection Pages { get; }

    /// <summary>Title, author and the other document-information entries.</summary>
    public PdfDocumentInfo Info { get; private set; } = null!;

    /// <summary>The path the document was opened from, when it was opened from a file.</summary>
    public string? Path { get; private set; }

    /// <summary>True when the file that was opened was encrypted.</summary>
    public bool WasEncrypted => _reader?.WasEncrypted ?? false;

    /// <summary>True when the cross-reference table had to be rebuilt to open the file.</summary>
    public bool WasRepaired => _reader?.WasRepaired ?? false;

    /// <summary>The permissions declared by the file that was opened, or <see cref="PdfPermissions.All"/>.</summary>
    public PdfPermissions Permissions => _reader?.Security?.Permissions ?? PdfPermissions.All;

    // ---- Construction --------------------------------------------------------------------------

    /// <summary>Creates an empty document with no pages.</summary>
    public static PdfDocument Create()
    {
        var document = new PdfDocument(null);

        var pageTree = new PdfDictionary();
        pageTree[PdfName.Type] = PdfName.Pages;
        pageTree[PdfName.Kids] = new PdfArray();
        pageTree.Set(PdfName.Count, 0);
        var pageTreeReference = document.AddObject(pageTree);

        var catalog = new PdfDictionary();
        catalog[PdfName.Type] = PdfName.Catalog;
        catalog[PdfName.Pages] = pageTreeReference;
        document.AddObject(catalog);

        document.Catalog = catalog;
        document.Info = PdfDocumentInfo.CreateFor(document);
        return document;
    }

    /// <summary>Opens a PDF from a file.</summary>
    /// <param name="path">The file to open.</param>
    /// <param name="password">The user or owner password, when the file is encrypted.</param>
    public static PdfDocument Open(string path, string password = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var document = Open(File.ReadAllBytes(path), password);
        document.Path = System.IO.Path.GetFullPath(path);
        return document;
    }

    /// <summary>Opens a PDF from a stream.</summary>
    public static PdfDocument Open(Stream stream, string password = "")
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return Open(buffer.ToArray(), password);
    }

    /// <summary>Opens a PDF from bytes.</summary>
    public static PdfDocument Open(byte[] bytes, string password = "")
    {
        var reader = PdfReader.Open(bytes, password);
        var document = new PdfDocument(reader)
        {
            Version = reader.Version,
        };

        foreach (var (number, value) in reader.AllObjects())
        {
            document._objects[number] = value;
            document._nextObjectNumber = Math.Max(document._nextObjectNumber, number + 1);
        }

        document.Catalog = reader.Catalog;

        // The catalogue may be a direct dictionary in a repaired file, in which case it has no
        // object number yet and would be dropped on save.
        if (!document._objects.ContainsValue(document.Catalog))
        {
            document.AddObject(document.Catalog);
        }

        var idArray = reader.Trailer.GetArray(PdfName.Id);
        document._fileId = idArray.Count > 0 && idArray[0] is PdfString id ? id.Value : null;

        document.Info = PdfDocumentInfo.OpenOrCreate(document, reader.Info);
        document.Pages.Reload();
        return document;
    }

    // ---- Object store --------------------------------------------------------------------------

    /// <summary>Adds an object to the store and returns a reference to it.</summary>
    public PdfReference AddObject(PdfObject value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var number = _nextObjectNumber++;
        value.ObjectNumber = number;
        value.GenerationNumber = 0;
        _objects[number] = value;
        return new PdfReference(number, 0, this);
    }

    /// <summary>Returns a reference to an object already in the store, adding it when it is not.</summary>
    public PdfReference ReferenceTo(PdfObject value)
    {
        if (value.IsIndirect && _objects.TryGetValue(value.ObjectNumber, out var existing) &&
            ReferenceEquals(existing, value))
        {
            return new PdfReference(value.ObjectNumber, value.GenerationNumber, this);
        }

        return AddObject(value);
    }

    /// <summary>Removes an object from the store.</summary>
    public bool RemoveObject(int number) => _objects.Remove(number);

    /// <inheritdoc />
    public PdfObject Resolve(int objectNumber, int generationNumber) =>
        _objects.TryGetValue(objectNumber, out var value)
            ? value
            : _reader?.Resolve(objectNumber, generationNumber) ?? PdfNull.Instance;

    /// <summary>Follows a reference chain to the object it names.</summary>
    public PdfObject? Follow(PdfObject? value)
    {
        for (var depth = 0; value is PdfReference reference && depth < 32; depth++)
        {
            value = reference.WithResolver(this).Resolve();
        }

        return value is PdfNull ? null : value;
    }

    /// <summary>The number of indirect objects the document will write.</summary>
    public int ObjectCount => _objects.Count;

    // ---- Importing from another document -------------------------------------------------------

    /// <summary>
    /// Copies an object graph from another document into this one, renumbering as it goes.
    /// </summary>
    /// <remarks>
    /// The map is what makes shared structure survive. Two pages of the source that use the same
    /// font resource must still share it after the import, or a 50-page merge duplicates every
    /// embedded font 50 times. Passing the same map across a whole document's import is therefore
    /// not an optimisation, it is a correctness requirement for the file size.
    /// </remarks>
    public PdfObject Import(PdfObject value, PdfDocument source, Dictionary<int, int> map)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(map);

        return ImportCore(value, source, map, 0);
    }

    private PdfObject ImportCore(PdfObject value, PdfDocument source, Dictionary<int, int> map, int depth)
    {
        if (depth > 128)
        {
            return PdfNull.Instance;
        }

        switch (value)
        {
            case PdfReference reference:
            {
                if (map.TryGetValue(reference.Number, out var existing))
                {
                    return new PdfReference(existing, 0, this);
                }

                // The placeholder is registered before the target is imported, so a cycle
                // (a page's /Parent pointing back at the tree that contains it) terminates.
                var number = _nextObjectNumber++;
                map[reference.Number] = number;

                var target = source.Resolve(reference.Number, reference.Generation);
                var imported = ImportCore(target, source, map, depth + 1);
                imported.ObjectNumber = number;
                imported.GenerationNumber = 0;
                _objects[number] = imported;

                return new PdfReference(number, 0, this);
            }

            case PdfStream stream:
            {
                var copy = new PdfStream(new PdfDictionary(), stream.Data);
                foreach (var (key, child) in stream)
                {
                    copy[key] = ImportCore(child, source, map, depth + 1);
                }

                return copy;
            }

            case PdfDictionary dictionary:
            {
                var copy = new PdfDictionary();
                foreach (var (key, child) in dictionary)
                {
                    copy[key] = ImportCore(child, source, map, depth + 1);
                }

                return copy;
            }

            case PdfArray array:
            {
                var copy = new PdfArray();
                foreach (var item in array)
                {
                    copy.Add(ImportCore(item, source, map, depth + 1));
                }

                return copy;
            }

            default:
                // Scalars are immutable in this model and can be shared.
                return value;
        }
    }

    // ---- Merging and splitting -----------------------------------------------------------------

    /// <summary>Appends every page of another document to this one.</summary>
    public void Merge(PdfDocument other)
    {
        ArgumentNullException.ThrowIfNull(other);
        MergeRange(other, 0, other.Pages.Count);
    }

    /// <summary>Appends a range of another document's pages.</summary>
    /// <param name="other">The document to take pages from.</param>
    /// <param name="startIndex">The first page to take, zero-based.</param>
    /// <param name="count">How many pages to take.</param>
    public void MergeRange(PdfDocument other, int startIndex, int count)
    {
        ArgumentNullException.ThrowIfNull(other);
        ArgumentOutOfRangeException.ThrowIfNegative(startIndex);

        if (ReferenceEquals(other, this))
        {
            throw new ArgumentException("A document cannot be merged into itself.", nameof(other));
        }

        var end = Math.Min(startIndex + count, other.Pages.Count);
        var map = new Dictionary<int, int>();

        for (var i = startIndex; i < end; i++)
        {
            var sourcePage = other.Pages[i];
            var imported = (PdfDictionary)ImportCore(sourcePage.Dictionary, other, map, 0);

            // Inherited attributes live on the source's page tree, which is not being imported.
            // Resolving them onto the page itself is what stops a merged page losing its size.
            MaterialiseInheritedAttributes(sourcePage, imported, other, map);

            Pages.AppendImported(imported);
        }
    }

    private void MaterialiseInheritedAttributes(PdfPage sourcePage, PdfDictionary imported,
        PdfDocument source, Dictionary<int, int> map)
    {
        foreach (var key in (PdfName[])[PdfName.MediaBox, PdfName.CropBox, PdfName.Resources, PdfName.Rotate])
        {
            if (imported.ContainsKey(key))
            {
                continue;
            }

            var inherited = sourcePage.GetInherited(key);
            if (inherited is not null)
            {
                imported[key] = ImportCore(inherited, source, map, 0);
            }
        }
    }

    /// <summary>
    /// Splits the document into one new document per page.
    /// </summary>
    /// <remarks>The caller owns the returned documents and must dispose them.</remarks>
    public List<PdfDocument> Split()
    {
        var result = new List<PdfDocument>(Pages.Count);

        for (var i = 0; i < Pages.Count; i++)
        {
            var part = Create();
            part.Version = Version;
            part.MergeRange(this, i, 1);
            part.Info.CopyFrom(Info);
            result.Add(part);
        }

        return result;
    }

    /// <summary>Splits the document into chunks of at most <paramref name="pagesPerPart"/> pages.</summary>
    public List<PdfDocument> Split(int pagesPerPart)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pagesPerPart, 1);

        var result = new List<PdfDocument>();

        for (var start = 0; start < Pages.Count; start += pagesPerPart)
        {
            var part = Create();
            part.Version = Version;
            part.MergeRange(this, start, pagesPerPart);
            part.Info.CopyFrom(Info);
            result.Add(part);
        }

        return result;
    }

    /// <summary>Concatenates documents into a new one. The inputs are left untouched.</summary>
    public static PdfDocument Concat(params PdfDocument[] documents)
    {
        ArgumentNullException.ThrowIfNull(documents);

        var result = Create();
        foreach (var document in documents)
        {
            result.Merge(document);
        }

        if (documents.Length > 0)
        {
            result.Info.CopyFrom(documents[0].Info);
        }

        return result;
    }

    /// <summary>Concatenates PDF files into a new document.</summary>
    public static PdfDocument ConcatFiles(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var result = Create();
        foreach (var path in paths)
        {
            using var source = Open(path);
            result.Merge(source);
        }

        return result;
    }

    // ---- Encryption ----------------------------------------------------------------------------

    /// <summary>
    /// Encrypts the document on its next save.
    /// </summary>
    /// <param name="userPassword">Required to open the document. Empty means it opens unprompted.</param>
    /// <param name="ownerPassword">Lifts the restrictions; defaults to the user password.</param>
    /// <param name="permissions">What a user-password holder may do.</param>
    /// <param name="cipher">The cipher. AES-256 unless a pre-2008 reader must open the file.</param>
    public void Encrypt(string userPassword, string? ownerPassword = null,
        PdfPermissions permissions = PdfPermissions.All, PdfCipher cipher = PdfCipher.Aes256)
    {
        ArgumentNullException.ThrowIfNull(userPassword);

        _fileId ??= PdfWriter.CreateFileId().Count > 0
            ? ((PdfString)PdfWriter.CreateFileId()[0]).Value
            : new byte[16];

        var (handler, dictionary) = StandardSecurityHandler.Create(
            userPassword, ownerPassword, permissions, cipher, _fileId);

        _encryption = handler;
        _encryptDictionary = dictionary;

        // AES-256 is a PDF 2.0 feature. Declaring 1.7 makes Acrobat refuse the file.
        if (cipher == PdfCipher.Aes256)
        {
            Version = "2.0";
        }
        else if (cipher == PdfCipher.Aes128 && string.CompareOrdinal(Version, "1.6") < 0)
        {
            Version = "1.6";
        }
    }

    /// <summary>Removes encryption, so the next save writes a plain file.</summary>
    public void Decrypt()
    {
        _encryption = null;
        _encryptDictionary = null;
    }

    // ---- Saving --------------------------------------------------------------------------------

    /// <summary>Saves back to the file the document was opened from.</summary>
    public void Save()
    {
        if (Path is null)
        {
            throw new InvalidOperationException(
                "This document was not opened from a file; call Save(path) or Save(stream).");
        }

        Save(Path);
    }

    /// <summary>Saves to a file.</summary>
    // ---- Embedded fonts ------------------------------------------------------------------------

    private readonly List<Fonts.EmbeddedFont> _embeddedFonts = [];
    private readonly Dictionary<string, Fonts.EmbeddedFont> _embeddedByKey = new(StringComparer.Ordinal);

    /// <summary>The fonts embedded in this document.</summary>
    public IReadOnlyList<Fonts.EmbeddedFont> EmbeddedFonts => _embeddedFonts;

    /// <summary>
    /// Embeds a TrueType font, so text can use characters the standard 14 fonts do not have.
    /// </summary>
    /// <param name="path">The <c>.ttf</c> file.</param>
    /// <remarks>
    /// <para>
    /// Only the glyphs actually drawn are written into the file, and only when the document is
    /// saved — a CJK face has tens of thousands of glyphs and a document uses a few hundred, so the
    /// difference is a 30 KB PDF against a 20 MB one.
    /// </para>
    /// <para>
    /// Embedding the same file twice returns the same font rather than a second copy of it.
    /// </para>
    /// </remarks>
    public Fonts.EmbeddedFont EmbedFont(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ObjectDisposedException.ThrowIf(_disposed, this);

        return EmbedFont(File.ReadAllBytes(path));
    }

    /// <summary>Embeds a TrueType font from bytes.</summary>
    public Fonts.EmbeddedFont EmbedFont(byte[] fontBytes)
    {
        ArgumentNullException.ThrowIfNull(fontBytes);
        ObjectDisposedException.ThrowIf(_disposed, this);

        // By content, so the same file loaded twice — from a cache and from disk, say — is one
        // embedded font and not two copies of the same outlines.
        var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fontBytes));

        if (_embeddedByKey.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var font = new Fonts.EmbeddedFont(this, Fonts.TrueTypeFont.Load(fontBytes),
            "TT" + (_embeddedFonts.Count + 1));

        _embeddedFonts.Add(font);
        _embeddedByKey[key] = font;

        return font;
    }

    /// <summary>Embeds a font already parsed.</summary>
    public Fonts.EmbeddedFont EmbedFont(Fonts.TrueTypeFont font)
    {
        ArgumentNullException.ThrowIfNull(font);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var embedded = new Fonts.EmbeddedFont(this, font, "TT" + (_embeddedFonts.Count + 1));
        _embeddedFonts.Add(embedded);

        return embedded;
    }

    /// <summary>
    /// Writes each embedded font's subset, now that the glyphs it uses are known.
    /// </summary>
    /// <remarks>
    /// Has to run before the objects are written and after the last piece of text is drawn, which
    /// is exactly at the start of a save and nowhere else.
    /// </remarks>
    private void FinishEmbeddedFonts()
    {
        foreach (var font in _embeddedFonts)
        {
            font.Finish();
        }
    }

    public void Save(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var full = System.IO.Path.GetFullPath(path);
        var directory = System.IO.Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(directory))
        {
            System.IO.Directory.CreateDirectory(directory);
        }

        var temp = full + ".pdfnet-tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                Save(stream);
            }

            File.Move(temp, full, overwrite: true);
        }
        catch
        {
            if (File.Exists(temp))
            {
                try
                {
                    File.Delete(temp);
                }
                catch (IOException)
                {
                    // The write already failed; the cleanup failure is not the interesting error.
                }
            }

            throw;
        }

        Path = full;
    }

    /// <summary>Saves to a stream.</summary>
    public void Save(Stream stream)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(stream);

        // Before Pages.Flush, so a page's resource dictionary can reference a font object that
        // does not exist until its subset has been built.
        FinishEmbeddedFonts();

        Pages.Flush();
        Info.Flush();

        var writer = new PdfWriter(stream) { Version = Version };

        var encryptNumber = 0;
        if (_encryptDictionary is not null)
        {
            var reference = ReferenceTo(_encryptDictionary);
            encryptNumber = reference.Number;
        }

        foreach (var (number, value) in _objects.OrderBy(o => o.Key))
        {
            writer.Add(number, value);
        }

        var trailer = new PdfDictionary();
        trailer[PdfName.Root] = ReferenceTo(Catalog);

        if (Info.Dictionary.Count > 0)
        {
            trailer[PdfName.Info] = ReferenceTo(Info.Dictionary);
        }

        trailer[PdfName.Id] = PdfWriter.CreateFileId(_fileId);

        if (_encryptDictionary is not null)
        {
            trailer[PdfName.Encrypt] = new PdfReference(encryptNumber, 0, this);
        }

        writer.Write(trailer, _encryption, encryptNumber);
    }

    /// <summary>Serialises the document to a byte array.</summary>
    public byte[] ToArray()
    {
        using var buffer = new MemoryStream();
        Save(buffer);
        return buffer.ToArray();
    }

    // ---- Text ----------------------------------------------------------------------------------

    /// <summary>Extracts the text of every page, in page order.</summary>
    public string ExtractText(string pageSeparator = "\n\n")
    {
        var builder = new StringBuilder();

        for (var i = 0; i < Pages.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(pageSeparator);
            }

            builder.Append(Pages[i].ExtractText());
        }

        return builder.ToString();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _reader?.Dispose();
        _objects.Clear();
        _disposed = true;
    }

    public override string ToString() =>
        $"PdfDocument({Pages.Count} pages{(Path is null ? "" : ", " + System.IO.Path.GetFileName(Path))})";
}

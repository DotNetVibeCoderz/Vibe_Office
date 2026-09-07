// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Security.Cryptography;
using OfficeNet.Core.Drawing;
using OfficeNet.Core.Metadata;
using OfficeNet.Core.Packaging;

namespace OfficeNet.Core.Documents;

/// <summary>
/// What every OfficeNet document can do regardless of format: carry metadata, extract its text, and
/// save.
/// </summary>
/// <remarks>
/// This is the "unified API" the four libraries share. It is deliberately narrow — only operations
/// that mean the same thing for a letter, a spreadsheet and a deck belong here. Anything that
/// differs (paragraphs, cells, slides) stays on the concrete type, because a lowest-common
/// -denominator abstraction over those would be a worse API than either of the three.
/// </remarks>
public interface IOfficeDocument : IDisposable
{
    /// <summary>Dublin Core metadata: title, author, dates.</summary>
    CoreProperties Properties { get; }

    /// <summary>Application metadata: producer, counts, company.</summary>
    ExtendedProperties AppProperties { get; }

    /// <summary>User-defined properties.</summary>
    CustomProperties Custom { get; }

    /// <summary>The path the document was opened from, when it was opened from a file.</summary>
    string? Path { get; }

    /// <summary>All of the document's text, in reading order.</summary>
    string ExtractText();

    /// <summary>Saves back to the file the document was opened from.</summary>
    void Save();

    /// <summary>Saves to a path.</summary>
    void Save(string path);

    /// <summary>Saves to a stream.</summary>
    void Save(Stream stream);
}

/// <summary>
/// The OPC-backed implementation of <see cref="IOfficeDocument"/> that WordNet, ExcelNet and
/// PowerPointNet all derive from.
/// </summary>
public abstract class OfficeDocument : IOfficeDocument
{
    private CoreProperties? _properties;
    private ExtendedProperties? _appProperties;
    private CustomProperties? _custom;
    private bool _disposed;

    /// <summary>Creates a document over an OPC package.</summary>
    protected OfficeDocument(OpcPackage package) =>
        Package = package ?? throw new ArgumentNullException(nameof(package));

    /// <summary>The underlying package. Use it for parts this library does not model.</summary>
    public OpcPackage Package { get; }

    /// <inheritdoc />
    public CoreProperties Properties => _properties ??= CoreProperties.Open(Package);

    /// <inheritdoc />
    public ExtendedProperties AppProperties => _appProperties ??= ExtendedProperties.Open(Package);

    /// <inheritdoc />
    public CustomProperties Custom => _custom ??= CustomProperties.Open(Package);

    /// <inheritdoc />
    public string? Path => Package.Path;

    /// <inheritdoc />
    public abstract string ExtractText();

    /// <summary>
    /// Flushes any model the derived class holds back into the package's XML parts.
    /// </summary>
    /// <remarks>
    /// Called before every save. A derived type that mutates the XML tree in place has nothing to
    /// do here; one that keeps a parsed object model (ExcelNet's sheets, PowerPointNet's slide
    /// list) writes it back at this point.
    /// </remarks>
    protected virtual void FlushToPackage()
    {
    }

    /// <inheritdoc />
    public void Save()
    {
        PrepareSave();
        Package.Save();
    }

    /// <inheritdoc />
    public void Save(string path)
    {
        PrepareSave();
        Package.Save(path);
    }

    /// <inheritdoc />
    public void Save(Stream stream)
    {
        PrepareSave();
        Package.Save(stream);
    }

    /// <summary>Serialises the document to a byte array.</summary>
    public byte[] ToArray()
    {
        using var buffer = new MemoryStream();
        Save(buffer);
        return buffer.ToArray();
    }

    private void PrepareSave()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        FlushToPackage();
        Properties.StampSave();
        AppProperties.StampProducer();
    }

    // ---- Media ---------------------------------------------------------------------------------

    /// <summary>
    /// Adds an image to the package under <paramref name="mediaFolder"/>, reusing an identical
    /// image that is already there.
    /// </summary>
    /// <remarks>
    /// Deduplication is by SHA-256 of the bytes. A deck that puts the same logo on forty slides
    /// otherwise carries forty copies of it, which is the single largest avoidable cost in a
    /// generated .pptx — and it is invisible until someone emails the file.
    /// </remarks>
    protected OpcPart AddImagePart(byte[] imageBytes, string mediaFolder, out ImageInfo info)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);

        info = ImageInfo.Read(imageBytes);
        var hash = Convert.ToHexString(SHA256.HashData(imageBytes));

        if (_mediaHashes.TryGetValue(hash, out var existingName) &&
            Package.TryGetPart(existingName, out var existing))
        {
            return existing;
        }

        // A first call has to index what is already in the package — a document opened from disk
        // has media this session never added.
        if (!_mediaIndexed)
        {
            IndexExistingMedia(mediaFolder);
            if (_mediaHashes.TryGetValue(hash, out existingName) &&
                Package.TryGetPart(existingName, out existing))
            {
                return existing;
            }
        }

        var partName = Package.NextPartName($"{mediaFolder}/image{{0}}.{info.Extension}");
        var part = Package.AddPart(partName, info.ContentType, imageBytes);
        _mediaHashes[hash] = partName;
        return part;
    }

    private readonly Dictionary<string, OpcPartName> _mediaHashes = [];
    private bool _mediaIndexed;

    private void IndexExistingMedia(string mediaFolder)
    {
        _mediaIndexed = true;

        foreach (var part in Package.Parts)
        {
            if (!part.Name.Value.StartsWith(mediaFolder, StringComparison.OrdinalIgnoreCase) ||
                !part.ContentType.StartsWith("image/", StringComparison.Ordinal))
            {
                continue;
            }

            var hash = Convert.ToHexString(SHA256.HashData(part.GetBytes()));
            _mediaHashes.TryAdd(hash, part.Name);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases the package.</summary>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            Package.Dispose();
        }

        _disposed = true;
    }
}

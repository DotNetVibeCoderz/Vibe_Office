// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.IO.Compression;
using OfficeNet.Core;
using OfficeNet.Core.Documents;
using OfficeNet.Core.Packaging;

namespace OfficeNet;

/// <summary>The document formats OfficeNet recognises.</summary>
public enum OfficeFormat
{
    /// <summary>Not a format OfficeNet reads.</summary>
    Unknown,

    /// <summary>A WordprocessingML document (.docx, .docm, .dotx).</summary>
    Word,

    /// <summary>A SpreadsheetML workbook (.xlsx, .xlsm, .xltx).</summary>
    Excel,

    /// <summary>A PresentationML presentation (.pptx, .ppsx, .potx).</summary>
    PowerPoint,

    /// <summary>A PDF.</summary>
    Pdf,

    /// <summary>A legacy binary Office file (.doc, .xls, .ppt), which OfficeNet cannot read.</summary>
    LegacyBinaryOffice,
}

/// <summary>
/// Opens any format OfficeNet supports without the caller having to know which one it is.
/// </summary>
/// <remarks>
/// <para>
/// The format is decided by looking inside the file, not at its extension. A <c>.docx</c> that is
/// really a workbook — which happens whenever something renames a download — opens correctly here
/// and fails with a clear message in the format-specific API.
/// </para>
/// <para>
/// This is the whole of the meta package. The four libraries are independent and a caller who knows
/// which format they have should use them directly; this exists for the case where they do not,
/// such as a folder of mixed attachments or an upload endpoint.
/// </para>
/// </remarks>
public static class Office
{
    /// <summary>Identifies a file's format by its content.</summary>
    public static OfficeFormat DetectFormat(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // Only the first few bytes are needed to reject the non-zip cases, and reading a whole
        // 200 MB PDF to learn it is a PDF would be absurd.
        using var stream = File.OpenRead(path);
        return DetectFormat(stream);
    }

    /// <summary>Identifies a stream's format by its content. The stream must be seekable.</summary>
    public static OfficeFormat DetectFormat(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanSeek)
        {
            throw new ArgumentException(
                "Format detection reads a header and then rewinds, so the stream must be seekable. " +
                "Copy it to a MemoryStream first.", nameof(stream));
        }

        var start = stream.Position;

        try
        {
            Span<byte> header = stackalloc byte[8];
            var read = stream.ReadAtLeast(header, 8, throwOnEndOfStream: false);

            if (read < 4)
            {
                return OfficeFormat.Unknown;
            }

            if (header[..4].SequenceEqual("%PDF"u8))
            {
                return OfficeFormat.Pdf;
            }

            // The OLE compound-document signature: .doc, .xls and .ppt all begin with it. Naming
            // it explicitly turns "not a zip" into advice the caller can act on.
            if (read >= 8 && header.SequenceEqual(
                    (ReadOnlySpan<byte>)[0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]))
            {
                return OfficeFormat.LegacyBinaryOffice;
            }

            if (header[0] != 'P' || header[1] != 'K')
            {
                return OfficeFormat.Unknown;
            }

            stream.Position = start;
            return DetectOoxmlFormat(stream);
        }
        finally
        {
            stream.Position = start;
        }
    }

    /// <summary>Identifies the format of a package already in memory.</summary>
    public static OfficeFormat DetectFormat(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        using var stream = new MemoryStream(bytes, writable: false);
        return DetectFormat(stream);
    }

    private static OfficeFormat DetectOoxmlFormat(Stream stream)
    {
        try
        {
            // The main part's name is enough and needs no full package load: the three formats put
            // it at a fixed, distinct path.
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

            foreach (var entry in archive.Entries)
            {
                var name = entry.FullName;

                if (name.StartsWith("word/", StringComparison.OrdinalIgnoreCase))
                {
                    return OfficeFormat.Word;
                }

                if (name.StartsWith("xl/", StringComparison.OrdinalIgnoreCase))
                {
                    return OfficeFormat.Excel;
                }

                if (name.StartsWith("ppt/", StringComparison.OrdinalIgnoreCase))
                {
                    return OfficeFormat.PowerPoint;
                }
            }

            return OfficeFormat.Unknown;
        }
        catch (InvalidDataException)
        {
            return OfficeFormat.Unknown;
        }
    }

    /// <summary>
    /// Opens a Word, Excel or PowerPoint file as the matching document type.
    /// </summary>
    /// <exception cref="OfficeNetException">
    /// The file is a PDF, a legacy binary Office file, or not a document at all.
    /// </exception>
    public static IOfficeDocument Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return DetectFormat(path) switch
        {
            OfficeFormat.Word => WordNet.WordDocument.Open(path),
            OfficeFormat.Excel => ExcelNet.Workbook.Open(path),
            OfficeFormat.PowerPoint => PowerPointNet.Presentation.Open(path),
            OfficeFormat.Pdf => throw new OfficeNetException(
                $"'{Path.GetFileName(path)}' is a PDF. A PDF is not an OPC document; open it with " +
                "PdfNet.Document.PdfDocument.Open instead."),
            OfficeFormat.LegacyBinaryOffice => throw new OfficeNetNotSupportedException(
                $"'{Path.GetFileName(path)}' is a legacy binary Office file (.doc, .xls or .ppt). " +
                "OfficeNet reads the XML formats only — convert it to .docx, .xlsx or .pptx first."),
            _ => OpenWithPlugin(path),
        };
    }

    /// <summary>
    /// Last resort for <see cref="Open"/>: a format a plugin registered.
    /// </summary>
    /// <remarks>
    /// Reached only after the built-ins have declined, so a plugin can add formats but never
    /// intercept an existing one.
    /// </remarks>
    private static IOfficeDocument OpenWithPlugin(string path)
    {
        using var stream = File.OpenRead(path);

        // Buffered because a handler sniffs and then reads, and a FileStream that a handler leaves
        // mid-way is awkward to rewind for the next one.
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        buffer.Position = 0;

        if (OfficeFormats.FindByContent(buffer) is { } handler)
        {
            buffer.Position = 0;
            return handler.Open(buffer);
        }

        var registered = OfficeFormats.Registered.Count;

        throw new OfficeNetException(
            $"'{Path.GetFileName(path)}' is not a document OfficeNet recognises." +
            (registered == 0
                ? " Register a handler with OfficeFormats.Register to add a format."
                : $" {registered} plugin handler(s) were asked and none claimed it."));
    }

    /// <summary>Extracts a file's text whatever format it is, PDF included.</summary>
    /// <remarks>
    /// The one operation that means the same thing for all four formats, which is why it is the
    /// only one the facade offers beyond opening.
    /// </remarks>
    public static string ExtractText(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (DetectFormat(path) == OfficeFormat.Pdf)
        {
            using var pdf = PdfNet.Document.PdfDocument.Open(path);
            return pdf.ExtractText();
        }

        using var document = Open(path);
        return document.ExtractText();
    }

    /// <summary>
    /// Converts a Word, Excel or PowerPoint file to PDF.
    /// </summary>
    /// <param name="sourcePath">The document to convert.</param>
    /// <param name="pdfPath">Where to write the PDF; defaults to the source path with .pdf.</param>
    /// <returns>The path written.</returns>
    public static string ConvertToPdf(string sourcePath, string? pdfPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        pdfPath ??= Path.ChangeExtension(sourcePath, ".pdf");

        switch (DetectFormat(sourcePath))
        {
            case OfficeFormat.Word:
            {
                using var document = WordNet.WordDocument.Open(sourcePath);
                document.SaveAsPdf(pdfPath);
                break;
            }

            case OfficeFormat.Excel:
            {
                using var workbook = ExcelNet.Workbook.Open(sourcePath);
                workbook.SaveAsPdf(pdfPath);
                break;
            }

            case OfficeFormat.PowerPoint:
            {
                using var presentation = PowerPointNet.Presentation.Open(sourcePath);
                presentation.SaveAsPdf(pdfPath);
                break;
            }

            case OfficeFormat.Pdf:
                // Copying rather than refusing: a batch converter fed a folder should not stop
                // because one file is already in the target format.
                if (!string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(pdfPath),
                        StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(sourcePath, pdfPath, overwrite: true);
                }

                break;

            case OfficeFormat.LegacyBinaryOffice:
                throw new OfficeNetNotSupportedException(
                    $"'{Path.GetFileName(sourcePath)}' is a legacy binary Office file. Convert it " +
                    "to .docx, .xlsx or .pptx first.");

            default:
                throw new OfficeNetException(
                    $"'{Path.GetFileName(sourcePath)}' is not a document OfficeNet can convert.");
        }

        return pdfPath;
    }

    /// <summary>The extensions the four built-in formats use.</summary>
    public static IReadOnlyList<string> BuiltInExtensions { get; } =
    [
        ".docx", ".docm", ".dotx",
        ".xlsx", ".xlsm", ".xltx",
        ".pptx", ".pptm", ".ppsx", ".potx",
        ".pdf",
    ];

    /// <summary>
    /// The file extensions <see cref="Open"/> handles, including any a plugin registered.
    /// </summary>
    /// <remarks>
    /// Computed rather than cached: a handler can be registered at any point, and a list captured
    /// at startup would silently omit whatever was added afterwards.
    /// </remarks>
    public static IReadOnlyList<string> SupportedExtensions =>
        [.. BuiltInExtensions.Concat(OfficeFormats.Extensions)
            .Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>True when a path's extension is one OfficeNet handles.</summary>
    /// <remarks>
    /// Useful for filtering a directory before opening anything. It is a hint, not a decision —
    /// <see cref="DetectFormat(string)"/> reads the file and is what should settle the question.
    /// </remarks>
    public static bool IsSupportedExtension(string path) =>
        !string.IsNullOrWhiteSpace(path) &&
        SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
}

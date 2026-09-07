// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Collections.Concurrent;
using OfficeNet.Core;
using OfficeNet.Core.Packaging;
using OfficeNet.Rendering;

namespace OfficeNet.Dashboard.Services;

/// <summary>One part inside an OPC package.</summary>
internal sealed record PackagePart(string Name, string ContentType, long Bytes);

/// <summary>One relationship, from a source part to a target.</summary>
internal sealed record PackageLink(string Id, string Source, string Target, string Type)
{
    /// <summary>The last segment of the relationship type URI, which is the readable part.</summary>
    public string ShortType => Type.Split('/').LastOrDefault() ?? Type;
}

/// <summary>Everything the inspector knows about one uploaded document.</summary>
internal sealed record Inspection(
    string Id,
    string FileName,
    OfficeFormat Format,
    long Bytes,
    string? Title,
    string? Author,
    int PageCount,
    string Text,
    IReadOnlyList<string> PagePngs,
    IReadOnlyList<PackagePart> Parts,
    IReadOnlyList<PackageLink> Links,
    string? Error)
{
    public bool Failed => Error is not null;

    /// <summary>The extension without the dot, used as the spine label.</summary>
    public string Extension =>
        Path.GetExtension(FileName).TrimStart('.').ToUpperInvariant();

    public string SizeLabel => Size(Bytes);

    internal static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / (1024.0 * 1024):0.##} MB",
    };
}

/// <summary>
/// Loads a document and produces everything the dashboard shows.
/// </summary>
/// <remarks>
/// <para>
/// Held per circuit rather than per request: a Blazor Server component keeps its state between
/// interactions, and re-rendering a page on every keystroke would be wasteful and slow.
/// </para>
/// <para>
/// Pages are stored as base64 data URIs. That is not how a production service should do it — it
/// triples the memory a page costs and puts it all in the circuit — but it keeps the sample to one
/// project with no static file plumbing, and the cap below keeps it honest.
/// </para>
/// </remarks>
internal sealed class DocumentInspector
{
    /// <summary>How many pages are rendered per document.</summary>
    /// <remarks>
    /// Rendering is CPU-bound and each page is a full bitmap. A 400-page PDF would otherwise
    /// occupy a core for a minute and the circuit's memory for the session.
    /// </remarks>
    private const int MaxRenderedPages = 8;

    private readonly ConcurrentDictionary<string, Inspection> _documents = new();

    public IReadOnlyCollection<Inspection> Documents => [.. _documents.Values.OrderBy(d => d.FileName)];

    public Inspection? Selected { get; private set; }

    public void Select(string id) => Selected = _documents.GetValueOrDefault(id);

    public void Remove(string id)
    {
        _documents.TryRemove(id, out _);

        if (Selected?.Id == id)
        {
            Selected = _documents.Values.FirstOrDefault();
        }
    }

    public async Task<Inspection> AddAsync(string fileName, Stream content, CancellationToken token)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, token);

        var bytes = buffer.ToArray();
        var inspection = Inspect(fileName, bytes);

        _documents[inspection.Id] = inspection;
        Selected = inspection;

        return inspection;
    }

    private static Inspection Inspect(string fileName, byte[] bytes)
    {
        var id = Guid.NewGuid().ToString("N")[..12];

        try
        {
            var format = Office.DetectFormat(bytes);

            if (format == OfficeFormat.Unknown)
            {
                return Empty(id, fileName, bytes, format,
                    "Not a document OfficeNet can read. Supported: .docx, .xlsx, .pptx, .pdf.");
            }

            // Written to a temp file because the renderer and the format detectors take a path for
            // the multi-format entry points; deleted immediately after.
            var temp = Path.Combine(Path.GetTempPath(),
                $"officenet-dash-{id}{Path.GetExtension(fileName)}");

            try
            {
                File.WriteAllBytes(temp, bytes);

                var (title, author, text, pages) = ReadMetadata(temp, format);
                var pngs = RenderPages(temp);
                var (parts, links) = ReadPackage(bytes, format);

                return new Inspection(id, fileName, format, bytes.LongLength,
                    title, author, pages, text, pngs, parts, links, Error: null);
            }
            finally
            {
                try
                {
                    File.Delete(temp);
                }
                catch (IOException)
                {
                    // A file still mapped by the renderer is cleaned up by the OS later; failing
                    // to delete a scratch file must not fail the upload.
                }
            }
        }
        catch (OfficeNetException ex)
        {
            return Empty(id, fileName, bytes, OfficeFormat.Unknown, ex.Message);
        }
    }

    private static Inspection Empty(
        string id, string fileName, byte[] bytes, OfficeFormat format, string error) =>
        new(id, fileName, format, bytes.LongLength, null, null, 0, string.Empty,
            [], [], [], error);

    private static (string? Title, string? Author, string Text, int Pages) ReadMetadata(
        string path, OfficeFormat format)
    {
        if (format == OfficeFormat.Pdf)
        {
            using var pdf = PdfNet.Document.PdfDocument.Open(path);
            return (pdf.Info.Title, pdf.Info.Author, pdf.ExtractText(), pdf.Pages.Count);
        }

        using var document = Office.Open(path);

        var pages = document switch
        {
            PowerPointNet.Presentation deck => deck.SlideCount,
            ExcelNet.Workbook workbook => workbook.Count,
            _ => 0,
        };

        return (document.Properties.Title, document.Properties.Creator, document.ExtractText(), pages);
    }

    private static IReadOnlyList<string> RenderPages(string path)
    {
        try
        {
            var pages = DocumentRenderer.Render(path, new RenderOptions
            {
                Dpi = 110,
                MaxPixels = 1100,
                DrawPageBorder = false,
                Format = RenderFormat.Png,
            });

            return [.. pages
                .Take(MaxRenderedPages)
                .Select(png => "data:image/png;base64," + Convert.ToBase64String(png))];
        }
        catch (OfficeNetException)
        {
            // A document that cannot be rendered still has metadata, text and an anatomy worth
            // showing. Losing the preview is not losing the page.
            return [];
        }
    }

    private static (IReadOnlyList<PackagePart>, IReadOnlyList<PackageLink>) ReadPackage(
        byte[] bytes, OfficeFormat format)
    {
        // A PDF is not an OPC package — it has no parts and no relationships. Showing an empty
        // anatomy panel for one is more honest than inventing a structure it does not have.
        if (format == OfficeFormat.Pdf)
        {
            return ([], []);
        }

        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var package = OpcPackage.Open(stream);

            var parts = package.Parts
                .Select(p => new PackagePart(p.Name.ToString(), p.ContentType, p.GetBytes().LongLength))
                .OrderBy(p => p.Name, StringComparer.Ordinal)
                .ToList();

            var links = new List<PackageLink>();

            foreach (var relationship in package.RootRelationships())
            {
                links.Add(new PackageLink(
                    relationship.Id, "/", relationship.Target, relationship.Type));
            }

            foreach (var part in package.Parts)
            {
                foreach (var relationship in part.Relationships)
                {
                    links.Add(new PackageLink(
                        relationship.Id, part.Name.ToString(), relationship.Target, relationship.Type));
                }
            }

            return (parts, links);
        }
        catch (OfficeNetException)
        {
            return ([], []);
        }
    }

    /// <summary>Converts a stored document to PDF and returns the bytes.</summary>
    public static byte[] ConvertToPdf(Inspection inspection, byte[] source)
    {
        var temp = Path.Combine(Path.GetTempPath(),
            $"officenet-conv-{inspection.Id}{Path.GetExtension(inspection.FileName)}");

        var target = Path.ChangeExtension(temp, ".pdf");

        try
        {
            File.WriteAllBytes(temp, source);
            Office.ConvertToPdf(temp, target);
            return File.ReadAllBytes(target);
        }
        finally
        {
            foreach (var path in new[] { temp, target })
            {
                try
                {
                    File.Delete(path);
                }
                catch (IOException)
                {
                }
            }
        }
    }
}

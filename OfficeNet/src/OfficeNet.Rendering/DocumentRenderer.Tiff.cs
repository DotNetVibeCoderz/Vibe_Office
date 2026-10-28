// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using PdfNet.Document;
using SkiaSharp;

namespace OfficeNet.Rendering;

/// <summary>How a document is written as one multi-page TIFF.</summary>
public sealed class TiffOptions
{
    /// <summary>Output resolution; 200 is fax fine mode, 300 an archive scan.</summary>
    public double Dpi { get; set; } = 200;

    /// <summary>Colour, grey, or black and white.</summary>
    public TiffColor Color { get; set; } = TiffColor.Rgb;

    /// <summary>How each strip is compressed.</summary>
    public TiffCompression Compression { get; set; } = TiffCompression.Deflate;

    /// <summary>Caps the longest side of a page in pixels, as for the other formats; 0 for no cap.</summary>
    public int MaxPixels { get; set; } = 6000;

    internal RenderOptions ForRendering() => new() { Dpi = Dpi, MaxPixels = MaxPixels };
}

public static partial class DocumentRenderer
{
    /// <summary>Renders every page of a PDF into one multi-page TIFF.</summary>
    public static byte[] RenderTiff(PdfDocument document, TiffOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        options ??= new TiffOptions();
        var rendering = options.ForRendering();
        var pages = new List<TiffWriter.Page>(document.Pages.Count);

        foreach (var page in document.Pages)
        {
            using var image = RenderImage(page, rendering, out var scale);
            // Read into memory Skia owns: an unpinned managed array may move while it is written.
            var info = new SKImageInfo(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            using var bitmap = new SKBitmap(info);

            if (!image.ReadPixels(bitmap.PeekPixels()))
            {
                throw new Core.OfficeNetException("Could not read the rendered page's pixels.");
            }

            var pixels = bitmap.Bytes;

            // The resolution actually used: MaxPixels may have scaled it below what was asked.
            pages.Add(new TiffWriter.Page(image.Width, image.Height, pixels, scale * 72));
        }

        return TiffWriter.Write(pages, options.Color, options.Compression);
    }

    /// <summary>Renders a Word document into one multi-page TIFF.</summary>
    public static byte[] RenderTiff(WordNet.WordDocument document, TiffOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        using var pdf = document.ToPdf();
        return RenderTiff(pdf, options);
    }

    /// <summary>Renders a workbook into one multi-page TIFF.</summary>
    public static byte[] RenderTiff(ExcelNet.Workbook workbook, TiffOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        using var pdf = workbook.ToPdf();
        return RenderTiff(pdf, options);
    }

    /// <summary>Renders a presentation into one multi-page TIFF, a slide per page.</summary>
    public static byte[] RenderTiff(PowerPointNet.Presentation presentation, TiffOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        using var pdf = presentation.ToPdf();
        return RenderTiff(pdf, options);
    }

    /// <summary>Renders any document OfficeNet reads into a multi-page TIFF file.</summary>
    public static void RenderTiff(string path, string tiffPath, TiffOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(tiffPath);

        using var pdf = OpenAsPdf(path);
        File.WriteAllBytes(tiffPath, RenderTiff(pdf, options));
    }
}

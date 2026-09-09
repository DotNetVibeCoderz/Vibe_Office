// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using OfficeNet.Core;
using OfficeNet.Core.Drawing;
using PdfNet.Document;
using PdfNet.Text;
using SkiaSharp;

namespace OfficeNet.Rendering;

/// <summary>The image formats the renderer writes.</summary>
public enum RenderFormat
{
    /// <summary>PNG — lossless, right for text and diagrams.</summary>
    Png,

    /// <summary>JPEG — smaller, right for pages that are mostly photographs.</summary>
    Jpeg,

    /// <summary>WebP.</summary>
    Webp,
}

/// <summary>How a page is rasterised.</summary>
public sealed class RenderOptions
{
    /// <summary>
    /// Output resolution. 96 matches a screen, 150 suits a thumbnail worth zooming, 300 is print.
    /// </summary>
    public double Dpi { get; set; } = 150;

    /// <summary>The image format.</summary>
    public RenderFormat Format { get; set; } = RenderFormat.Png;

    /// <summary>JPEG and WebP quality, 1 to 100. Ignored for PNG.</summary>
    public int Quality { get; set; } = 90;

    /// <summary>The colour behind the page; PDF pages are transparent until something paints them.</summary>
    public OfficeColor Background { get; set; } = OfficeColor.White;

    /// <summary>
    /// Caps the longest side in pixels; 0 leaves <see cref="Dpi"/> in charge.
    /// </summary>
    /// <remarks>
    /// A guard rather than a preference. An A0 poster at 300 DPI is 9933 x 14043 pixels and around
    /// 560 MB of bitmap — enough to take a server down. Capping the size scales the DPI down
    /// instead of allocating it.
    /// </remarks>
    public int MaxPixels { get; set; } = 4000;

    /// <summary>Draws a hairline border round the page, which helps a white page read as a page.</summary>
    public bool DrawPageBorder { get; set; }
}

/// <summary>
/// Renders OfficeNet documents to images.
/// </summary>
/// <remarks>
/// <para>
/// This lives in its own package because it is the one part of OfficeNet that needs a native
/// dependency. Everything else — reading, writing, converting to PDF — runs on managed code alone,
/// and that is what makes the core libraries behave identically on every platform. Rasterisation
/// cannot: it needs a font engine and a path rasteriser, and SkiaSharp is the sane way to get both.
/// </para>
/// <para>
/// Word, Excel and PowerPoint are rendered by converting to PDF first. That is not a shortcut —
/// their PDF exporters already resolve styles, lay out flow content and paginate, so rendering
/// through PDF means one layout engine rather than four, and a rendered page always matches the
/// PDF a user would get from the same document.
/// </para>
/// </remarks>
public static class DocumentRenderer
{
    /// <summary>Renders one PDF page to an image.</summary>
    public static byte[] RenderPage(PdfPage page, RenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        options ??= new RenderOptions();

        var scale = ResolveScale(page.Width, page.Height, options);

        var width = Math.Max(1, (int)Math.Round(page.Width * scale));
        var height = Math.Max(1, (int)Math.Round(page.Height * scale));

        using var surface = SKSurface.Create(new SKImageInfo(width, height,
            SKColorType.Rgba8888, SKAlphaType.Premul))
            ?? throw new OfficeNetException(
                $"Could not allocate a {width}x{height} surface. Lower Dpi or MaxPixels.");

        var canvas = surface.Canvas;
        canvas.Clear(ToSkia(options.Background));

        // A rotated page is stored unrotated and displayed turned; the renderer has to apply what
        // a viewer would, or a landscape scan comes out on its side.
        ApplyRotation(canvas, page, scale);

        DrawPageContent(canvas, page, scale);

        if (options.DrawPageBorder)
        {
            canvas.ResetMatrix();
            using var border = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = new SKColor(0xC0, 0xC0, 0xC0),
                StrokeWidth = 1,
                IsAntialias = false,
            };

            canvas.DrawRect(0.5f, 0.5f, width - 1, height - 1, border);
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(ToSkiaFormat(options.Format), options.Quality);

        return data.ToArray();
    }

    private static double ResolveScale(double widthPoints, double heightPoints, RenderOptions options)
    {
        var scale = options.Dpi / 72.0;

        if (options.MaxPixels <= 0)
        {
            return scale;
        }

        var longest = Math.Max(widthPoints, heightPoints) * scale;

        // Scale the DPI down rather than cropping: a smaller correct image beats a large fragment.
        return longest > options.MaxPixels
            ? scale * (options.MaxPixels / longest)
            : scale;
    }

    private static void ApplyRotation(SKCanvas canvas, PdfPage page, double scale)
    {
        var box = page.CropBox;

        switch (page.Rotation)
        {
            case 90:
                canvas.Translate((float)(box.Height * scale), 0);
                canvas.RotateDegrees(90);
                break;
            case 180:
                canvas.Translate((float)(box.Width * scale), (float)(box.Height * scale));
                canvas.RotateDegrees(180);
                break;
            case 270:
                canvas.Translate(0, (float)(box.Width * scale));
                canvas.RotateDegrees(270);
                break;
        }
    }

    /// <summary>
    /// Draws a page's text and images.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a <em>content</em> renderer, not a full PDF interpreter. It interprets the content
    /// stream for paths, colours and image placement, and replays the extractor's fragments for
    /// text — which already resolved fonts, encodings and the text matrix.
    /// </para>
    /// <para>
    /// What it does not draw: gradients, patterns, soft masks, transparency groups, blend modes and
    /// clipping paths. Text uses the file's own embedded font when that font is a TrueType program,
    /// which is what makes a page of a script the machine has no font for render as text rather than
    /// as empty boxes; a CFF or Type 1 program, or a font the file does not embed at all, still
    /// falls back to a substituted system face and its glyph shapes are then close but not exact.
    /// </para>
    /// </remarks>
    private static void DrawPageContent(SKCanvas canvas, PdfPage page, double scale)
    {
        var box = page.CropBox;

        using var content = PageContent.Read(page);

        // Painting order is the stream's own order, and in practice that means backgrounds and
        // rules first, then images, then text. Drawing the paths last would hide the page.
        foreach (var path in content.Paths)
        {
            DrawPath(canvas, path, box, scale);
        }

        var placements = content.Images;
        var index = 0;

        foreach (var image in SafeExtractImages(page))
        {
            // The extractor walks the resource dictionary; the interpreter walks the stream. Match
            // them by resource name, and fall back to stream order when a name is missing — an
            // image drawn twice appears in the placement list twice and in the resources once.
            var placement = FindPlacement(placements, image.Name, ref index);
            DrawImage(canvas, image, placement, box, scale);
        }

        foreach (var fragment in page.ExtractTextFragments())
        {
            DrawText(canvas, fragment, content.ColorNear(fragment.X, fragment.Y), box, scale);
        }
    }

    private static SKMatrix? FindPlacement(
        IReadOnlyList<PlacedImage> placements, string name, ref int index)
    {
        foreach (var placement in placements)
        {
            if (string.Equals(placement.Name, name, StringComparison.Ordinal))
            {
                return placement.Matrix;
            }
        }

        return index < placements.Count ? placements[index++].Matrix : null;
    }

    private static void DrawPath(SKCanvas canvas, PaintedPath painted, PdfRectangle box, double scale)
    {
        // The path was built in PDF user space, so one transform takes the whole thing to device
        // space — which is why the interpreter does not need to know the output resolution.
        var matrix = new SKMatrix(
            (float)scale, 0, (float)(-box.Left * scale),
            0, (float)-scale, (float)(box.Top * scale),
            0, 0, 1);

        using var path = new SKPath();
        painted.Path.Transform(in matrix, path);

        path.FillType = painted.EvenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding;

        if (painted.Fill is { } fill)
        {
            using var paint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Color = fill,
            };

            canvas.DrawPath(path, paint);
        }

        if (painted.Stroke is { } stroke)
        {
            using var paint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                Color = stroke,

                // A hairline in PDF is zero-width, meaning "the thinnest the device can draw".
                // Skia reads zero the same way, so the scaled width is only clamped from below.
                StrokeWidth = (float)Math.Max(painted.LineWidth * scale, 0.35),
            };

            canvas.DrawPath(path, paint);
        }
    }

    private static IReadOnlyList<PdfImage> SafeExtractImages(PdfPage page)
    {
        try
        {
            return [.. page.ExtractImages()];
        }
        catch (OfficeNetException)
        {
            // A codec this library passes through rather than decodes must not stop the text from
            // being drawn.
            return [];
        }
    }

    private static void DrawImage(
        SKCanvas canvas, PdfImage image, SKMatrix? placement, PdfRectangle box, double scale)
    {
        using var bitmap = SKBitmap.Decode(image.Data);

        if (bitmap is null)
        {
            return;
        }

        float left, top, width, height;

        if (placement is { } matrix)
        {
            // The matrix maps the unit square onto user space, so its image is the placed rectangle.
            // Only the axis-aligned case is handled: a rotated or skewed image is drawn upright in
            // its bounding box, which is wrong but still recognisable in a thumbnail.
            var origin = matrix.MapPoint(0, 0);
            var corner = matrix.MapPoint(1, 1);

            var userLeft = Math.Min(origin.X, corner.X);
            var userRight = Math.Max(origin.X, corner.X);
            var userTop = Math.Max(origin.Y, corner.Y);
            var userBottom = Math.Min(origin.Y, corner.Y);

            left = (float)((userLeft - box.Left) * scale);
            top = (float)((box.Top - userTop) * scale);
            width = (float)((userRight - userLeft) * scale);
            height = (float)((userTop - userBottom) * scale);
        }
        else
        {
            // No placement was recovered from the stream. Centring at natural size is a guess, but
            // it keeps the image on the page instead of dropping it.
            width = (float)(bitmap.Width * scale * 72.0 / 96.0);
            height = (float)(bitmap.Height * scale * 72.0 / 96.0);
            left = (float)((box.Width * scale - width) / 2);
            top = (float)((box.Height * scale - height) / 2);
        }

        if (width <= 0 || height <= 0)
        {
            return;
        }

        using var paint = new SKPaint { IsAntialias = true };
        using var skImage = SKImage.FromBitmap(bitmap);

        // Linear filtering with mipmaps: a page thumbnail scales a photograph down hard, and
        // nearest-neighbour there produces visible aliasing on every edge.
        var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);

        canvas.DrawImage(skImage, new SKRect(left, top, left + width, top + height), sampling, paint);
    }

    private static void DrawText(
        SKCanvas canvas, TextFragment fragment, SKColor color, PdfRectangle box, double scale)
    {
        // A fragment with glyphs but no decodable text is still ink on the page — a subset font
        // with no /ToUnicode decodes to nothing at all, and skipping it would erase the line.
        var embedded = EmbeddedTypeface(fragment);
        var glyphs = embedded is null ? null : fragment.Glyphs;

        if (glyphs is null && fragment.Text.Trim().Length == 0)
        {
            return;
        }

        var typeface = embedded ?? ResolveTypeface(fragment.FontName);
        var size = (float)Math.Max(1, fragment.FontSize * scale);

        using var font = new SKFont(typeface, size);
        using var paint = new SKPaint { IsAntialias = true, Color = color };

        // PDF's y axis points up from the bottom-left; Skia's points down from the top-left.
        var x = (float)((fragment.X - box.Left) * scale);
        var y = (float)((box.Top - fragment.Y) * scale);

        if (fragment.Rotation != 0)
        {
            // The baseline is turned, so the canvas turns with it, about the fragment's own origin.
            // The angle is negated because PDF measures anticlockwise from a y axis that points up
            // and Skia clockwise from one that points down.
            canvas.Save();
            canvas.RotateDegrees((float)-fragment.Rotation, x, y);
        }

        if (glyphs is not null)
        {
            DrawGlyphs(canvas, glyphs, fragment.GlyphOffsets ?? [], scale, font, paint, x, y);
        }
        else
        {
            canvas.DrawText(fragment.Text, x, y, SKTextAlign.Left, font, paint);
        }

        if (fragment.Rotation != 0)
        {
            canvas.Restore();
        }
    }

    /// <summary>
    /// Draws a run by glyph id rather than by character.
    /// </summary>
    /// <remarks>
    /// The ids index the embedded program directly, which is the only way to draw a subset: it
    /// carries no <c>cmap</c>, so asking the font what glyph a character has returns nothing.
    /// Positions come from the file's own widths rather than from the font's metrics: those are
    /// what the producer laid the line out with, and re-measuring puts the glyphs somewhere the
    /// file never said.
    /// </remarks>
    private static void DrawGlyphs(SKCanvas canvas, ushort[] glyphs, float[] offsets, double scale,
        SKFont font, SKPaint paint, float x, float y)
    {
        if (glyphs.Length == 0)
        {
            return;
        }

        using var builder = new SKTextBlobBuilder();

        var run = builder.AllocateHorizontalRun(font, glyphs.Length, y);
        var positions = new float[glyphs.Length];

        for (var i = 0; i < glyphs.Length; i++)
        {
            positions[i] = x + (i < offsets.Length ? offsets[i] * (float)scale : 0);
        }

        run.SetGlyphs(glyphs);
        run.SetPositions(positions);

        using var blob = builder.Build();

        if (blob is not null)
        {
            canvas.DrawText(blob, 0, 0, paint);
        }
    }

    private static readonly Dictionary<byte[], SKTypeface?> EmbeddedCache =
        new(ReferenceEqualityComparer.Instance as IEqualityComparer<byte[]>);

    private static readonly Lock EmbeddedLock = new();

    /// <summary>
    /// Loads the font the file itself carries, when it carries one that can be loaded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is what makes a page of Devanagari, Thai or Han render as text rather than as a row of
    /// empty boxes. Substituting a system face by name works only while the substitute happens to
    /// cover the script, which for anything outside Latin, Greek and Cyrillic it usually does not.
    /// </para>
    /// <para>
    /// Only a <c>FontFile2</c> is loaded: it is a bare TrueType file, which is what a font engine
    /// takes. CFF and Type 1 programs need wrapping first, and until that is done they fall back to
    /// substitution rather than failing.
    /// </para>
    /// <para>
    /// A font that will not load is cached as a null so a broken program is not re-parsed once per
    /// fragment on a page that uses it throughout.
    /// </para>
    /// </remarks>
    private static SKTypeface? EmbeddedTypeface(TextFragment fragment)
    {
        if (fragment.Glyphs is not { Length: > 0 } ||
            fragment.Font is not { HasLoadableProgram: true } info ||
            info.EmbeddedProgram is not { } program)
        {
            return null;
        }

        lock (EmbeddedLock)
        {
            if (EmbeddedCache.TryGetValue(program, out var cached))
            {
                return cached;
            }

            SKTypeface? typeface = null;

            try
            {
                using var data = SKData.CreateCopy(program);
                typeface = SKTypeface.FromData(data);
            }
            catch (Exception e) when (e is InvalidOperationException or ArgumentException)
            {
                typeface = null;
            }

            EmbeddedCache[program] = typeface;
            return typeface;
        }
    }

    private static readonly Dictionary<string, SKTypeface> TypefaceCache = new(StringComparer.Ordinal);
    private static readonly Lock TypefaceLock = new();

    /// <summary>
    /// Finds a system font for a PDF font name.
    /// </summary>
    /// <remarks>
    /// A subset font's name carries a six-letter prefix and a plus sign (<c>ABCDEF+Helvetica</c>)
    /// which has to be stripped before any match. When nothing matches, Skia's default is used —
    /// on a container with no fonts installed that produces replacement glyphs, which is an
    /// environment problem rather than something the library can fix.
    /// </remarks>
    private static SKTypeface ResolveTypeface(string? fontName)
    {
        var name = fontName is { Length: > 7 } && fontName[6] == '+'
            ? fontName[7..]
            : fontName ?? string.Empty;

        lock (TypefaceLock)
        {
            if (TypefaceCache.TryGetValue(name, out var cached))
            {
                return cached;
            }

            var bold = name.Contains("Bold", StringComparison.OrdinalIgnoreCase);
            var italic = name.Contains("Italic", StringComparison.OrdinalIgnoreCase) ||
                         name.Contains("Oblique", StringComparison.OrdinalIgnoreCase);

            var family = name switch
            {
                _ when name.Contains("Times", StringComparison.OrdinalIgnoreCase) => "Times New Roman",
                _ when name.Contains("Courier", StringComparison.OrdinalIgnoreCase) => "Courier New",
                _ when name.Contains("Helvetica", StringComparison.OrdinalIgnoreCase) => "Arial",
                _ when name.Length == 0 => "Arial",
                _ => name.Split(',', '-')[0],
            };

            var style = new SKFontStyle(
                bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
                SKFontStyleWidth.Normal,
                italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);

            var typeface = SKTypeface.FromFamilyName(family, style)
                           ?? SKTypeface.FromFamilyName("Arial", style)
                           ?? SKTypeface.CreateDefault();

            TypefaceCache[name] = typeface;
            return typeface;
        }
    }

    private static SKColor ToSkia(OfficeColor color) => new(color.R, color.G, color.B, color.A);

    private static SKEncodedImageFormat ToSkiaFormat(RenderFormat format) => format switch
    {
        RenderFormat.Jpeg => SKEncodedImageFormat.Jpeg,
        RenderFormat.Webp => SKEncodedImageFormat.Webp,
        _ => SKEncodedImageFormat.Png,
    };

    // ---- Whole documents -------------------------------------------------------------------------

    /// <summary>Renders every page of a PDF.</summary>
    public static IReadOnlyList<byte[]> RenderPdf(PdfDocument document, RenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        return [.. document.Pages.Select(page => RenderPage(page, options))];
    }

    /// <summary>Renders a Word document by converting it to PDF first.</summary>
    public static IReadOnlyList<byte[]> RenderWord(WordNet.WordDocument document,
        RenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        using var pdf = document.ToPdf();
        return RenderPdf(pdf, options);
    }

    /// <summary>Renders a workbook by converting it to PDF first.</summary>
    public static IReadOnlyList<byte[]> RenderExcel(ExcelNet.Workbook workbook,
        RenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        using var pdf = workbook.ToPdf();
        return RenderPdf(pdf, options);
    }

    /// <summary>Renders a presentation, one image per slide.</summary>
    public static IReadOnlyList<byte[]> RenderPowerPoint(PowerPointNet.Presentation presentation,
        RenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        using var pdf = presentation.ToPdf();
        return RenderPdf(pdf, options);
    }

    /// <summary>
    /// Renders any document OfficeNet reads, choosing the path from the file's content.
    /// </summary>
    /// <returns>One image per page or slide.</returns>
    public static IReadOnlyList<byte[]> Render(string path, RenderOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[4];
        var read = stream.ReadAtLeast(header, 4, throwOnEndOfStream: false);
        stream.Position = 0;

        if (read >= 4 && header.SequenceEqual("%PDF"u8))
        {
            using var pdf = PdfDocument.Open(stream);
            return RenderPdf(pdf, options);
        }

        // Everything else is an OPC package; the format libraries reject the wrong one clearly.
        var extension = Path.GetExtension(path).ToLowerInvariant();

        switch (extension)
        {
            case ".docx" or ".docm" or ".dotx":
            {
                using var document = WordNet.WordDocument.Open(stream);
                return RenderWord(document, options);
            }

            case ".xlsx" or ".xlsm" or ".xltx":
            {
                using var workbook = ExcelNet.Workbook.Open(stream);
                return RenderExcel(workbook, options);
            }

            case ".pptx" or ".pptm" or ".ppsx" or ".potx":
            {
                using var presentation = PowerPointNet.Presentation.Open(stream);
                return RenderPowerPoint(presentation, options);
            }

            default:
                throw new OfficeNetException(
                    $"'{Path.GetFileName(path)}' is not a document OfficeNet can render.");
        }
    }

    /// <summary>
    /// Renders one slide.
    /// </summary>
    /// <param name="presentation">The deck.</param>
    /// <param name="index">The slide's zero-based index.</param>
    /// <param name="options">How to rasterise it.</param>
    /// <remarks>
    /// Laying out the whole deck to get one slide is what <see cref="RenderPowerPoint"/> would do,
    /// and on a two-hundred-slide deck that is most of a second per thumbnail. The layout still has
    /// to run — a slide's page number and its master both come from the deck — but only one page is
    /// rasterised, which is where the time and nearly all the memory go.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">There is no such slide.</exception>
    public static byte[] RenderSlide(PowerPointNet.Presentation presentation, int index,
        RenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, presentation.Slides.Count);

        using var pdf = presentation.ToPdf();

        return RenderPage(pdf.Pages[index], options);
    }

    /// <summary>
    /// Renders a range of a document's pages.
    /// </summary>
    /// <param name="document">The PDF, which the Word, Excel and PowerPoint exporters all produce.</param>
    /// <param name="range">Which pages, zero-based.</param>
    /// <param name="options">How to rasterise them.</param>
    public static IReadOnlyList<byte[]> RenderPdf(PdfDocument document, Range range,
        RenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        var (offset, length) = range.GetOffsetAndLength(document.Pages.Count);

        return [.. Enumerable.Range(offset, length).Select(i => RenderPage(document.Pages[i], options))];
    }

    // ---- Writing files ---------------------------------------------------------------------------

    /// <summary>
    /// Writes rendered images to a directory, one numbered file per page.
    /// </summary>
    /// <remarks>
    /// Split out so that a caller holding a live document does not have to save it to disk first
    /// just to render it — which is what the path-based overload forced, and which meant a temporary
    /// file in every pipeline that generated a deck and wanted thumbnails of it.
    /// </remarks>
    public static IReadOnlyList<string> WriteImages(IReadOnlyList<byte[]> images,
        string outputDirectory, string namePrefix, RenderFormat format = RenderFormat.Png)
    {
        ArgumentNullException.ThrowIfNull(images);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(namePrefix);

        Directory.CreateDirectory(outputDirectory);

        var extension = format switch
        {
            RenderFormat.Jpeg => "jpg",
            RenderFormat.Webp => "webp",
            _ => "png",
        };

        var written = new List<string>(images.Count);

        for (var i = 0; i < images.Count; i++)
        {
            // Padded so a directory listing sorts the way the document reads.
            var name = images.Count == 1
                ? $"{namePrefix}.{extension}"
                : $"{namePrefix}-{i + 1:D2}.{extension}";

            var target = Path.Combine(outputDirectory, name);
            File.WriteAllBytes(target, images[i]);
            written.Add(target);
        }

        return written;
    }

    /// <summary>Renders a presentation and writes one image file per slide.</summary>
    public static IReadOnlyList<string> RenderToFiles(PowerPointNet.Presentation presentation,
        string outputDirectory, RenderOptions? options = null, string namePrefix = "slide")
    {
        options ??= new RenderOptions();

        return WriteImages(RenderPowerPoint(presentation, options), outputDirectory, namePrefix,
            options.Format);
    }

    /// <summary>Renders a Word document and writes one image file per page.</summary>
    public static IReadOnlyList<string> RenderToFiles(WordNet.WordDocument document,
        string outputDirectory, RenderOptions? options = null, string namePrefix = "page")
    {
        options ??= new RenderOptions();

        return WriteImages(RenderWord(document, options), outputDirectory, namePrefix, options.Format);
    }

    /// <summary>Renders a workbook and writes one image file per page.</summary>
    public static IReadOnlyList<string> RenderToFiles(ExcelNet.Workbook workbook,
        string outputDirectory, RenderOptions? options = null, string namePrefix = "page")
    {
        options ??= new RenderOptions();

        return WriteImages(RenderExcel(workbook, options), outputDirectory, namePrefix, options.Format);
    }

    /// <summary>Renders a PDF and writes one image file per page.</summary>
    public static IReadOnlyList<string> RenderToFiles(PdfDocument document,
        string outputDirectory, RenderOptions? options = null, string namePrefix = "page")
    {
        options ??= new RenderOptions();

        return WriteImages(RenderPdf(document, options), outputDirectory, namePrefix, options.Format);
    }

    /// <summary>Renders a document and writes one numbered image file per page.</summary>
    /// <returns>The paths written.</returns>
    public static IReadOnlyList<string> RenderToFiles(string path, string outputDirectory,
        RenderOptions? options = null, string? namePrefix = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        options ??= new RenderOptions();

        return WriteImages(Render(path, options), outputDirectory,
            namePrefix ?? Path.GetFileNameWithoutExtension(path), options.Format);
    }

    /// <summary>Renders the first page as a thumbnail of a given width.</summary>
    public static byte[] RenderThumbnail(string path, int widthPixels = 400)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(widthPixels);

        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[4];
        var read = stream.ReadAtLeast(header, 4, throwOnEndOfStream: false);
        stream.Position = 0;

        PdfDocument pdf;

        if (read >= 4 && header.SequenceEqual("%PDF"u8))
        {
            pdf = PdfDocument.Open(stream);
        }
        else
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();

            pdf = extension switch
            {
                ".docx" or ".docm" or ".dotx" => OpenWord(stream),
                ".xlsx" or ".xlsm" or ".xltx" => OpenExcel(stream),
                ".pptx" or ".pptm" or ".ppsx" or ".potx" => OpenPowerPoint(stream),
                _ => throw new OfficeNetException(
                    $"'{Path.GetFileName(path)}' is not a document OfficeNet can render."),
            };
        }

        using (pdf)
        {
            if (pdf.Pages.Count == 0)
            {
                throw new OfficeNetException($"'{Path.GetFileName(path)}' has no pages.");
            }

            var page = pdf.Pages[0];

            // Derive the DPI from the width the caller asked for, so the thumbnail is exactly that
            // wide whatever the page size.
            return RenderPage(page, new RenderOptions
            {
                Dpi = widthPixels / page.Width * 72.0,
                MaxPixels = 0,
                DrawPageBorder = true,
            });
        }

        static PdfDocument OpenWord(Stream stream)
        {
            using var document = WordNet.WordDocument.Open(stream);
            return document.ToPdf();
        }

        static PdfDocument OpenExcel(Stream stream)
        {
            using var workbook = ExcelNet.Workbook.Open(stream);
            return workbook.ToPdf();
        }

        static PdfDocument OpenPowerPoint(Stream stream)
        {
            using var presentation = PowerPointNet.Presentation.Open(stream);
            return presentation.ToPdf();
        }
    }
}

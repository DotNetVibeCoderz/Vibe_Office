// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Globalization;
using System.Text;
using OfficeNet.Core.Drawing;
using PdfNet.Document;
using PdfNet.Objects;

namespace PdfNet.Annotations;

/// <summary>The annotation subtypes this library creates and recognises.</summary>
public enum PdfAnnotationType
{
    /// <summary>A sticky note.</summary>
    Text,

    /// <summary>A clickable link.</summary>
    Link,

    /// <summary>Free-standing text drawn on the page.</summary>
    FreeText,

    /// <summary>A text highlight.</summary>
    Highlight,

    /// <summary>Underlined text.</summary>
    Underline,

    /// <summary>Struck-through text.</summary>
    StrikeOut,

    /// <summary>Squiggly-underlined text.</summary>
    Squiggly,

    /// <summary>A rectangle.</summary>
    Square,

    /// <summary>An ellipse.</summary>
    Circle,

    /// <summary>A rubber stamp.</summary>
    Stamp,

    /// <summary>An interactive form widget.</summary>
    Widget,

    /// <summary>A file attachment.</summary>
    FileAttachment,

    /// <summary>Any other subtype.</summary>
    Other,
}

/// <summary>An annotation on a page.</summary>
public sealed class PdfAnnotation
{
    private readonly PdfDocument _document;

    internal PdfAnnotation(PdfDocument document, PdfDictionary dictionary)
    {
        _document = document;
        Dictionary = dictionary;
    }

    /// <summary>The underlying annotation dictionary.</summary>
    public PdfDictionary Dictionary { get; }

    /// <summary>The annotation's subtype.</summary>
    public PdfAnnotationType AnnotationType => Dictionary.GetName(PdfName.Subtype) switch
    {
        "Text" => PdfAnnotationType.Text,
        "Link" => PdfAnnotationType.Link,
        "FreeText" => PdfAnnotationType.FreeText,
        "Highlight" => PdfAnnotationType.Highlight,
        "Underline" => PdfAnnotationType.Underline,
        "StrikeOut" => PdfAnnotationType.StrikeOut,
        "Squiggly" => PdfAnnotationType.Squiggly,
        "Square" => PdfAnnotationType.Square,
        "Circle" => PdfAnnotationType.Circle,
        "Stamp" => PdfAnnotationType.Stamp,
        "Widget" => PdfAnnotationType.Widget,
        "FileAttachment" => PdfAnnotationType.FileAttachment,
        _ => PdfAnnotationType.Other,
    };

    /// <summary>The rectangle the annotation occupies, in page coordinates.</summary>
    public PdfRectangle Rectangle
    {
        get => Dictionary.Get(PdfName.Get("Rect")) is PdfArray array
            ? PdfRectangle.FromArray(array)
            : default;
        set => Dictionary[PdfName.Get("Rect")] = value.ToArray();
    }

    /// <summary>The annotation's text content or note.</summary>
    public string? Contents
    {
        get => Dictionary.GetText(PdfName.Get("Contents"));
        set => Dictionary[PdfName.Get("Contents")] =
            value is null ? null : new PdfString(value);
    }

    /// <summary>The author.</summary>
    public string? Author
    {
        get => Dictionary.GetText(PdfName.Get("T"));
        set => Dictionary[PdfName.Get("T")] = value is null ? null : new PdfString(value);
    }

    /// <summary>When the annotation was last modified.</summary>
    public DateTimeOffset? ModifiedDate
    {
        get => (Dictionary.Get(PdfName.Get("M")) as PdfString)?.AsDate();
        set => Dictionary[PdfName.Get("M")] = value is null ? null : PdfString.FromDate(value.Value);
    }

    /// <summary>The annotation's colour, when it declares one.</summary>
    public OfficeColor? Color
    {
        get
        {
            if (Dictionary.Get(PdfName.Get("C")) is not PdfArray array)
            {
                return null;
            }

            var values = array.AsDoubles();

            // /C is 1, 3 or 4 numbers: grey, RGB or CMYK. An empty array means "no colour".
            return values.Length switch
            {
                1 => OfficeColor.FromRgb((byte)(values[0] * 255), (byte)(values[0] * 255), (byte)(values[0] * 255)),
                3 => OfficeColor.FromRgb((byte)(values[0] * 255), (byte)(values[1] * 255), (byte)(values[2] * 255)),
                4 => OfficeColor.FromRgb(
                    (byte)(255 * (1 - Math.Min(1, values[0] + values[3]))),
                    (byte)(255 * (1 - Math.Min(1, values[1] + values[3]))),
                    (byte)(255 * (1 - Math.Min(1, values[2] + values[3])))),
                _ => null,
            };
        }
        set => Dictionary[PdfName.Get("C")] = value is null
            ? null
            : new PdfArray(value.Value.R / 255.0, value.Value.G / 255.0, value.Value.B / 255.0);
    }

    /// <summary>The URI a link annotation points at.</summary>
    public string? Uri
    {
        get => _document.Follow(Dictionary[PdfName.Get("A")]) is PdfDictionary action
            ? action.GetText(PdfName.Get("URI"))
            : null;
        set
        {
            if (value is null)
            {
                Dictionary.Remove(PdfName.Get("A"));
                return;
            }

            var action = new PdfDictionary();
            action.SetName(PdfName.Type, "Action");
            action.SetName(PdfName.Subtype, "URI");
            action[PdfName.Get("URI")] = new PdfString(value);
            Dictionary[PdfName.Get("A")] = action;
        }
    }

    /// <summary>True when the annotation is hidden from display and print.</summary>
    public bool IsHidden
    {
        get => (Dictionary.GetInt(PdfName.Get("F")) & 2) != 0;
        set
        {
            var flags = Dictionary.GetInt(PdfName.Get("F"));
            Dictionary.Set(PdfName.Get("F"), value ? flags | 2 : flags & ~2);
        }
    }

    public override string ToString() =>
        $"{AnnotationType} {Rectangle}{(Contents is null ? "" : $" \"{Contents}\"")}";
}

/// <summary>Adds and reads the annotations on a page.</summary>
/// <remarks>
/// Markup annotations that highlight text need a <c>/QuadPoints</c> array giving the corners of
/// each region they cover, and Acrobat's corner order is not the order the specification's diagram
/// suggests — it is upper-left, upper-right, lower-left, lower-right. Getting it wrong draws the
/// highlight as a bowtie, which is the classic symptom.
/// </remarks>
public static class AnnotationExtensions
{
    /// <summary>The page's annotations.</summary>
    public static IReadOnlyList<PdfAnnotation> GetAnnotations(this PdfPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        var result = new List<PdfAnnotation>();

        if (page.Dictionary.Get(PdfName.Annots) is not PdfArray array)
        {
            return result;
        }

        foreach (var item in array)
        {
            if (page.Document.Follow(item) is PdfDictionary dictionary)
            {
                result.Add(new PdfAnnotation(page.Document, dictionary));
            }
        }

        return result;
    }

    /// <summary>Removes every annotation from the page.</summary>
    public static void ClearAnnotations(this PdfPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        page.Dictionary.Remove(PdfName.Annots);
    }

    /// <summary>Removes the annotations matching a predicate.</summary>
    public static int RemoveAnnotations(this PdfPage page, Func<PdfAnnotation, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(predicate);

        if (page.Dictionary.Get(PdfName.Annots) is not PdfArray array)
        {
            return 0;
        }

        var kept = new PdfArray();
        var removed = 0;

        foreach (var item in array)
        {
            if (page.Document.Follow(item) is PdfDictionary dictionary &&
                predicate(new PdfAnnotation(page.Document, dictionary)))
            {
                removed++;
                continue;
            }

            kept.Add(item);
        }

        page.Dictionary[PdfName.Annots] = kept;
        return removed;
    }

    private static PdfAnnotation Add(PdfPage page, PdfDictionary dictionary)
    {
        dictionary.SetName(PdfName.Type, "Annot");
        dictionary[PdfName.Get("M")] = PdfString.FromDate(DateTimeOffset.Now);

        // /P is what lets a reader find the page an annotation belongs to when it is reached from
        // the outline or from a search index rather than by walking the page.
        dictionary[PdfName.Get("P")] = page.Document.ReferenceTo(page.Dictionary);

        page.Annotations.Add(page.Document.AddObject(dictionary));
        return new PdfAnnotation(page.Document, dictionary);
    }

    /// <summary>Adds a sticky-note annotation.</summary>
    public static PdfAnnotation AddTextNote(this PdfPage page, double x, double y, string contents,
        string? author = null, OfficeColor? color = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(contents);

        var dictionary = new PdfDictionary();
        dictionary.SetName(PdfName.Subtype, "Text");

        // A note icon is drawn at a fixed size regardless of the rectangle, but the rectangle still
        // has to be about the icon's size or the click target is wrong.
        dictionary[PdfName.Get("Rect")] = new PdfArray(x, y - 20, x + 20, y);
        dictionary[PdfName.Get("Contents")] = new PdfString(contents);
        dictionary.SetName(PdfName.Get("Name"), "Comment");
        dictionary[PdfName.Get("C")] = ColorArray(color ?? OfficeColor.FromRgb(0xFF, 0xD4, 0x00));

        if (author is not null)
        {
            dictionary[PdfName.Get("T")] = new PdfString(author);
        }

        return Add(page, dictionary);
    }

    /// <summary>Adds a link over a rectangle.</summary>
    public static PdfAnnotation AddLink(this PdfPage page, PdfRectangle area, string uri)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentException.ThrowIfNullOrWhiteSpace(uri);

        var action = new PdfDictionary();
        action.SetName(PdfName.Type, "Action");
        action.SetName(PdfName.Subtype, "URI");
        action[PdfName.Get("URI")] = new PdfString(uri);

        var dictionary = new PdfDictionary();
        dictionary.SetName(PdfName.Subtype, "Link");
        dictionary[PdfName.Get("Rect")] = area.ToArray();
        dictionary[PdfName.Get("A")] = action;

        // Without /Border [0 0 0] every reader draws a black box round the link.
        dictionary[PdfName.Get("Border")] = new PdfArray(0, 0, 0);

        return Add(page, dictionary);
    }

    /// <summary>Adds an internal link to another page of the same document.</summary>
    public static PdfAnnotation AddInternalLink(this PdfPage page, PdfRectangle area, PdfPage target,
        double? targetTop = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(target);

        // /Fit would ignore the vertical position; /XYZ with nulls for zoom keeps the reader's
        // current zoom, which is what a cross-reference should do.
        var destination = new PdfArray
        {
            page.Document.ReferenceTo(target.Dictionary),
            PdfName.Get("XYZ"),
            PdfNull.Instance,
            targetTop is null ? PdfNull.Instance : new PdfNumber(targetTop.Value),
            PdfNull.Instance,
        };

        var dictionary = new PdfDictionary();
        dictionary.SetName(PdfName.Subtype, "Link");
        dictionary[PdfName.Get("Rect")] = area.ToArray();
        dictionary[PdfName.Get("Dest")] = destination;
        dictionary[PdfName.Get("Border")] = new PdfArray(0, 0, 0);

        return Add(page, dictionary);
    }

    /// <summary>Highlights one or more rectangles of text.</summary>
    public static PdfAnnotation AddHighlight(this PdfPage page, IReadOnlyList<PdfRectangle> areas,
        OfficeColor? color = null, string? note = null, string? author = null) =>
        AddMarkup(page, "Highlight", areas, color ?? OfficeColor.FromRgb(0xFF, 0xF0, 0x00), note, author);

    /// <summary>Underlines one or more rectangles of text.</summary>
    public static PdfAnnotation AddUnderline(this PdfPage page, IReadOnlyList<PdfRectangle> areas,
        OfficeColor? color = null, string? note = null, string? author = null) =>
        AddMarkup(page, "Underline", areas, color ?? OfficeColor.FromRgb(0, 0x70, 0xC0), note, author);

    /// <summary>Strikes through one or more rectangles of text.</summary>
    public static PdfAnnotation AddStrikeOut(this PdfPage page, IReadOnlyList<PdfRectangle> areas,
        OfficeColor? color = null, string? note = null, string? author = null) =>
        AddMarkup(page, "StrikeOut", areas, color ?? OfficeColor.Red, note, author);

    private static PdfAnnotation AddMarkup(PdfPage page, string subtype, IReadOnlyList<PdfRectangle> areas,
        OfficeColor color, string? note, string? author)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(areas);

        if (areas.Count == 0)
        {
            throw new ArgumentException("A markup annotation needs at least one area.", nameof(areas));
        }

        var quads = new PdfArray();
        var bounds = areas[0].Normalized;

        foreach (var area in areas)
        {
            var r = area.Normalized;

            // Corner order: upper-left, upper-right, lower-left, lower-right. Following the
            // specification's figure literally (clockwise) draws the highlight as a bowtie.
            quads.AddValue(r.Left);
            quads.AddValue(r.Top);
            quads.AddValue(r.Right);
            quads.AddValue(r.Top);
            quads.AddValue(r.Left);
            quads.AddValue(r.Bottom);
            quads.AddValue(r.Right);
            quads.AddValue(r.Bottom);

            bounds = new PdfRectangle(
                Math.Min(bounds.Left, r.Left),
                Math.Min(bounds.Bottom, r.Bottom),
                Math.Max(bounds.Right, r.Right),
                Math.Max(bounds.Top, r.Top));
        }

        var dictionary = new PdfDictionary();
        dictionary.SetName(PdfName.Subtype, subtype);
        dictionary[PdfName.Get("Rect")] = bounds.ToArray();
        dictionary[PdfName.Get("QuadPoints")] = quads;
        dictionary[PdfName.Get("C")] = ColorArray(color);
        dictionary.Set(PdfName.Get("CA"), 1.0);

        if (note is not null)
        {
            dictionary[PdfName.Get("Contents")] = new PdfString(note);
        }

        if (author is not null)
        {
            dictionary[PdfName.Get("T")] = new PdfString(author);
        }

        var annotation = Add(page, dictionary);

        if (subtype == "Highlight")
        {
            GenerateHighlightAppearance(page, dictionary, bounds, areas, color);
        }

        return annotation;
    }

    /// <summary>
    /// Draws a highlight's appearance, using multiply blending so the text underneath stays visible.
    /// </summary>
    /// <remarks>
    /// Without an appearance stream a highlight shows only in readers that synthesise one, and
    /// without the multiply blend it paints an opaque block over the text it is meant to emphasise.
    /// </remarks>
    private static void GenerateHighlightAppearance(PdfPage page, PdfDictionary annotation,
        PdfRectangle bounds, IReadOnlyList<PdfRectangle> areas, OfficeColor color)
    {
        var builder = new StringBuilder();
        builder.Append("/GS0 gs\n");
        builder.Append(CultureInfo.InvariantCulture,
            $"{color.R / 255.0:0.###} {color.G / 255.0:0.###} {color.B / 255.0:0.###} rg\n");

        foreach (var area in areas)
        {
            var r = area.Normalized;
            builder.Append(CultureInfo.InvariantCulture,
                $"{r.Left - bounds.Left:0.###} {r.Bottom - bounds.Bottom:0.###} " +
                $"{r.Width:0.###} {r.Height:0.###} re f\n");
        }

        var blend = new PdfDictionary();
        blend.SetName(PdfName.Type, "ExtGState");
        blend.SetName(PdfName.Get("BM"), "Multiply");
        blend.Set(PdfName.Get("ca"), 1.0);

        var extGStates = new PdfDictionary();
        extGStates[PdfName.Get("GS0")] = blend;

        var resources = new PdfDictionary();
        resources[PdfName.Get("ExtGState")] = extGStates;

        var appearance = new PdfStream();
        appearance[PdfName.Type] = PdfName.XObject;
        appearance.SetName(PdfName.Subtype, "Form");
        appearance[PdfName.Get("BBox")] = new PdfArray(0, 0, bounds.Width, bounds.Height);
        appearance[PdfName.Resources] = resources;
        appearance.SetText(builder.ToString());

        var appearanceDictionary = new PdfDictionary();
        appearanceDictionary[PdfName.Get("N")] = page.Document.AddObject(appearance);
        annotation[PdfName.Get("AP")] = appearanceDictionary;
    }

    /// <summary>Adds a rubber stamp with text drawn in a rounded, coloured frame.</summary>
    public static PdfAnnotation AddStamp(this PdfPage page, PdfRectangle area, string text,
        OfficeColor? color = null, string? author = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var stampColor = color ?? OfficeColor.FromRgb(0xC0, 0x00, 0x00);
        var bounds = area.Normalized;

        // The size is chosen to fill the frame: the text is what the stamp is, so it should be as
        // large as it can be while leaving a visible border.
        var fontSize = Math.Min(bounds.Height * 0.55,
            (bounds.Width - 16) / Math.Max(1, Content.StandardFonts.MeasureString(
                Content.StandardFont.HelveticaBold, text) / 1000.0));

        var textWidth = Content.StandardFonts.MeasurePoints(
            Content.StandardFont.HelveticaBold, text, fontSize);

        var builder = new StringBuilder();
        builder.Append(CultureInfo.InvariantCulture,
            $"{stampColor.R / 255.0:0.###} {stampColor.G / 255.0:0.###} {stampColor.B / 255.0:0.###} RG\n");
        builder.Append(CultureInfo.InvariantCulture,
            $"{stampColor.R / 255.0:0.###} {stampColor.G / 255.0:0.###} {stampColor.B / 255.0:0.###} rg\n");
        builder.Append("2 w\n");
        builder.Append(CultureInfo.InvariantCulture,
            $"1 1 {bounds.Width - 2:0.###} {bounds.Height - 2:0.###} re S\n");
        builder.Append("BT\n");
        builder.Append(CultureInfo.InvariantCulture, $"/Helv {fontSize:0.##} Tf\n");
        builder.Append(CultureInfo.InvariantCulture,
            $"1 0 0 1 {(bounds.Width - textWidth) / 2:0.###} {(bounds.Height - fontSize) / 2 + fontSize * 0.2:0.###} Tm\n");
        builder.Append('(').Append(EscapeText(text)).Append(") Tj\nET\n");

        var fonts = new PdfDictionary();
        fonts[PdfName.Get("Helv")] =
            page.Document.AddObject(Content.StandardFonts.CreateFontDictionary(Content.StandardFont.HelveticaBold));

        var resources = new PdfDictionary();
        resources[PdfName.Font] = fonts;

        var appearance = new PdfStream();
        appearance[PdfName.Type] = PdfName.XObject;
        appearance.SetName(PdfName.Subtype, "Form");
        appearance[PdfName.Get("BBox")] = new PdfArray(0, 0, bounds.Width, bounds.Height);
        appearance[PdfName.Resources] = resources;
        appearance.SetText(builder.ToString());

        var appearanceDictionary = new PdfDictionary();
        appearanceDictionary[PdfName.Get("N")] = page.Document.AddObject(appearance);

        var dictionary = new PdfDictionary();
        dictionary.SetName(PdfName.Subtype, "Stamp");
        dictionary[PdfName.Get("Rect")] = bounds.ToArray();
        dictionary[PdfName.Get("Contents")] = new PdfString(text);
        dictionary[PdfName.Get("AP")] = appearanceDictionary;
        dictionary[PdfName.Get("C")] = ColorArray(stampColor);

        if (author is not null)
        {
            dictionary[PdfName.Get("T")] = new PdfString(author);
        }

        return Add(page, dictionary);
    }

    /// <summary>Adds a rectangle annotation.</summary>
    public static PdfAnnotation AddSquare(this PdfPage page, PdfRectangle area,
        OfficeColor? borderColor = null, OfficeColor? fillColor = null, double borderWidth = 1)
    {
        ArgumentNullException.ThrowIfNull(page);

        var dictionary = new PdfDictionary();
        dictionary.SetName(PdfName.Subtype, "Square");
        dictionary[PdfName.Get("Rect")] = area.Normalized.ToArray();
        dictionary[PdfName.Get("C")] = ColorArray(borderColor ?? OfficeColor.Red);

        if (fillColor is not null)
        {
            dictionary[PdfName.Get("IC")] = ColorArray(fillColor.Value);
        }

        var border = new PdfDictionary();
        border.SetName(PdfName.Type, "Border");
        border.Set(PdfName.W, borderWidth);
        dictionary[PdfName.Get("BS")] = border;

        return Add(page, dictionary);
    }

    private static PdfArray ColorArray(OfficeColor color) =>
        new(color.R / 255.0, color.G / 255.0, color.B / 255.0);

    private static string EscapeText(string text)
    {
        var bytes = Content.StandardFonts.EncodeWinAnsi(text);
        var builder = new StringBuilder(bytes.Length);

        foreach (var b in bytes)
        {
            if (b is (byte)'(' or (byte)')' or (byte)'\\')
            {
                builder.Append('\\');
            }

            builder.Append((char)b);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Stamps a diagonal watermark across a page, behind its existing content.
    /// </summary>
    public static void AddWatermark(this PdfPage page, string text, OfficeColor? color = null,
        double opacity = 0.15, double angleDegrees = 45)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var box = page.MediaBox;
        var watermarkColor = color ?? OfficeColor.Gray;

        // Size the text to span most of the page's diagonal, which is what makes a watermark read
        // as one regardless of page size or orientation.
        var diagonal = Math.Sqrt(box.Width * box.Width + box.Height * box.Height);
        var unitWidth = Content.StandardFonts.MeasureString(Content.StandardFont.HelveticaBold, text) / 1000.0;
        var fontSize = unitWidth > 0 ? diagonal * 0.7 / unitWidth : 48;

        var textWidth = Content.StandardFonts.MeasurePoints(
            Content.StandardFont.HelveticaBold, text, fontSize);

        var radians = angleDegrees * Math.PI / 180;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);

        var centerX = box.Left + box.Width / 2;
        var centerY = box.Bottom + box.Height / 2;

        // Rotate about the page centre, then step back half the text width along the rotated axis.
        var startX = centerX - textWidth / 2 * cos + fontSize * 0.35 * sin;
        var startY = centerY - textWidth / 2 * sin - fontSize * 0.35 * cos;

        var alpha = new PdfDictionary();
        alpha.SetName(PdfName.Type, "ExtGState");
        alpha.Set(PdfName.Get("ca"), Math.Clamp(opacity, 0, 1));
        alpha.Set(PdfName.Get("CA"), Math.Clamp(opacity, 0, 1));

        var extGStates = new PdfDictionary();
        extGStates[PdfName.Get("OfnWm")] = alpha;

        var fonts = new PdfDictionary();
        fonts[PdfName.Get("OfnWmF")] =
            page.Document.AddObject(Content.StandardFonts.CreateFontDictionary(Content.StandardFont.HelveticaBold));

        var resources = page.Resources;

        if (resources.Get(PdfName.Get("ExtGState")) is not PdfDictionary existingStates)
        {
            resources[PdfName.Get("ExtGState")] = extGStates;
        }
        else
        {
            existingStates[PdfName.Get("OfnWm")] = alpha;
        }

        if (resources.Get(PdfName.Font) is not PdfDictionary existingFonts)
        {
            resources[PdfName.Font] = fonts;
        }
        else
        {
            existingFonts[PdfName.Get("OfnWmF")] = fonts[PdfName.Get("OfnWmF")]!;
        }

        var content = string.Create(CultureInfo.InvariantCulture,
            $"q\n/OfnWm gs\n" +
            $"{watermarkColor.R / 255.0:0.###} {watermarkColor.G / 255.0:0.###} {watermarkColor.B / 255.0:0.###} rg\n" +
            $"BT\n/OfnWmF {fontSize:0.##} Tf\n" +
            $"{cos:0.#####} {sin:0.#####} {-sin:0.#####} {cos:0.#####} {startX:0.###} {startY:0.###} Tm\n" +
            $"({EscapeText(text)}) Tj\nET\nQ\n");

        page.PrependContent(Encoding.Latin1.GetBytes(content));
    }
}

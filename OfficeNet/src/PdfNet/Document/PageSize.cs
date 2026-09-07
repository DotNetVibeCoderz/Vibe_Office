// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using OfficeNet.Core;
using PdfNet.Objects;

namespace PdfNet.Document;

/// <summary>A rectangle in PDF user space, where the origin is the bottom-left corner.</summary>
/// <remarks>
/// PDF's y axis points up, unlike almost every screen coordinate system and unlike WordprocessingML.
/// Every conversion in this library flips it exactly once; a page that renders upside down is
/// always a second flip that was applied by accident.
/// </remarks>
public readonly record struct PdfRectangle(double Left, double Bottom, double Right, double Top)
{
    /// <summary>The width.</summary>
    public double Width => Math.Abs(Right - Left);

    /// <summary>The height.</summary>
    public double Height => Math.Abs(Top - Bottom);

    /// <summary>The rectangle with its corners in the canonical (lower-left, upper-right) order.</summary>
    /// <remarks>
    /// A <c>/MediaBox</c> may legally be written with its corners in either order — <c>[0 792 612
    /// 0]</c> means the same page as <c>[0 0 612 792]</c>. Normalising once at the boundary is what
    /// keeps every downstream width calculation from having to think about it.
    /// </remarks>
    public PdfRectangle Normalized => new(
        Math.Min(Left, Right), Math.Min(Bottom, Top),
        Math.Max(Left, Right), Math.Max(Bottom, Top));

    /// <summary>Creates a rectangle from a position and a size.</summary>
    public static PdfRectangle FromSize(double left, double bottom, double width, double height) =>
        new(left, bottom, left + width, bottom + height);

    /// <summary>Reads a PDF array as a rectangle.</summary>
    public static PdfRectangle FromArray(PdfArray array)
    {
        var values = array.AsDoubles();
        return values.Length < 4
            ? default
            : new PdfRectangle(values[0], values[1], values[2], values[3]).Normalized;
    }

    /// <summary>The rectangle as the four-number array PDF stores.</summary>
    public PdfArray ToArray() => new(Left, Bottom, Right, Top);

    /// <summary>The rectangle grown by a margin on every side.</summary>
    public PdfRectangle Inflate(double amount) =>
        new(Left - amount, Bottom - amount, Right + amount, Top + amount);

    public override string ToString() => $"[{Left:0.##} {Bottom:0.##} {Right:0.##} {Top:0.##}]";
}

/// <summary>
/// The standard page sizes, in PDF points (1/72 inch).
/// </summary>
public static class PageSize
{
    /// <summary>A4 — 210 x 297 mm.</summary>
    public static PdfRectangle A4 => Millimeters(210, 297);

    /// <summary>A3 — 297 x 420 mm.</summary>
    public static PdfRectangle A3 => Millimeters(297, 420);

    /// <summary>A5 — 148 x 210 mm.</summary>
    public static PdfRectangle A5 => Millimeters(148, 210);

    /// <summary>A6 — 105 x 148 mm.</summary>
    public static PdfRectangle A6 => Millimeters(105, 148);

    /// <summary>US Letter — 8.5 x 11 in.</summary>
    public static PdfRectangle Letter => Inches(8.5, 11);

    /// <summary>US Legal — 8.5 x 14 in.</summary>
    public static PdfRectangle Legal => Inches(8.5, 14);

    /// <summary>US Tabloid — 11 x 17 in.</summary>
    public static PdfRectangle Tabloid => Inches(11, 17);

    /// <summary>F4 / Folio — 215 x 330 mm, the standard office size in Indonesia.</summary>
    public static PdfRectangle F4 => Millimeters(215, 330);

    /// <summary>A 16:9 presentation page, 13.333 x 7.5 in — the PowerPoint widescreen slide.</summary>
    public static PdfRectangle Widescreen => Inches(13.333, 7.5);

    /// <summary>A 4:3 presentation page, 10 x 7.5 in.</summary>
    public static PdfRectangle Standard43 => Inches(10, 7.5);

    /// <summary>A page of a given size in millimetres.</summary>
    public static PdfRectangle Millimeters(double width, double height) =>
        new(0, 0, width * 72 / 25.4, height * 72 / 25.4);

    /// <summary>A page of a given size in inches.</summary>
    public static PdfRectangle Inches(double width, double height) =>
        new(0, 0, width * 72, height * 72);

    /// <summary>A page of a given size in points.</summary>
    public static PdfRectangle Points(double width, double height) => new(0, 0, width, height);

    /// <summary>A page sized from an OfficeNet <see cref="Length"/> pair.</summary>
    public static PdfRectangle FromLength(Length width, Length height) =>
        new(0, 0, width.Points, height.Points);

    /// <summary>The same size rotated a quarter turn.</summary>
    public static PdfRectangle Landscape(this PdfRectangle portrait) =>
        new(0, 0, portrait.Height, portrait.Width);

    /// <summary>Looks a size up by name; <c>null</c> when the name is not one of the standards.</summary>
    public static PdfRectangle? ByName(string name) => name.Trim().ToUpperInvariant() switch
    {
        "A3" => A3,
        "A4" => A4,
        "A5" => A5,
        "A6" => A6,
        "LETTER" => Letter,
        "LEGAL" => Legal,
        "TABLOID" or "LEDGER" => Tabloid,
        "F4" or "FOLIO" => F4,
        "WIDESCREEN" or "16:9" => Widescreen,
        "4:3" => Standard43,
        _ => null,
    };
}

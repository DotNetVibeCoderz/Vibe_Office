// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using PdfNet.Fonts;

namespace PdfNet.Content;

/// <summary>
/// A font to draw with: one of the 14 standard fonts, or a TrueType font embedded in the document.
/// </summary>
/// <remarks>
/// <para>
/// The exporters lay text out by measuring it, and they do not care which kind of font does the
/// measuring — only the output does. A standard font keeps a file small and free of licensing
/// questions; an embedded one is the only kind PDF/A allows, and the only kind that can draw text
/// outside Latin-1. This lets the same layout code produce either.
/// </para>
/// <para>
/// Both convert implicitly, so a method taking a <see cref="PdfFont"/> accepts
/// <c>StandardFont.Helvetica</c> or an <see cref="EmbeddedFont"/> as they are.
/// </para>
/// </remarks>
public sealed class PdfFont
{
    private readonly StandardFont _standard;

    private PdfFont(StandardFont standard, EmbeddedFont? embedded)
    {
        _standard = standard;
        Embedded = embedded;
    }

    /// <summary>The embedded font, or <c>null</c> for a standard one.</summary>
    public EmbeddedFont? Embedded { get; }

    /// <summary>The standard font; meaningful only when <see cref="Embedded"/> is <c>null</c>.</summary>
    public StandardFont Standard => _standard;

    /// <summary>Whether the font's program is in the document.</summary>
    public bool IsEmbedded => Embedded is not null;

    public static PdfFont FromStandard(StandardFont font) => new(font, null);

    public static PdfFont FromEmbedded(EmbeddedFont font)
    {
        ArgumentNullException.ThrowIfNull(font);
        return new PdfFont(default, font);
    }

    public static implicit operator PdfFont(StandardFont font) => FromStandard(font);

    public static implicit operator PdfFont(EmbeddedFont font) => FromEmbedded(font);

    /// <summary>The width of a string at a size, in points.</summary>
    public double Measure(ReadOnlySpan<char> text, double sizePoints) =>
        Embedded is { } embedded
            ? embedded.MeasureText(text.ToString(), sizePoints)
            : StandardFonts.MeasurePoints(_standard, text, sizePoints);

    /// <summary>The width of a string at a size, in points.</summary>
    public double Measure(string text, double sizePoints) => Measure(text.AsSpan(), sizePoints);

    public override string ToString() => Embedded is { } embedded ? embedded.Font.FullName : _standard.ToString();
}

// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using OfficeNet.Core;
using PdfNet.Document;

namespace PdfNet.Content;

/// <summary>
/// The fonts an exporter draws a document with: embedded when a resolver supplies them, the
/// closest of the standard 14 otherwise — unless every font must be embedded.
/// </summary>
/// <remarks>
/// <para>
/// One choice made the same way by the Word, Excel and PowerPoint exporters. A family the resolver
/// does not have falls back to the default family; a style it does not have falls back as the
/// resolver decides. Two styles that resolve to the same file are one embedded font.
/// </para>
/// <para>
/// For PDF/A a font that is still not found is an error naming it, rather than a standard font
/// that would make the save refuse the document later with less to go on.
/// </para>
/// </remarks>
public sealed class FontSet
{
    private readonly PdfDocument _document;
    private readonly Func<string, bool, bool, byte[]?>? _resolver;
    private readonly string _defaultFamily;
    private readonly bool _requireEmbedded;
    private readonly Dictionary<(string Family, bool Bold, bool Italic), PdfFont> _fonts = [];

    /// <param name="document">The document fonts are embedded in.</param>
    /// <param name="resolver">(family, bold, italic) → TrueType bytes, or <c>null</c> when there is none.</param>
    /// <param name="defaultFamily">The family used when a run names none, and when the resolver lacks one.</param>
    /// <param name="requireEmbedded">Whether a font that cannot be embedded is an error — for PDF/A.</param>
    public FontSet(PdfDocument document, Func<string, bool, bool, byte[]?>? resolver, string defaultFamily,
        bool requireEmbedded = false)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultFamily);

        _document = document;
        _resolver = resolver;
        _defaultFamily = defaultFamily;
        _requireEmbedded = requireEmbedded;
    }

    /// <summary>A set for an export, from its conformance setting.</summary>
    public static FontSet For(PdfDocument document, Func<string, bool, bool, byte[]?>? resolver, string defaultFamily,
        PdfConformance conformance) =>
        new(document, resolver, defaultFamily, conformance != PdfConformance.None);

    /// <summary>The font for a family and style.</summary>
    public PdfFont Get(string? family, bool bold = false, bool italic = false)
    {
        family = string.IsNullOrWhiteSpace(family) ? _defaultFamily : family;

        if (_fonts.TryGetValue((family, bold, italic), out var cached))
        {
            return cached;
        }

        var font = Embed(family, bold, italic) ?? Embed(_defaultFamily, bold, italic);

        if (font is null && _requireEmbedded)
        {
            throw new OfficeNetException(
                $"A PDF/A export embeds every font, and no font file was found for '{family}' or the default " +
                $"'{_defaultFamily}'. " + (_resolver is null
                    ? "Set the export's FontResolver option — FontDirectory.System().Resolve looks in the system's font folders."
                    : "Make FontResolver return a TrueType file for one of them."));
        }

        font ??= StandardFonts.Match(family, bold, italic);
        _fonts[(family, bold, italic)] = font;
        return font;
    }

    private PdfFont? Embed(string family, bool bold, bool italic)
    {
        if (_resolver?.Invoke(family, bold, italic) is not { Length: > 0 } bytes)
        {
            return null;
        }

        // The same bytes twice are one embedded font.
        return PdfFont.FromEmbedded(_document.EmbedFont(bytes));
    }
}

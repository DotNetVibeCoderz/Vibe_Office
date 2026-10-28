// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Text;
using OfficeNet.Core.Markdown;

namespace WordNet.Export;

/// <summary>How a document is written out as Markdown.</summary>
public sealed class WordMarkdownOptions
{
    /// <summary>Whether pictures are embedded as <c>data:</c> URIs rather than replaced by alt text.</summary>
    public bool EmbedImages { get; set; } = true;

    /// <summary>
    /// Decides where each picture lives: given its bytes and alt text, returns the URL to write —
    /// typically after saving the bytes beside the Markdown — or <c>null</c> to fall back to
    /// <see cref="EmbedImages"/>.
    /// </summary>
    public Func<byte[], string?, string?>? ImageUrl { get; set; }

    internal MarkdownWriteOptions ForWriting() => new()
    {
        EmbedImages = EmbedImages,
        ImageUrl = ImageUrl,
    };
}

/// <summary>
/// Converts a document into GitHub-flavoured Markdown.
/// </summary>
/// <remarks>
/// <para>
/// It reads the document exactly as <see cref="WordToHtml"/> does — the same blocks, the same list
/// numbering, the same headings from styles — and hands them to <see cref="MarkdownWriter"/>
/// instead of the HTML writer, so the two exports cannot disagree about what the document says.
/// </para>
/// <para>
/// Headings, lists (nested, numbered, resuming after an interruption), tables, links, pictures,
/// bold, italic, strikethrough and inline code survive. Quotations come from the Quote style;
/// code from a code style or a paragraph set entirely in a monospaced font, with consecutive code
/// paragraphs joined into one listing. What Markdown has no syntax for — colour, fonts, sizes,
/// alignment, underline, merged cells — is dropped rather than approximated.
/// </para>
/// </remarks>
public static class WordToMarkdown
{
    /// <summary>Converts a document to a Markdown string.</summary>
    public static string Convert(WordDocument document, WordMarkdownOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        options ??= new WordMarkdownOptions();

        return MarkdownWriter.Write(WordToHtml.ToBlocks(document, includeImages: true), options.ForWriting());
    }

    /// <summary>Converts a .docx file to a .md file.</summary>
    public static void Convert(string docxPath, string markdownPath, WordMarkdownOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(docxPath);
        ArgumentNullException.ThrowIfNull(markdownPath);

        using var document = WordDocument.Open(docxPath);
        File.WriteAllText(markdownPath, Convert(document, options), new UTF8Encoding(false));
    }
}

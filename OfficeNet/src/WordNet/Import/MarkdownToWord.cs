// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using OfficeNet.Core.Markdown;

namespace WordNet.Import;

/// <summary>
/// Converts Markdown into a Word document.
/// </summary>
/// <remarks>
/// <para>
/// Markdown is defined as HTML: the CommonMark specification says what each construct means by
/// the HTML it produces. So that is the route — <see cref="MarkdownToHtml"/> and then
/// <see cref="HtmlToWord"/> — and a Markdown heading, list or table becomes exactly what the same
/// thing written in HTML becomes, rather than what a second converter thinks it should.
/// </para>
/// <para>
/// The dialect is GitHub's by default (tables, strikethrough, bare URLs); see
/// <see cref="MarkdownOptions"/>. Headings take Word's heading styles, quotations the Quote style,
/// code blocks a monospaced font with their line breaks kept, and pictures are resolved as the
/// HTML importer resolves them: relative paths from <see cref="HtmlWordOptions.BaseDirectory"/>,
/// <c>data:</c> URIs inline, and never a network request unless the caller supplies one.
/// </para>
/// </remarks>
public static class MarkdownToWord
{
    /// <summary>Creates a document from Markdown.</summary>
    public static WordDocument CreateDocument(string markdown, MarkdownOptions? markdownOptions = null,
        HtmlWordOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        return HtmlToWord.CreateDocument(MarkdownToHtml.Convert(markdown, markdownOptions), options);
    }

    /// <summary>Creates a document from a Markdown file, resolving pictures beside it.</summary>
    public static WordDocument CreateDocumentFromFile(string path, MarkdownOptions? markdownOptions = null,
        HtmlWordOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(path);

        options ??= new HtmlWordOptions();
        options.BaseDirectory ??= Path.GetDirectoryName(Path.GetFullPath(path));

        return CreateDocument(File.ReadAllText(path), markdownOptions, options);
    }

    /// <summary>Appends Markdown to a document that already exists.</summary>
    /// <returns>The blocks that were written, in order.</returns>
    public static IReadOnlyList<object> Convert(WordDocument document, string markdown,
        MarkdownOptions? markdownOptions = null, HtmlWordOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(markdown);

        return HtmlToWord.Convert(document, MarkdownToHtml.Convert(markdown, markdownOptions), options);
    }
}

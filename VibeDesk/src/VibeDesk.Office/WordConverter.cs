using System.Net;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using VibeDesk.Application.Documents;

namespace VibeDesk.Office;

/// <summary>
/// .docx to HTML and back.
/// </summary>
/// <remarks>
/// VibeDesk stores a document as HTML, so this maps the OOXML constructs that have an HTML
/// counterpart and drops the ones that do not. Carried: headings, paragraphs, bold/italic/underline
/// and strikethrough runs, bulleted and numbered lists, tables, hyperlinks, line breaks. Dropped:
/// images, footnotes, headers and footers, section breaks, fonts and colours, tracked changes.
/// </remarks>
internal static class WordConverter
{
    // ─────────────────────────────────── read ───────────────────────────────────

    public static DocumentModel Read(Stream source)
    {
        using var package = WordprocessingDocument.Open(source, isEditable: false);

        var body = package.MainDocumentPart?.Document?.Body;
        if (body is null) return new DocumentModel();

        var html = new StringBuilder();

        // List state: OOXML marks each item as a paragraph with a numbering reference rather than
        // wrapping them, so the wrapper has to be opened and closed as the items go by.
        string? openList = null;

        foreach (var element in body.ChildElements)
        {
            switch (element)
            {
                case Paragraph paragraph:
                    AppendParagraph(html, paragraph, ref openList);
                    break;

                case Table table:
                    CloseList(html, ref openList);
                    AppendTable(html, table);
                    break;
            }
        }

        CloseList(html, ref openList);

        var result = html.ToString();

        return new DocumentModel
        {
            Html = result.Length == 0 ? "<p></p>" : result,
            WordCount = WordCount(result),
        };
    }

    private static void AppendParagraph(StringBuilder html, Paragraph paragraph, ref string? openList)
    {
        var style = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? string.Empty;
        var listKind = ListKind(paragraph);
        var inner = Runs(paragraph);

        if (listKind is not null)
        {
            if (openList != listKind)
            {
                CloseList(html, ref openList);
                html.Append('<').Append(listKind).Append('>');
                openList = listKind;
            }

            html.Append("<li>").Append(inner).Append("</li>");
            return;
        }

        CloseList(html, ref openList);

        var tag = style switch
        {
            "Title" or "Heading1" => "h1",
            "Heading2" => "h2",
            "Heading3" => "h3",
            "Heading4" or "Heading5" or "Heading6" => "h4",
            "Quote" or "IntenseQuote" => "blockquote",
            _ => "p",
        };

        // An empty paragraph is a deliberate blank line in Word, so it is kept rather than skipped.
        html.Append('<').Append(tag).Append('>')
            .Append(inner.Length == 0 ? "<br>" : inner)
            .Append("</").Append(tag).Append('>');
    }

    /// <summary>Returns "ul" or "ol" for a list paragraph, or null when it is ordinary text.</summary>
    private static string? ListKind(Paragraph paragraph)
    {
        var numbering = paragraph.ParagraphProperties?.NumberingProperties;
        if (numbering is null) return null;

        var style = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? string.Empty;

        // Word's own style names are the only hint available without walking the numbering part, and
        // resolving that part properly buys very little: the distinction is bullets versus numbers.
        return style.Contains("Number", StringComparison.OrdinalIgnoreCase) ? "ol" : "ul";
    }

    private static void CloseList(StringBuilder html, ref string? openList)
    {
        if (openList is null) return;

        html.Append("</").Append(openList).Append('>');
        openList = null;
    }

    /// <summary>
    /// Renders the inline content of a paragraph or cell in document order. Direct children rather
    /// than descendants, so a hyperlink's runs are rendered once, inside the link, and in the place
    /// the author put them.
    /// </summary>
    private static string Runs(OpenXmlElement container)
    {
        var text = new StringBuilder();

        foreach (var element in container.ChildElements)
        {
            switch (element)
            {
                case Run run:
                    text.Append(Run(run));
                    break;

                case Hyperlink link:
                    var inner = string.Concat(link.Elements<Run>().Select(Run));

                    // Only the display text survives: resolving the relationship needs the part this
                    // method does not have, and a broken href is worse than none.
                    if (inner.Length > 0) text.Append("<a>").Append(inner).Append("</a>");
                    break;

                // Revision containers wrap runs; their content is part of the text either way.
                case InsertedRun or RunPropertiesChange:
                    text.Append(Runs(element));
                    break;
            }
        }

        return text.ToString();
    }

    private static string Run(Run run)
    {
        var text = new StringBuilder();

        foreach (var child in run.ChildElements)
        {
            switch (child)
            {
                case Text t:
                    text.Append(WebUtility.HtmlEncode(t.Text));
                    break;

                case Break:
                    text.Append("<br>");
                    break;

                case TabChar:
                    text.Append("&nbsp;&nbsp;&nbsp;&nbsp;");
                    break;
            }
        }

        var inner = text.ToString();
        if (inner.Length == 0) return string.Empty;

        var properties = run.RunProperties;
        if (properties is null) return inner;

        if (On(properties.Bold)) inner = $"<b>{inner}</b>";
        if (On(properties.Italic)) inner = $"<i>{inner}</i>";
        if (Underlined(properties.Underline)) inner = $"<u>{inner}</u>";
        if (On(properties.Strike)) inner = $"<s>{inner}</s>";

        return inner;
    }

    /// <summary>
    /// A toggle property is on when present without an explicit <c>val="0"</c>. Testing only for
    /// presence would make <c>&lt;w:b w:val="0"/&gt;</c> — Word's way of switching bold *off* — read
    /// as bold.
    /// </summary>
    private static bool On(OnOffType? property) =>
        property is not null && (property.Val is null || property.Val.Value);

    /// <summary>
    /// Underline carries a style rather than a toggle, and <c>none</c> is how Word switches it off.
    /// </summary>
    private static bool Underlined(Underline? underline) =>
        underline is not null &&
        (underline.Val is null || underline.Val.Value != UnderlineValues.None);

    private static void AppendTable(StringBuilder html, Table table)
    {
        html.Append("<table>");

        foreach (var row in table.Elements<TableRow>())
        {
            html.Append("<tr>");

            foreach (var cell in row.Elements<TableCell>())
            {
                var inner = string.Concat(cell.Elements<Paragraph>().Select(Runs));
                html.Append("<td>").Append(inner).Append("</td>");
            }

            html.Append("</tr>");
        }

        html.Append("</table>");
    }

    private static int WordCount(string html) =>
        WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " "))
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Length;
}

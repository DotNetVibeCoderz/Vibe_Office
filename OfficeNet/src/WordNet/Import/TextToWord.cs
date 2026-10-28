// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace WordNet.Import;

/// <summary>
/// Converts plain text into a Word document: one paragraph per line.
/// </summary>
/// <remarks>
/// Every line is kept, blank ones included, so the document has the shape the text had. Tabs
/// become Word tabs. Text is never interpreted — a line starting with <c>#</c> is a line starting
/// with <c>#</c>; use <see cref="MarkdownToWord"/> for that.
/// </remarks>
public static class TextToWord
{
    /// <summary>Creates a document from plain text.</summary>
    /// <param name="text">The text; any line ending is accepted.</param>
    /// <param name="fontName">A font for every paragraph, or <c>null</c> for the document default.</param>
    public static WordDocument CreateDocument(string text, string? fontName = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        var document = WordDocument.Create();

        try
        {
            Convert(document, text, fontName);
            return document;
        }
        catch
        {
            document.Dispose();
            throw;
        }
    }

    /// <summary>Creates a document from a text file, detecting its encoding from a byte-order mark.</summary>
    public static WordDocument CreateDocumentFromFile(string path, string? fontName = null)
    {
        ArgumentNullException.ThrowIfNull(path);

        return CreateDocument(File.ReadAllText(path), fontName);
    }

    /// <summary>Appends plain text to a document, one paragraph per line.</summary>
    /// <returns>The paragraphs that were written.</returns>
    public static IReadOnlyList<Paragraph> Convert(WordDocument document, string text, string? fontName = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(text);

        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');

        // A final line ending ends the last line; it does not start an empty one.
        var count = lines.Length > 1 && lines[^1].Length == 0 ? lines.Length - 1 : lines.Length;
        var paragraphs = new List<Paragraph>(count);

        for (var i = 0; i < count; i++)
        {
            var paragraph = document.AddParagraph();

            if (lines[i].Length > 0)
            {
                var run = paragraph.AddRun(lines[i]);

                if (fontName is not null)
                {
                    run.Format.FontName = fontName;
                }
            }

            paragraphs.Add(paragraph);
        }

        return paragraphs;
    }
}

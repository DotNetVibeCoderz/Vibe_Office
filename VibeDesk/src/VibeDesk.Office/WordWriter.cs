using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using VibeDesk.Application.Documents;

namespace VibeDesk.Office;

/// <summary>
/// HTML to .docx. The other half of <see cref="WordConverter"/>.
/// </summary>
/// <remarks>
/// Writes a self-contained document with a styles part, so headings and lists look like headings and
/// lists when the file is opened in Word rather than like uniformly-formatted paragraphs.
/// </remarks>
internal static class WordWriter
{
    public static void Write(Stream destination, DocumentModel model, string title)
    {
        using var package = WordprocessingDocument.Create(
            destination, WordprocessingDocumentType.Document);

        var main = package.AddMainDocumentPart();
        main.Document = new Document();
        var body = main.Document.AppendChild(new Body());

        AddStyles(main);
        AddNumbering(main);

        // The title is the first heading: an exported file opened on its own should say what it is.
        if (!string.IsNullOrWhiteSpace(title))
        {
            body.AppendChild(Paragraph("Title", [new HtmlSpan(title, false, false, false, false)]));
        }

        foreach (var block in HtmlOutline.Parse(model.Html))
        {
            switch (block.Tag)
            {
                case "table":
                    body.AppendChild(Table(block.Rows ?? []));
                    break;

                case "li":
                    body.AppendChild(ListItem(block.Spans, ordered: false));
                    break;

                case "li-ol":
                    body.AppendChild(ListItem(block.Spans, ordered: true));
                    break;

                default:
                    body.AppendChild(Paragraph(StyleFor(block.Tag), block.Spans));
                    break;
            }
        }

        main.Document.Save();
    }

    private static string StyleFor(string tag) => tag switch
    {
        "h1" => "Heading1",
        "h2" => "Heading2",
        "h3" => "Heading3",
        "h4" or "h5" or "h6" => "Heading4",
        "blockquote" => "Quote",
        _ => "Normal",
    };

    private static Paragraph Paragraph(string style, IEnumerable<HtmlSpan> spans)
    {
        var paragraph = new Paragraph(
            new ParagraphProperties(new ParagraphStyleId { Val = style }));

        foreach (var run in Runs(spans)) paragraph.AppendChild(run);

        return paragraph;
    }

    private static Paragraph ListItem(IEnumerable<HtmlSpan> spans, bool ordered)
    {
        var properties = new ParagraphProperties(
            new ParagraphStyleId { Val = ordered ? "ListNumber" : "ListParagraph" },
            new NumberingProperties(
                new NumberingLevelReference { Val = 0 },
                new NumberingId { Val = ordered ? 2 : 1 }));

        var paragraph = new Paragraph(properties);

        foreach (var run in Runs(spans)) paragraph.AppendChild(run);

        return paragraph;
    }

    private static IEnumerable<Run> Runs(IEnumerable<HtmlSpan> spans)
    {
        foreach (var span in spans)
        {
            // A span may carry newlines from <br>; each becomes a real break so the line structure
            // the author saw is the line structure Word shows.
            var lines = span.Text.Split('\n');

            for (var i = 0; i < lines.Length; i++)
            {
                var run = new Run();

                if (span.Bold || span.Italic || span.Underline || span.Strike)
                {
                    var properties = new RunProperties();

                    if (span.Bold) properties.AppendChild(new Bold());
                    if (span.Italic) properties.AppendChild(new Italic());
                    if (span.Underline) properties.AppendChild(new Underline { Val = UnderlineValues.Single });
                    if (span.Strike) properties.AppendChild(new Strike());

                    run.AppendChild(properties);
                }

                if (i > 0) run.AppendChild(new Break());

                // Preserve, or Word collapses the leading and trailing spaces that separate words.
                run.AppendChild(new Text(lines[i]) { Space = SpaceProcessingModeValues.Preserve });

                yield return run;
            }
        }
    }

    private static Table Table(List<List<string>> rows)
    {
        // Border order is fixed by the schema — top, left, bottom, right, insideH, insideV — and the
        // validator rejects any other sequence rather than sorting it out.
        var table = new Table(new TableProperties(
            new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 4 },
                new LeftBorder { Val = BorderValues.Single, Size = 4 },
                new BottomBorder { Val = BorderValues.Single, Size = 4 },
                new RightBorder { Val = BorderValues.Single, Size = 4 },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 4 })));

        // A table must declare its column grid before its rows, or Word treats the file as damaged.
        var columns = rows.Count > 0 ? rows.Max(r => r.Count) : 1;
        var grid = new TableGrid();

        for (var i = 0; i < columns; i++) grid.AppendChild(new GridColumn());

        table.AppendChild(grid);

        foreach (var row in rows)
        {
            var tableRow = new TableRow();

            foreach (var cell in row)
            {
                tableRow.AppendChild(new TableCell(
                    new Paragraph(new Run(
                        new Text(cell) { Space = SpaceProcessingModeValues.Preserve }))));
            }

            table.AppendChild(tableRow);
        }

        return table;
    }

    /// <summary>
    /// A minimal styles part. Without one, Word renders every paragraph identically no matter what
    /// style id it carries — the headings would be there in the markup and invisible on the page.
    /// </summary>
    private static void AddStyles(MainDocumentPart main)
    {
        var part = main.AddNewPart<StyleDefinitionsPart>();
        var styles = new Styles();

        styles.AppendChild(Heading("Title", "Title", 36, before: 0, after: 240));
        styles.AppendChild(Heading("Heading1", "heading 1", 32, before: 240, after: 120));
        styles.AppendChild(Heading("Heading2", "heading 2", 26, before: 200, after: 100));
        styles.AppendChild(Heading("Heading3", "heading 3", 24, before: 160, after: 80));
        styles.AppendChild(Heading("Heading4", "heading 4", 22, before: 140, after: 80));

        styles.AppendChild(new Style
        {
            Type = StyleValues.Paragraph,
            StyleId = "Quote",
            StyleName = new StyleName { Val = "Quote" },
            StyleParagraphProperties = new StyleParagraphProperties(
                new SpacingBetweenLines { Before = "120", After = "120" },
                new Indentation { Left = "720" }),
            StyleRunProperties = new StyleRunProperties(new Italic()),
        });

        part.Styles = styles;
        part.Styles.Save();
    }

    private static Style Heading(string id, string name, int halfPoints, int before, int after) => new()
    {
        Type = StyleValues.Paragraph,
        StyleId = id,
        StyleName = new StyleName { Val = name },
        StyleParagraphProperties = new StyleParagraphProperties(
            new KeepNext(),
            new SpacingBetweenLines { Before = before.ToString(), After = after.ToString() }),
        StyleRunProperties = new StyleRunProperties(
            new Bold(),
            new FontSize { Val = halfPoints.ToString() }),
    };

    /// <summary>
    /// Numbering definitions for the two list kinds. A list paragraph without these renders as an
    /// ordinary indented paragraph, with no bullet or number at all.
    /// </summary>
    private static void AddNumbering(MainDocumentPart main)
    {
        var part = main.AddNewPart<NumberingDefinitionsPart>();

        var bulletDefinition = new AbstractNum(
            new Level(
                new NumberingFormat { Val = NumberFormatValues.Bullet },
                new LevelText { Val = "•" },
                new PreviousParagraphProperties(new Indentation { Left = "720", Hanging = "360" }))
            { LevelIndex = 0 })
        { AbstractNumberId = 1 };

        var numberDefinition = new AbstractNum(
            new Level(
                new StartNumberingValue { Val = 1 },
                new NumberingFormat { Val = NumberFormatValues.Decimal },
                new LevelText { Val = "%1." },
                new PreviousParagraphProperties(new Indentation { Left = "720", Hanging = "360" }))
            { LevelIndex = 0 })
        { AbstractNumberId = 2 };

        // Abstract definitions must precede the instances that point at them.
        part.Numbering = new Numbering(
            bulletDefinition,
            numberDefinition,
            new NumberingInstance(new AbstractNumId { Val = 1 }) { NumberID = 1 },
            new NumberingInstance(new AbstractNumId { Val = 2 }) { NumberID = 2 });

        part.Numbering.Save();
    }
}

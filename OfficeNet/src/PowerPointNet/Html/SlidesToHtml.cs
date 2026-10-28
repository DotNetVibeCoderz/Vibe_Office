// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Text;
using System.Xml.Linq;
using OfficeNet.Core.Html;
using OfficeNet.Core.Xml;
using PowerPointNet.Charts;
using PowerPointNet.Diagrams;
using PowerPointNet.Shapes;

namespace PowerPointNet.Html;

/// <summary>How a presentation is written out as HTML.</summary>
public sealed class SlideHtmlOptions
{
    /// <summary>A complete page, or only the body's content.</summary>
    public bool FullDocument { get; set; } = true;

    /// <summary>The page title. Defaults to the first slide's title.</summary>
    public string? Title { get; set; }

    /// <summary>Whether pictures are embedded as <c>data:</c> URIs rather than replaced by alt text.</summary>
    public bool EmbedImages { get; set; } = true;

    /// <summary>Whether each slide's speaker notes follow it, as a quotation.</summary>
    public bool IncludeNotes { get; set; }

    /// <summary>Whether slides hidden from the slide show are written too.</summary>
    public bool IncludeHiddenSlides { get; set; }

    internal HtmlWriteOptions ForWriting() => new()
    {
        FullDocument = FullDocument,
        Title = Title,
        EmbedImages = EmbedImages,
    };
}

/// <summary>
/// Converts a presentation into one HTML page — the reverse of <see cref="HtmlToSlides"/>.
/// </summary>
/// <remarks>
/// <para>
/// A slide is a canvas and a page is a flow, so this writes what a slide <em>says</em> rather than
/// where it puts things: the title as a heading, then the rest in reading order — top to bottom,
/// left to right — with bullets as lists keeping their levels, tables as tables, pictures as
/// pictures, a chart as the table of its numbers, and a diagram as a list of its nodes. A rule
/// separates one slide from the next.
/// </para>
/// <para>
/// It goes through the same blocks and the same <see cref="HtmlWriter"/> as the Word exporter, so a
/// deck and a document that say the same thing come out in the same HTML.
/// </para>
/// </remarks>
public static class SlidesToHtml
{
    /// <summary>Converts a presentation to an HTML string.</summary>
    public static string Convert(Presentation presentation, SlideHtmlOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        options ??= new SlideHtmlOptions();
        return HtmlWriter.Write(ToBlocks(presentation, options), options.ForWriting());
    }

    /// <summary>Converts a .pptx file to an .html file.</summary>
    public static void Convert(string pptxPath, string htmlPath, SlideHtmlOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(pptxPath);
        ArgumentNullException.ThrowIfNull(htmlPath);

        using var presentation = Presentation.Open(pptxPath);
        File.WriteAllText(htmlPath, Convert(presentation, options), new UTF8Encoding(false));
    }

    internal static List<HtmlBlock> ToBlocks(Presentation presentation, SlideHtmlOptions options)
    {
        var blocks = new List<HtmlBlock>();
        var lists = 0;
        var first = true;

        foreach (var slide in presentation.Slides)
        {
            if (slide.IsHidden && !options.IncludeHiddenSlides)
            {
                continue;
            }

            if (!first)
            {
                blocks.Add(new HtmlBlock { Kind = HtmlBlockKind.Rule });
            }

            first = false;

            var title = slide.Title;
            blocks.Add(new HtmlBlock
            {
                Kind = HtmlBlockKind.Heading,
                HeadingLevel = 2,
                Spans = [new HtmlSpan(title?.Text.Trim() is { Length: > 0 } text ? text : $"Slide {slide.SlideNumber}", default, null)],
            });

            // Reading order: what is higher comes first, and of two things level, the one on the left.
            var shapes = slide.Shapes.Where(s => title is null || s.Element != title.Element)
                .OrderBy(s => Math.Round(s.Top.Points / 6)).ThenBy(s => s.Left.Points);

            foreach (var shape in shapes)
            {
                AddShape(slide, shape, blocks, ref lists);
            }

            if (options.IncludeNotes && slide.Notes.Trim() is { Length: > 0 } notes)
            {
                foreach (var line in notes.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    blocks.Add(new HtmlBlock { Kind = HtmlBlockKind.Paragraph, Spans = [new HtmlSpan(line, default, null)], Quote = true });
                }
            }
        }

        return blocks;
    }

    private static void AddShape(Slide slide, Shape shape, List<HtmlBlock> blocks, ref int lists)
    {
        switch (shape)
        {
            case SlideTable table:
                var rows = table.Rows.Select(row => row.Cells.Select(cell => cell.Text.Trim()).ToList()).ToList();

                if (rows.Count > 0)
                {
                    blocks.Add(new HtmlBlock { Kind = HtmlBlockKind.Table, TableRows = rows, TableHasHeader = table.HasHeaderRow });
                }

                return;

            case Picture picture:
                if (picture.GetImageBytes() is { Length: > 0 } image)
                {
                    blocks.Add(new HtmlBlock { Kind = HtmlBlockKind.Image, Image = image, AltText = picture.AltText ?? picture.Name });
                }

                return;

            case SlideChart chart:
                AddChart(chart.GetData(), blocks);
                return;

            case SmartArt diagram:
                lists++;

                foreach (var node in diagram.Nodes.Where(n => n.Trim().Length > 0))
                {
                    blocks.Add(new HtmlBlock { Kind = HtmlBlockKind.ListItem, ListId = lists, Spans = [new HtmlSpan(node.Trim(), default, null)] });
                }

                return;
        }

        if (shape.Kind == ShapeKind.Group)
        {
            // A group's members, in their own reading order.
            var members = shape.Element.Elements()
                .Where(e => e.Name.LocalName is "sp" or "grpSp" or "pic" or "graphicFrame")
                .Select(e => new Shape(shape.Presentation, e))
                .OrderBy(s => Math.Round(s.Top.Points / 6)).ThenBy(s => s.Left.Points);

            foreach (var member in members)
            {
                AddShape(slide, member, blocks, ref lists);
            }

            return;
        }

        if (shape.TextFrame is { } frame)
        {
            AddText(slide, shape, frame, blocks, ref lists);
        }
    }

    /// <summary>A chart as the table of its numbers, under its title — what a page can say without drawing.</summary>
    private static void AddChart(OfficeNet.Core.Charts.ChartData data, List<HtmlBlock> blocks)
    {
        if (data.Title is { Length: > 0 } title)
        {
            blocks.Add(new HtmlBlock { Kind = HtmlBlockKind.Paragraph, Spans = [new HtmlSpan(title, new CssStyle { Bold = true }, null)] });
        }

        var rows = new List<List<string>>();
        rows.Add([string.Empty, .. data.Series.Select(s => s.Name)]);

        for (var i = 0; i < data.Categories.Count; i++)
        {
            rows.Add([data.Categories[i], .. data.Series.Select(s => i < s.Values.Count && !double.IsNaN(s.Values[i])
                ? s.Values[i].ToString(System.Globalization.CultureInfo.InvariantCulture)
                : string.Empty)]);
        }

        blocks.Add(new HtmlBlock { Kind = HtmlBlockKind.Table, TableRows = rows, TableHasHeader = true });
    }

    private static void AddText(Slide slide, Shape shape, TextFrame frame, List<HtmlBlock> blocks, ref int lists)
    {
        // The master bullets a body placeholder's paragraphs unless one says otherwise; a plain
        // text box has no bullets unless it asks for them.
        var bulletedByDefault = shape.IsPlaceholder && shape.PlaceholderType is null or "body" or "obj";
        var listId = ++lists;

        foreach (var paragraph in frame.Paragraphs)
        {
            var spans = paragraph.Runs
                .Where(run => run.Text.Length > 0)
                .Select(run => new HtmlSpan(run.Text, new CssStyle
                {
                    Bold = run.Bold,
                    Italic = run.Italic,
                    Underline = run.Underline,
                    Color = run.Color,
                    FontFamily = run.FontName,
                }, LinkOf(slide, run.Element)))
                .ToList();

            if (spans.All(s => s.Text.Trim().Length == 0))
            {
                continue;
            }

            if (paragraph.HasBullet ?? bulletedByDefault)
            {
                blocks.Add(new HtmlBlock
                {
                    Kind = HtmlBlockKind.ListItem,
                    ListLevel = Math.Clamp(paragraph.Level, 0, 8),
                    Numbered = paragraph.Element.Element(Ns.A + "pPr")?.Element(Ns.A + "buAutoNum") is not null,
                    ListId = listId,
                    Spans = spans,
                });
            }
            else
            {
                blocks.Add(new HtmlBlock
                {
                    Kind = HtmlBlockKind.Paragraph,
                    Spans = spans,
                    Style = new CssStyle
                    {
                        Alignment = paragraph.Alignment switch
                        {
                            TextAlignment.Center => TextAlign.Center,
                            TextAlignment.Right => TextAlign.Right,
                            TextAlignment.Justify => TextAlign.Justify,
                            _ => null,
                        },
                    },
                });
            }
        }
    }

    /// <summary>The target of a run's click link, through the slide's relationships.</summary>
    private static string? LinkOf(Slide slide, XElement run)
    {
        var id = run.Element(Ns.A + "rPr")?.Element(Ns.A + "hlinkClick")?.Attribute(Ns.R + "id")?.Value;

        return id is { Length: > 0 } && slide.Part.RelationshipById(id) is { } relationship ? relationship.Target : null;
    }
}

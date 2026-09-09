// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Xml.Linq;
using OfficeNet.Core;
using OfficeNet.Core.Drawing;
using OfficeNet.Core.Xml;
using PdfNet.Content;
using PdfNet.Document;
using PowerPointNet.Shapes;
using PdfAlignment = PdfNet.Content.TextAlignment;
using SlideAlignment = PowerPointNet.Shapes.TextAlignment;

namespace PowerPointNet.Export;

/// <summary>Options for rendering a presentation to PDF.</summary>
public sealed class SlidePdfOptions
{
    /// <summary>Renders hidden slides too.</summary>
    public bool IncludeHiddenSlides { get; set; }

    /// <summary>Draws pictures. Turning this off produces a much faster text-only PDF.</summary>
    public bool IncludeImages { get; set; } = true;

    /// <summary>Adds each slide's speaker notes below the slide.</summary>
    public bool IncludeNotes { get; set; }

    /// <summary>Prints a slide number in the bottom-right corner.</summary>
    public bool ShowSlideNumbers { get; set; }

    /// <summary>The background used when neither the slide nor its layout sets one.</summary>
    public OfficeColor DefaultBackground { get; set; } = OfficeColor.White;

    /// <summary>A watermark stamped across every page.</summary>
    public string? Watermark { get; set; }
}

/// <summary>
/// Renders a presentation to PDF, one page per slide.
/// </summary>
/// <remarks>
/// <para>
/// A slide is already a fixed-size canvas with absolutely positioned shapes, which makes this a far
/// more faithful conversion than the Word one: there is no reflow to reproduce, only a coordinate
/// change from EMU to points and a y-axis flip.
/// </para>
/// <para>
/// What it does not do is resolve inheritance the way PowerPoint does. A shape's effective
/// formatting can come from its placeholder on the layout, from the layout's placeholder on the
/// master, and from the master's text styles; this reads the shape and falls back to the layout's
/// matching placeholder, which covers generated decks and most authored ones. Gradients, pictures
/// as fills, shadows and 3-D effects are out of scope.
/// </para>
/// </remarks>
public static class PptToPdf
{
    /// <summary>Renders a presentation to a new PDF.</summary>
    public static PdfDocument Convert(Presentation presentation, SlidePdfOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        options ??= new SlidePdfOptions();

        var pdf = PdfDocument.Create();
        pdf.Info.Title = presentation.Properties.Title;
        pdf.Info.Author = presentation.Properties.Creator;
        pdf.Info.Creator = "OfficeNet PowerPointNet by Gravicode Studios";

        var width = presentation.SlideWidth.Points;
        var height = presentation.SlideHeight.Points;
        var pageSize = PageSize.Points(width, height);

        foreach (var slide in presentation.Slides)
        {
            if (slide.IsHidden && !options.IncludeHiddenSlides)
            {
                continue;
            }

            var page = pdf.Pages.Add(pageSize);
            RenderSlide(presentation, slide, page, options);
        }

        if (pdf.Pages.Count == 0)
        {
            pdf.Pages.Add(pageSize);
        }

        return pdf;
    }

    private static void RenderSlide(Presentation presentation, Slide slide, PdfPage page,
        SlidePdfOptions options)
    {
        var layout = presentation.LayoutOf(slide);

        using (var canvas = page.OpenCanvas())
        {
            canvas.TopDown = true;

            var background = slide.BackgroundColor
                             ?? layout?.BackgroundColor
                             ?? presentation.Master?.BackgroundColor
                             ?? options.DefaultBackground;

            canvas.SetFillColor(background);
            canvas.Rectangle(0, 0, page.MediaBox.Width, page.MediaBox.Height).Fill();

            foreach (var shape in slide.Shapes)
            {
                RenderShape(presentation, canvas, shape, layout, options);
            }

            if (options.ShowSlideNumbers)
            {
                canvas.SetFont(StandardFont.Helvetica, 10);
                canvas.SetFillColor(OfficeColor.Gray);

                var text = slide.SlideNumber.ToString();
                var textWidth = StandardFonts.MeasurePoints(StandardFont.Helvetica, text, 10);

                canvas.DrawText(text, page.MediaBox.Width - 30 - textWidth,
                    page.MediaBox.Height - 20);
            }

            if (options.IncludeNotes && slide.Notes is { Length: > 0 } notes)
            {
                canvas.SetFont(StandardFont.HelveticaOblique, 9);
                canvas.SetFillColor(OfficeColor.FromRgb(0x59, 0x59, 0x59));
                canvas.DrawWrappedText(notes, 30, page.MediaBox.Height - 46,
                    page.MediaBox.Width - 60, 11);
            }
        }

        if (options.Watermark is { Length: > 0 } watermark)
        {
            PdfNet.Annotations.AnnotationExtensions.AddWatermark(page, watermark, OfficeColor.Gray, 0.1);
        }
    }

    private static void RenderShape(Presentation presentation, PdfCanvas canvas, Shape shape,
        Layouts.SlideLayout? layout, SlidePdfOptions options)
    {
        var bounds = ResolveBounds(shape, layout);

        if (bounds is not { } box)
        {
            return;
        }

        var (left, top, width, height) = box;

        if (width <= 0 || height <= 0)
        {
            return;
        }

        if (shape is Picture picture)
        {
            if (!options.IncludeImages)
            {
                return;
            }

            var bytes = picture.GetImageBytes();

            if (bytes is null)
            {
                return;
            }

            try
            {
                canvas.DrawImage(bytes, left, top, width, height);
            }
            catch (OfficeNetException)
            {
                // A format PdfNet cannot embed leaves a gap rather than aborting the deck.
            }

            return;
        }

        if (shape is SlideTable table)
        {
            RenderTable(canvas, table, left, top, width, height);
            return;
        }

        if (shape is Charts.SlideChart chart)
        {
            ChartRenderer.Render(canvas, chart.GetData(), left, top, width, height);
            return;
        }

        if (shape is Diagrams.SmartArt diagram)
        {
            RenderDiagram(canvas, diagram, left, top, width, height);
            return;
        }

        if (shape.FillColor is { } fill)
        {
            canvas.SetFillColor(fill);
            canvas.Rectangle(left, top, width, height).Fill();
        }

        if (shape.LineColor is { } line)
        {
            canvas.SetStrokeColor(line);
            canvas.SetLineWidth(Math.Max(0.5, shape.LineWidth.Points));
            canvas.Rectangle(left, top, width, height).Stroke();
        }

        var frame = shape.TextFrame;

        if (frame is null)
        {
            return;
        }

        RenderTextFrame(presentation, canvas, frame, shape, layout, left, top, width, height);
    }

    /// <summary>
    /// Draws a SmartArt diagram from its cached shapes.
    /// </summary>
    /// <remarks>
    /// The four standard diagram parts describe an algorithm, not a picture. Running that algorithm
    /// is PowerPoint's job; what is drawn here is the <c>dsp:drawing</c> part, which holds the
    /// shapes the algorithm produced and which PowerPoint keeps in step. Reading the data model and
    /// laying it out again would produce a second opinion about where the boxes go, and the two
    /// would disagree the moment anyone edited the diagram.
    /// </remarks>
    private static void RenderDiagram(PdfCanvas canvas, Diagrams.SmartArt diagram,
        double left, double top, double width, double height)
    {
        var tree = diagram.DrawingPart.Xml.Root?.Element(Ns.Dsp + "spTree");

        if (tree is null)
        {
            return;
        }

        foreach (var shape in tree.Elements(Ns.Dsp + "sp"))
        {
            var properties = shape.Element(Ns.Dsp + "spPr");
            var transform = properties?.Element(Ns.A + "xfrm");
            var offset = transform?.Element(Ns.A + "off");
            var extent = transform?.Element(Ns.A + "ext");

            if (offset is null || extent is null)
            {
                continue;
            }

            // Diagram coordinates are relative to the frame, so the frame's own position is added
            // here rather than baked into the part — which is what lets a diagram be moved without
            // rewriting every shape in it.
            var x = left + Length.FromEmu(offset.LongAttr("x")).Points;
            var y = top + Length.FromEmu(offset.LongAttr("y")).Points;
            var w = Length.FromEmu(extent.LongAttr("cx")).Points;
            var h = Length.FromEmu(extent.LongAttr("cy")).Points;

            if (w <= 0 || h <= 0)
            {
                continue;
            }

            var preset = properties?.Element(Ns.A + "prstGeom");
            var geometry = preset?.Attr("prst") ?? "rect";
            var adjust = Adjustment(preset);
            var fill = SolidFill(properties?.Element(Ns.A + "solidFill"));
            var line = properties?.Element(Ns.A + "ln");
            var stroke = SolidFill(line?.Element(Ns.A + "solidFill"));

            var rotation = transform.Attr("rot") is { } rot &&
                           long.TryParse(rot, System.Globalization.CultureInfo.InvariantCulture,
                               out var units)
                ? units / 60000.0
                : 0;

            canvas.Save();

            if (rotation != 0)
            {
                // About the shape's own centre, named in PDF space: the canvas flips y per drawing
                // call rather than through the matrix, so a centre given top-down turns the shape
                // about a point reflected across the middle of the page.
                var centreX = x + (w / 2);
                var centreY = canvas.PageHeight - (y + (h / 2));

                canvas.Transform(1, 0, 0, 1, centreX, centreY);
                canvas.Rotate(-rotation);
                canvas.Transform(1, 0, 0, 1, -centreX, -centreY);
            }

            if (fill is not null || stroke is not null)
            {
                if (fill is { } fillColor)
                {
                    canvas.SetFillColor(fillColor);
                }

                if (stroke is { } strokeColor)
                {
                    canvas.SetStrokeColor(strokeColor);
                    canvas.SetLineWidth(Math.Max(0.5,
                        Length.FromEmu(line.LongAttr("w")).Points));
                }

                TraceDiagramShape(canvas, geometry, x, y, w, h, adjust);

                if (fill is not null && stroke is not null)
                {
                    canvas.FillAndStroke();
                }
                else if (fill is not null)
                {
                    canvas.Fill();
                }
                else
                {
                    canvas.Stroke();
                }
            }

            RenderDiagramText(canvas, shape, x, y, w, h, geometry, adjust);

            canvas.Restore();
        }
    }

    /// <summary>The preset's <c>adj</c> guide as a fraction, or <c>null</c> for its default.</summary>
    private static double? Adjustment(XElement? preset) =>
        preset?.Element(Ns.A + "avLst")?.Elements(Ns.A + "gd")
            .FirstOrDefault(g => g.Attr("name") == "adj")?.Attr("fmla") is { } formula &&
        formula.StartsWith("val ", StringComparison.Ordinal) &&
        double.TryParse(formula[4..], System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value / 100000
            : null;

    /// <summary>
    /// Traces the preset geometries a diagram uses.
    /// </summary>
    /// <remarks>
    /// <c>adjust</c> is the preset's own adjustment as a fraction, or <c>null</c> for the preset's
    /// default. What it means differs per shape, which is why each case decides for itself.
    /// </remarks>
    private static void TraceDiagramShape(PdfCanvas canvas, string geometry,
        double x, double y, double width, double height, double? adjust = null)
    {
        switch (geometry)
        {
            case "ellipse":
                canvas.Ellipse(x, y, width, height);
                break;

            case "roundRect":
                canvas.RoundedRectangle(x, y, width, height, Math.Min(width, height) * 0.16667);
                break;

            case "triangle":
                canvas.MoveTo(x + (width / 2), y);
                canvas.LineTo(x + width, y + height);
                canvas.LineTo(x, y + height);
                canvas.ClosePath();
                break;

            case "trapezoid":
            {
                // How far in each top corner sits, as a fraction of the width. A fixed value lines
                // the bands of a pyramid up at exactly one band count and at no other, so the shape
                // carries its own.
                var inset = width * Math.Clamp(adjust ?? 0.25, 0, 0.5);

                canvas.MoveTo(x + inset, y);
                canvas.LineTo(x + width - inset, y);
                canvas.LineTo(x + width, y + height);
                canvas.LineTo(x, y + height);
                canvas.ClosePath();
                break;
            }

            case "chevron":
            case "homePlate":
            {
                // A chevron is a home plate with a notch cut out of its left edge. The point is a
                // fraction of the width and capped at half the height, so a tall box does not grow
                // a spike and a wide one keeps a readable body.
                var point = Math.Min(width * Math.Clamp(adjust ?? 0.25, 0, 0.5), height / 2);

                canvas.MoveTo(x, y);
                canvas.LineTo(x + width - point, y);
                canvas.LineTo(x + width, y + (height / 2));
                canvas.LineTo(x + width - point, y + height);
                canvas.LineTo(x, y + height);

                if (geometry == "chevron")
                {
                    canvas.LineTo(x + point, y + (height / 2));
                }

                canvas.ClosePath();
                break;
            }

            case "rightArrow":
            {
                var head = width * 0.45;
                var shaft = height * 0.3;

                canvas.MoveTo(x, y + ((height - shaft) / 2));
                canvas.LineTo(x + width - head, y + ((height - shaft) / 2));
                canvas.LineTo(x + width - head, y);
                canvas.LineTo(x + width, y + (height / 2));
                canvas.LineTo(x + width - head, y + height);
                canvas.LineTo(x + width - head, y + ((height + shaft) / 2));
                canvas.LineTo(x, y + ((height + shaft) / 2));
                canvas.ClosePath();
                break;
            }

            default:
                canvas.Rectangle(x, y, width, height);
                break;
        }
    }

    /// <summary>Draws a diagram shape's text, centred in the part of the box that can hold it.</summary>
    /// <remarks>
    /// Centring on the box is right for a rectangle and wrong for a chevron, whose left edge is a
    /// notch: the label then sits half over the shape behind it. The offset shifts the text into the
    /// body, which is the whole reason the geometry is passed in.
    /// </remarks>
    private static void RenderDiagramText(PdfCanvas canvas, XElement shape,
        double x, double y, double width, double height, string geometry = "rect",
        double? adjust = null)
    {
        var body = shape.Element(Ns.Dsp + "txBody");

        if (body is null)
        {
            return;
        }

        var runs = body.Descendants(Ns.A + "r").ToList();

        if (runs.Count == 0)
        {
            return;
        }

        var text = string.Concat(runs.Select(r => r.Element(Ns.A + "t")?.Value ?? string.Empty));

        if (text.Trim().Length == 0)
        {
            return;
        }

        var properties = runs[0].Element(Ns.A + "rPr");
        var size = properties?.Attr("sz") is { } sz &&
                   double.TryParse(sz, System.Globalization.CultureInfo.InvariantCulture, out var centipoints)
            ? centipoints / 100
            : 12;

        var color = SolidFill(properties?.Element(Ns.A + "solidFill")) ?? OfficeColor.White;
        var font = StandardFonts.Match("Calibri", bold: false, italic: false);

        canvas.SetFont(font, size);
        canvas.SetFillColor(color);

        var point = geometry is "chevron" or "homePlate"
            ? Math.Min(width * Math.Clamp(adjust ?? 0.25, 0, 0.5), height / 2)
            : 0;

        // A chevron loses its left edge to the notch and its right to the point; a home plate only
        // loses the right. Either way the usable body is narrower than the box, and centred inside it.
        var bodyLeft = x + (geometry == "chevron" ? point : 0);
        var bodyWidth = width - point - (geometry == "chevron" ? point : 0);

        // Wrapped, because a diagram's labels are short but not always short enough.
        var inset = bodyWidth * 0.06;
        var available = Math.Max(10, bodyWidth - (inset * 2));
        var lines = WrapDiagramText(text, font, size, available);

        var lineHeight = size * 1.2;
        var block = lineHeight * lines.Count;
        var cursor = y + ((height - block) / 2) + (size * 0.85);

        foreach (var line in lines)
        {
            var lineWidth = StandardFonts.MeasurePoints(font, line, size);
            canvas.DrawText(line, bodyLeft + ((bodyWidth - lineWidth) / 2), cursor);
            cursor += lineHeight;
        }
    }

    private static List<string> WrapDiagramText(string text, StandardFont font, double size,
        double available)
    {
        var lines = new List<string>();
        var current = new System.Text.StringBuilder();

        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = current.Length == 0 ? word : current + " " + word;

            if (StandardFonts.MeasurePoints(font, candidate, size) > available && current.Length > 0)
            {
                lines.Add(current.ToString());
                current.Clear();
                current.Append(word);
            }
            else
            {
                current.Clear();
                current.Append(candidate);
            }
        }

        if (current.Length > 0)
        {
            lines.Add(current.ToString());
        }

        return lines.Count == 0 ? [text] : lines;
    }

    private static OfficeColor? SolidFill(XElement? fill) =>
        fill?.Element(Ns.A + "srgbClr")?.Attr("val") is { } hex &&
        OfficeColor.TryParse(hex, out var color)
            ? color
            : null;

    /// <summary>
    /// Finds a shape's position, falling back to its placeholder on the layout.
    /// </summary>
    /// <remarks>
    /// A placeholder on a slide usually carries no transform of its own — it inherits the layout's.
    /// Treating a missing transform as (0,0) stacks every placeholder in the top-left corner, which
    /// is the single most visible way a slide converter goes wrong.
    /// </remarks>
    private static (double Left, double Top, double Width, double Height)? ResolveBounds(
        Shape shape, Layouts.SlideLayout? layout)
    {
        if (shape.Width.Emu > 0 && shape.Height.Emu > 0)
        {
            return (shape.Left.Points, shape.Top.Points, shape.Width.Points, shape.Height.Points);
        }

        if (!shape.IsPlaceholder || layout is null)
        {
            return null;
        }

        var match = layout.Placeholders.FirstOrDefault(p =>
                        p.PlaceholderType == shape.PlaceholderType &&
                        p.PlaceholderIndex == shape.PlaceholderIndex)
                    ?? layout.Placeholders.FirstOrDefault(p =>
                        p.PlaceholderType == shape.PlaceholderType);

        if (match is null || match.Width.Emu <= 0)
        {
            return null;
        }

        return (match.Left.Points, match.Top.Points, match.Width.Points, match.Height.Points);
    }

    private static void RenderTextFrame(Presentation presentation, PdfCanvas canvas, TextFrame frame,
        Shape shape, Layouts.SlideLayout? layout, double left, double top, double width, double height)
    {
        const double Inset = 7.2;

        var contentLeft = left + Inset;
        var contentWidth = Math.Max(10, width - Inset * 2);

        var isTitle = shape.PlaceholderType is "title" or "ctrTitle";
        var lines = new List<(string Text, double Size, bool Bold, bool Italic, OfficeColor Color,
            SlideAlignment Alignment, int Level, bool Bullet, string Font)>();

        foreach (var paragraph in frame.Paragraphs)
        {
            var text = paragraph.Text;
            var run = paragraph.Runs.FirstOrDefault();

            // Sizes fall back to the master's own scale: 44 pt for a title, 28 pt for level-zero
            // body text, stepping down 4 pt per level.
            var size = run?.FontSize?.Points
                       ?? (isTitle ? 32 : Math.Max(12, 24 - paragraph.Level * 4));

            var color = run?.Color
                        ?? (isTitle ? OfficeColor.FromRgb(0x1F, 0x38, 0x64) : OfficeColor.Black);

            var alignment = paragraph.Alignment
                            ?? (shape.PlaceholderType is "ctrTitle" or "subTitle"
                                ? SlideAlignment.Center
                                : SlideAlignment.Left);

            var bullet = paragraph.HasBullet
                         ?? shape.PlaceholderType is "body" && text.Length > 0;

            lines.Add((text, size, run?.Bold ?? isTitle, run?.Italic ?? false, color, alignment,
                paragraph.Level, bullet, run?.FontName ?? "Calibri"));
        }

        if (lines.Count == 0)
        {
            return;
        }

        // Vertical anchoring needs the total height first, so the text is measured before drawing.
        double totalHeight = 0;

        foreach (var line in lines)
        {
            var font = StandardFonts.Match(line.Font, line.Bold, line.Italic);
            var indent = line.Level * 18.0 + (line.Bullet ? 14 : 0);
            var wrapped = CountWrappedLines(font, line.Text, line.Size, contentWidth - indent);
            totalHeight += wrapped * line.Size * 1.2 + 4;
        }

        var cursor = frame.Anchor switch
        {
            TextAnchor.Middle => top + Math.Max(0, (height - totalHeight) / 2),
            TextAnchor.Bottom => top + Math.Max(0, height - totalHeight - Inset),
            _ => top + Inset,
        };

        foreach (var line in lines)
        {
            var font = StandardFonts.Match(line.Font, line.Bold, line.Italic);
            var indent = line.Level * 18.0 + (line.Bullet ? 14 : 0);

            canvas.SetFont(font, line.Size);
            canvas.SetFillColor(line.Color);

            if (line.Bullet && line.Text.Length > 0)
            {
                canvas.DrawText("•", contentLeft + line.Level * 18.0, cursor + line.Size * 0.85);
            }

            var used = canvas.DrawWrappedText(line.Text, contentLeft + indent, cursor,
                contentWidth - indent, line.Size * 1.2,
                line.Alignment switch
                {
                    SlideAlignment.Center => PdfAlignment.Center,
                    SlideAlignment.Right => PdfAlignment.Right,
                    SlideAlignment.Justify => PdfAlignment.Justify,
                    _ => PdfAlignment.Left,
                });

            cursor += used + 4;
        }
    }

    private static int CountWrappedLines(StandardFont font, string text, double size, double width)
    {
        if (text.Length == 0 || width <= 0)
        {
            return 1;
        }

        var count = 0;

        foreach (var paragraph in text.Replace("\r\n", "\n").Split('\n'))
        {
            var lines = 1;
            double used = 0;

            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var wordWidth = StandardFonts.MeasurePoints(font, word + " ", size);

                if (used + wordWidth > width && used > 0)
                {
                    lines++;
                    used = wordWidth;
                }
                else
                {
                    used += wordWidth;
                }
            }

            count += lines;
        }

        return Math.Max(1, count);
    }

    private static void RenderTable(PdfCanvas canvas, SlideTable table, double left, double top,
        double width, double height)
    {
        var grid = table.Element.Descendants(Ns.A + "gridCol")
            .Select(c => Length.FromEmu(c.LongAttr("w")).Points)
            .ToArray();

        if (grid.Length == 0)
        {
            return;
        }

        var declared = grid.Sum();

        // The grid should already sum to the frame width; scaling covers a table whose columns
        // were edited without the frame being resized.
        var scale = declared > 0 ? width / declared : 1;

        var rows = table.Rows;
        var rowHeights = rows.Select(r =>
        {
            var h = r.Height.Points;
            return h > 0 ? h : height / Math.Max(1, rows.Count);
        }).ToArray();

        var totalHeight = rowHeights.Sum();
        var verticalScale = totalHeight > 0 ? height / totalHeight : 1;

        var y = top;

        for (var r = 0; r < rows.Count; r++)
        {
            var rowHeight = rowHeights[r] * verticalScale;
            var x = left;
            var column = 0;

            foreach (var cell in rows[r].Cells)
            {
                var span = Math.Max(1, cell.GridSpan);
                var cellWidth = 0.0;

                for (var i = 0; i < span && column + i < grid.Length; i++)
                {
                    cellWidth += grid[column + i] * scale;
                }

                if (cell.FillColor is { } fill)
                {
                    canvas.SetFillColor(fill);
                    canvas.Rectangle(x, y, cellWidth, rowHeight).Fill();
                }
                else if (r == 0 && table.HasHeaderRow)
                {
                    // A header row with no explicit fill still needs to read as a header; this is
                    // the accent the built-in table style would apply.
                    canvas.SetFillColor(OfficeColor.FromRgb(0x2E, 0x54, 0x96));
                    canvas.Rectangle(x, y, cellWidth, rowHeight).Fill();
                }

                canvas.SetStrokeColor(OfficeColor.White);
                canvas.SetLineWidth(1);
                canvas.Rectangle(x, y, cellWidth, rowHeight).Stroke();

                var text = cell.Text;

                if (text.Length > 0)
                {
                    var run = cell.TextFrame.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault();
                    var bold = run?.Bold ?? r == 0 && table.HasHeaderRow;
                    var size = run?.FontSize?.Points ?? 12;

                    var color = run?.Color
                                ?? (r == 0 && table.HasHeaderRow && cell.FillColor is null
                                    ? OfficeColor.White
                                    : OfficeColor.Black);

                    canvas.SetFont(StandardFonts.Match(run?.FontName ?? "Calibri", bold, false), size);
                    canvas.SetFillColor(color);
                    canvas.DrawText(text, x + 6, y + (rowHeight + size) / 2 - size * 0.25);
                }

                x += cellWidth;
                column += span;
            }

            y += rowHeight;
        }
    }
}

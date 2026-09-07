// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using OfficeNet.Core.Charts;
using OfficeNet.Core.Drawing;
using OfficeNet.Core;
using PdfNet.Content;
using PowerPointNet.Charts;
using System.Globalization;
using PdfAlignment = PdfNet.Content.TextAlignment;

namespace PowerPointNet.Export;

/// <summary>
/// Draws a chart into a PDF page.
/// </summary>
/// <remarks>
/// <para>
/// A chart in a <c>.pptx</c> is data, not a picture: PowerPoint draws it at display time from the
/// cached numbers. Exporting a deck to PDF therefore means drawing it here, or the slide comes out
/// with its title and an empty rectangle where the chart was.
/// </para>
/// <para>
/// This is a plot of the data, not a reproduction of PowerPoint's renderer. Axis ticks are chosen
/// by the same "nice numbers" rule most plotting libraries use rather than Excel's exact algorithm,
/// so a gridline may land on 250 where PowerPoint chose 200. The bars, points and slices themselves
/// are positioned from the values and are accurate.
/// </para>
/// </remarks>
internal static class ChartRenderer
{
    private const double TitleSize = 14;
    private const double LabelSize = 8;
    private const double LegendSize = 9;

    internal static void Render(
        PdfCanvas canvas, ChartData data, double left, double top, double width, double height)
    {
        if (data.Series.Count == 0 || width <= 0 || height <= 0)
        {
            return;
        }

        var plot = new Rect(left, top, width, height);

        if (data.Title is { Length: > 0 } title)
        {
            canvas.SetFont(StandardFont.HelveticaBold, TitleSize);
            canvas.SetFillColor(OfficeColor.FromRgb(0x40, 0x40, 0x40));
            canvas.DrawText(title, plot.Left, plot.Top + TitleSize, plot.Width, PdfAlignment.Center);

            plot = plot.Inset(top: TitleSize + 8);
        }

        if (data.Legend != LegendPosition.None && data.Series.Count > 0)
        {
            plot = DrawLegend(canvas, data, plot);
        }

        if (data.IsPieFamily)
        {
            DrawPie(canvas, data, plot);
            return;
        }

        DrawAxisChart(canvas, data, plot);
    }

    // ---- Legend --------------------------------------------------------------------------------

    private static Rect DrawLegend(PdfCanvas canvas, ChartData data, Rect plot)
    {
        // A pie's legend names the categories; every other chart's names the series. Labelling a
        // pie with its single series name would produce a legend with one meaningless entry.
        var labels = data.IsPieFamily
            ? data.Categories
            : [.. data.Series.Select(s => s.Name)];

        if (labels.Count == 0)
        {
            return plot;
        }

        canvas.SetFont(StandardFont.Helvetica, LegendSize);

        const double SwatchSize = 7;
        const double Gap = 12;

        var widths = labels
            .Select(label => SwatchSize + 4 + canvas.MeasureText(label) + Gap)
            .ToArray();

        var total = widths.Sum() - Gap;

        var vertical = data.Legend is LegendPosition.Left or LegendPosition.Right;

        if (vertical)
        {
            var columnWidth = widths.Max() - Gap + 6;
            columnWidth = Math.Min(columnWidth, plot.Width / 3);

            var x = data.Legend == LegendPosition.Left ? plot.Left : plot.Right - columnWidth;
            var y = plot.Top + ((plot.Height - (labels.Count * (SwatchSize + 6))) / 2);

            for (var i = 0; i < labels.Count; i++)
            {
                DrawLegendEntry(canvas, data, i, labels[i], x, y, SwatchSize);
                y += SwatchSize + 6;
            }

            return data.Legend == LegendPosition.Left
                ? plot.Inset(left: columnWidth + 6)
                : plot.Inset(right: columnWidth + 6);
        }

        var startX = plot.Left + Math.Max(0, (plot.Width - total) / 2);
        var rowY = data.Legend == LegendPosition.Top ? plot.Top : plot.Bottom - SwatchSize - 2;

        for (var i = 0; i < labels.Count; i++)
        {
            DrawLegendEntry(canvas, data, i, labels[i], startX, rowY, SwatchSize);
            startX += widths[i];
        }

        return data.Legend == LegendPosition.Top
            ? plot.Inset(top: SwatchSize + 10)
            : plot.Inset(bottom: SwatchSize + 10);
    }

    private static void DrawLegendEntry(
        PdfCanvas canvas, ChartData data, int index, string label, double x, double y, double size)
    {
        canvas.SetFillColor(ColorFor(data, index));
        canvas.Rectangle(x, y, size, size).Fill();

        canvas.SetFillColor(OfficeColor.FromRgb(0x40, 0x40, 0x40));
        canvas.SetFont(StandardFont.Helvetica, LegendSize);
        canvas.DrawText(label, x + size + 4, y + size - 0.5);
    }

    // ---- Pie and doughnut ----------------------------------------------------------------------

    private static void DrawPie(PdfCanvas canvas, ChartData data, Rect plot)
    {
        var values = data.Series[0].Values;
        var total = values.Where(v => !double.IsNaN(v) && v > 0).Sum();

        if (total <= 0)
        {
            return;
        }

        var radius = Math.Min(plot.Width, plot.Height) / 2 * 0.9;
        var cx = plot.Left + (plot.Width / 2);
        var cy = plot.Top + (plot.Height / 2);

        // PDF angles run counter-clockwise from east; a pie reads clockwise from north, which is
        // what every spreadsheet draws and what a reader expects.
        var angle = 90.0;

        for (var i = 0; i < values.Count; i++)
        {
            var value = values[i];

            if (double.IsNaN(value) || value <= 0)
            {
                continue;
            }

            var sweep = value / total * 360.0;

            canvas.SetFillColor(ChartData.DefaultPalette[i % ChartData.DefaultPalette.Length]);
            DrawSlice(canvas, cx, cy, radius, angle, angle - sweep,
                data.Type == ChartType.Doughnut ? radius * data.DoughnutHoleSize / 100.0 : 0);

            if (data.ShowDataLabels || data.ShowPercentages)
            {
                var middle = (angle - (sweep / 2)) * Math.PI / 180;
                var labelRadius = radius * (data.Type == ChartType.Doughnut ? 0.8 : 0.65);

                var text = data.ShowPercentages
                    ? $"{value / total * 100:0.#}%"
                    : Format(value, data.ValueFormat);

                canvas.SetFont(StandardFont.HelveticaBold, LabelSize);
                canvas.SetFillColor(OfficeColor.White);

                var textWidth = canvas.MeasureText(text);
                canvas.DrawText(text,
                    cx + (Math.Cos(middle) * labelRadius) - (textWidth / 2),
                    cy - (Math.Sin(middle) * labelRadius) + (LabelSize / 3));
            }

            angle -= sweep;
        }
    }

    private static void DrawSlice(PdfCanvas canvas, double cx, double cy, double radius,
        double fromDegrees, double toDegrees, double innerRadius)
    {
        // Flat-shaded polygons rather than Béziers: at slide scale the difference is invisible and
        // an arc approximation that is subtly wrong is worse than a polygon that is exactly the
        // shape it claims to be.
        var steps = Math.Max(2, (int)Math.Ceiling(Math.Abs(fromDegrees - toDegrees) / 3));
        var points = new List<(double X, double Y)>();

        for (var i = 0; i <= steps; i++)
        {
            var t = fromDegrees + ((toDegrees - fromDegrees) * i / steps);
            var radians = t * Math.PI / 180;
            points.Add((cx + (Math.Cos(radians) * radius), cy - (Math.Sin(radians) * radius)));
        }

        if (innerRadius > 0)
        {
            for (var i = steps; i >= 0; i--)
            {
                var t = fromDegrees + ((toDegrees - fromDegrees) * i / steps);
                var radians = t * Math.PI / 180;
                points.Add((cx + (Math.Cos(radians) * innerRadius), cy - (Math.Sin(radians) * innerRadius)));
            }
        }
        else
        {
            points.Add((cx, cy));
        }

        canvas.MoveTo(points[0].X, points[0].Y);

        for (var i = 1; i < points.Count; i++)
        {
            canvas.LineTo(points[i].X, points[i].Y);
        }

        canvas.ClosePath().Fill();
    }

    // ---- Bar, column, line, area, scatter and radar ---------------------------------------------

    private static void DrawAxisChart(PdfCanvas canvas, ChartData data, Rect plot)
    {
        var stacked = data.Type is ChartType.ColumnStacked or ChartType.BarStacked
            or ChartType.AreaStacked;

        var horizontal = data.Type is ChartType.Bar or ChartType.BarStacked;

        var (minimum, maximum, step) = ChooseScale(data, stacked);

        canvas.SetFont(StandardFont.Helvetica, LabelSize);

        // The value labels decide how much room the axis needs, so they are measured before the
        // plot rectangle is fixed rather than assumed to fit.
        var labelWidth = 0.0;

        for (var value = minimum; value <= maximum + (step / 2); value += step)
        {
            labelWidth = Math.Max(labelWidth, canvas.MeasureText(Format(value, data.ValueFormat)));
        }

        var axisGap = LabelSize + 6;

        var area = horizontal
            ? plot.Inset(left: MeasureCategories(canvas, data) + 6, bottom: axisGap)
            : plot.Inset(left: labelWidth + 6, bottom: axisGap);

        // The value-axis title needs a row of its own above the plot. Insetting the left edge, as
        // a rotated title would, leaves it sitting on top of the highest tick label instead.
        if (data.ValueAxisTitle is { Length: > 0 })
        {
            area = area.Inset(top: LabelSize + 6);
        }

        if (data.CategoryAxisTitle is { Length: > 0 })
        {
            area = area.Inset(bottom: LabelSize + 6);
        }

        if (area.Width <= 10 || area.Height <= 10)
        {
            return;
        }

        DrawGrid(canvas, data, area, minimum, maximum, step, horizontal);
        DrawAxisTitles(canvas, data, plot, area, horizontal);

        switch (data.Type)
        {
            case ChartType.Line or ChartType.LineMarkers or ChartType.Radar:
                DrawLines(canvas, data, area, minimum, maximum, markers: data.Type != ChartType.Line);
                break;

            case ChartType.Area or ChartType.AreaStacked:
                DrawAreas(canvas, data, area, minimum, maximum, stacked);
                break;

            case ChartType.Scatter:
                DrawScatter(canvas, data, area, minimum, maximum);
                break;

            default:
                DrawBars(canvas, data, area, minimum, maximum, stacked, horizontal);
                break;
        }
    }

    private static double MeasureCategories(PdfCanvas canvas, ChartData data)
    {
        var width = 0.0;

        foreach (var category in data.Categories)
        {
            width = Math.Max(width, canvas.MeasureText(category));
        }

        return width;
    }

    private static void DrawGrid(PdfCanvas canvas, ChartData data, Rect area,
        double minimum, double maximum, double step, bool horizontal)
    {
        canvas.SetLineWidth(0.5);
        canvas.SetFont(StandardFont.Helvetica, LabelSize);

        for (var value = minimum; value <= maximum + (step / 2); value += step)
        {
            var position = Project(value, minimum, maximum, area, horizontal);
            var text = Format(value, data.ValueFormat);

            if (data.ShowGridLines)
            {
                canvas.SetStrokeColor(OfficeColor.FromRgb(0xE0, 0xE0, 0xE0));

                if (horizontal)
                {
                    canvas.MoveTo(position, area.Top).LineTo(position, area.Bottom).Stroke();
                }
                else
                {
                    canvas.MoveTo(area.Left, position).LineTo(area.Right, position).Stroke();
                }
            }

            canvas.SetFillColor(OfficeColor.FromRgb(0x59, 0x59, 0x59));

            if (horizontal)
            {
                canvas.DrawText(text, position - (canvas.MeasureText(text) / 2), area.Bottom + LabelSize + 2);
            }
            else
            {
                canvas.DrawText(text, area.Left - canvas.MeasureText(text) - 4, position + (LabelSize / 3));
            }
        }

        canvas.SetStrokeColor(OfficeColor.FromRgb(0x90, 0x90, 0x90));
        canvas.MoveTo(area.Left, area.Bottom).LineTo(area.Right, area.Bottom).Stroke();
        canvas.MoveTo(area.Left, area.Top).LineTo(area.Left, area.Bottom).Stroke();
    }

    private static void DrawAxisTitles(
        PdfCanvas canvas, ChartData data, Rect plot, Rect area, bool horizontal)
    {
        canvas.SetFont(StandardFont.Helvetica, LabelSize + 1);
        canvas.SetFillColor(OfficeColor.FromRgb(0x59, 0x59, 0x59));

        if (data.CategoryAxisTitle is { Length: > 0 } category)
        {
            canvas.DrawText(category, area.Left, plot.Bottom, area.Width, PdfAlignment.Center);
        }

        // Drawn horizontally above the plot rather than rotated up the side: rotated text needs a
        // text matrix per string and buys very little on a slide-sized chart.
        if (data.ValueAxisTitle is { Length: > 0 } value)
        {
            canvas.DrawText(value, horizontal ? area.Left : plot.Left, area.Top - 5);
        }
    }

    private static void DrawBars(PdfCanvas canvas, ChartData data, Rect area,
        double minimum, double maximum, bool stacked, bool horizontal)
    {
        var categories = Math.Max(1, CategoryCount(data));
        var slot = (horizontal ? area.Height : area.Width) / categories;

        // GapWidth is a percentage of the bar width, which is how OOXML expresses it: 150 means the
        // gap is one and a half times a bar.
        var gap = slot * Math.Clamp(data.GapWidth, 0, 500) / (100.0 + data.GapWidth);
        var groupWidth = slot - gap;
        var barWidth = stacked ? groupWidth : groupWidth / data.Series.Count;

        var baseline = Project(Math.Clamp(0, minimum, maximum), minimum, maximum, area, horizontal);

        canvas.SetFont(StandardFont.Helvetica, LabelSize);

        for (var category = 0; category < categories; category++)
        {
            var slotStart = (horizontal ? area.Top : area.Left) + (category * slot) + (gap / 2);
            var positive = 0.0;
            var negative = 0.0;

            for (var s = 0; s < data.Series.Count; s++)
            {
                var value = ValueAt(data.Series[s], category);

                if (double.IsNaN(value))
                {
                    continue;
                }

                double from, to;

                if (stacked)
                {
                    var start = value >= 0 ? positive : negative;
                    var end = start + value;

                    if (value >= 0)
                    {
                        positive = end;
                    }
                    else
                    {
                        negative = end;
                    }

                    from = Project(start, minimum, maximum, area, horizontal);
                    to = Project(end, minimum, maximum, area, horizontal);
                }
                else
                {
                    from = baseline;
                    to = Project(value, minimum, maximum, area, horizontal);
                }

                var offset = stacked ? 0 : s * barWidth;

                canvas.SetFillColor(ColorFor(data, s));

                if (horizontal)
                {
                    var x = Math.Min(from, to);
                    canvas.Rectangle(x, slotStart + offset, Math.Abs(to - from), barWidth).Fill();
                }
                else
                {
                    var y = Math.Min(from, to);
                    canvas.Rectangle(slotStart + offset, y, barWidth, Math.Abs(to - from)).Fill();
                }

                if (data.ShowDataLabels && !stacked)
                {
                    var text = Format(value, data.ValueFormat);
                    canvas.SetFillColor(OfficeColor.FromRgb(0x40, 0x40, 0x40));

                    if (horizontal)
                    {
                        canvas.DrawText(text, Math.Max(from, to) + 3,
                            slotStart + offset + (barWidth / 2) + (LabelSize / 3));
                    }
                    else
                    {
                        canvas.DrawText(text,
                            slotStart + offset + (barWidth / 2) - (canvas.MeasureText(text) / 2),
                            Math.Min(from, to) - 3);
                    }
                }
            }

            DrawCategoryLabel(canvas, data, category, area, slot, horizontal);
        }
    }

    private static void DrawCategoryLabel(
        PdfCanvas canvas, ChartData data, int index, Rect area, double slot, bool horizontal)
    {
        if (index >= data.Categories.Count)
        {
            return;
        }

        var label = data.Categories[index];
        canvas.SetFont(StandardFont.Helvetica, LabelSize);
        canvas.SetFillColor(OfficeColor.FromRgb(0x59, 0x59, 0x59));

        if (horizontal)
        {
            canvas.DrawText(label,
                area.Left - canvas.MeasureText(label) - 4,
                area.Top + (index * slot) + (slot / 2) + (LabelSize / 3));
        }
        else
        {
            var centre = area.Left + (index * slot) + (slot / 2);
            canvas.DrawText(label, centre - (canvas.MeasureText(label) / 2), area.Bottom + LabelSize + 2);
        }
    }

    private static void DrawLines(PdfCanvas canvas, ChartData data, Rect area,
        double minimum, double maximum, bool markers)
    {
        var categories = Math.Max(1, CategoryCount(data));
        var slot = area.Width / categories;

        canvas.SetLineWidth(1.6);

        for (var s = 0; s < data.Series.Count; s++)
        {
            canvas.SetStrokeColor(ColorFor(data, s));
            var started = false;

            for (var category = 0; category < categories; category++)
            {
                var value = ValueAt(data.Series[s], category);

                if (double.IsNaN(value))
                {
                    // dispBlanksAs="gap": break the line rather than joining across the hole.
                    if (started)
                    {
                        canvas.Stroke();
                        started = false;
                    }

                    continue;
                }

                var x = area.Left + (category * slot) + (slot / 2);
                var y = Project(value, minimum, maximum, area, horizontal: false);

                if (started)
                {
                    canvas.LineTo(x, y);
                }
                else
                {
                    canvas.MoveTo(x, y);
                    started = true;
                }
            }

            if (started)
            {
                canvas.Stroke();
            }

            if (markers)
            {
                DrawMarkers(canvas, data, s, area, minimum, maximum, slot, categories);
            }
        }

        for (var category = 0; category < categories; category++)
        {
            DrawCategoryLabel(canvas, data, category, area, slot, horizontal: false);
        }
    }

    private static void DrawMarkers(PdfCanvas canvas, ChartData data, int series, Rect area,
        double minimum, double maximum, double slot, int categories)
    {
        canvas.SetFillColor(ColorFor(data, series));

        for (var category = 0; category < categories; category++)
        {
            var value = ValueAt(data.Series[series], category);

            if (double.IsNaN(value))
            {
                continue;
            }

            var x = area.Left + (category * slot) + (slot / 2);
            var y = Project(value, minimum, maximum, area, horizontal: false);

            canvas.Rectangle(x - 2.5, y - 2.5, 5, 5).Fill();
        }
    }

    private static void DrawAreas(PdfCanvas canvas, ChartData data, Rect area,
        double minimum, double maximum, bool stacked)
    {
        var categories = Math.Max(1, CategoryCount(data));
        var slot = area.Width / categories;
        var running = new double[categories];

        for (var s = 0; s < data.Series.Count; s++)
        {
            var points = new List<(double X, double Y)>();
            var baseline = new List<(double X, double Y)>();

            for (var category = 0; category < categories; category++)
            {
                var value = ValueAt(data.Series[s], category);
                value = double.IsNaN(value) ? 0 : value;

                var start = stacked ? running[category] : 0;
                var end = start + value;

                if (stacked)
                {
                    running[category] = end;
                }

                var x = area.Left + (category * slot) + (slot / 2);
                points.Add((x, Project(end, minimum, maximum, area, horizontal: false)));
                baseline.Add((x, Project(start, minimum, maximum, area, horizontal: false)));
            }

            if (points.Count < 2)
            {
                continue;
            }

            canvas.SetFillColor(ColorFor(data, s));

            canvas.MoveTo(points[0].X, points[0].Y);

            for (var i = 1; i < points.Count; i++)
            {
                canvas.LineTo(points[i].X, points[i].Y);
            }

            for (var i = baseline.Count - 1; i >= 0; i--)
            {
                canvas.LineTo(baseline[i].X, baseline[i].Y);
            }

            canvas.ClosePath().Fill();
        }

        for (var category = 0; category < categories; category++)
        {
            DrawCategoryLabel(canvas, data, category, area, slot, horizontal: false);
        }
    }

    private static void DrawScatter(
        PdfCanvas canvas, ChartData data, Rect area, double minimum, double maximum)
    {
        var xMinimum = double.MaxValue;
        var xMaximum = double.MinValue;

        foreach (var series in data.Series)
        {
            foreach (var x in series.XValues ?? [])
            {
                if (double.IsNaN(x))
                {
                    continue;
                }

                xMinimum = Math.Min(xMinimum, x);
                xMaximum = Math.Max(xMaximum, x);
            }
        }

        if (xMinimum > xMaximum)
        {
            xMinimum = 0;
            xMaximum = 1;
        }

        if (Math.Abs(xMaximum - xMinimum) < double.Epsilon)
        {
            xMaximum = xMinimum + 1;
        }

        for (var s = 0; s < data.Series.Count; s++)
        {
            var series = data.Series[s];
            canvas.SetFillColor(ColorFor(data, s));

            for (var i = 0; i < series.Values.Count; i++)
            {
                var y = series.Values[i];
                var x = series.XValues is { } xs && i < xs.Count ? xs[i] : i;

                if (double.IsNaN(x) || double.IsNaN(y))
                {
                    continue;
                }

                var px = area.Left + ((x - xMinimum) / (xMaximum - xMinimum) * area.Width);
                var py = Project(y, minimum, maximum, area, horizontal: false);

                canvas.Rectangle(px - 2, py - 2, 4, 4).Fill();
            }
        }
    }

    // ---- Scale ---------------------------------------------------------------------------------

    private static (double Minimum, double Maximum, double Step) ChooseScale(
        ChartData data, bool stacked)
    {
        var minimum = 0.0;
        var maximum = 0.0;

        if (stacked)
        {
            var categories = CategoryCount(data);

            for (var category = 0; category < categories; category++)
            {
                var positive = 0.0;
                var negative = 0.0;

                foreach (var series in data.Series)
                {
                    var value = ValueAt(series, category);

                    if (double.IsNaN(value))
                    {
                        continue;
                    }

                    if (value >= 0)
                    {
                        positive += value;
                    }
                    else
                    {
                        negative += value;
                    }
                }

                maximum = Math.Max(maximum, positive);
                minimum = Math.Min(minimum, negative);
            }
        }
        else
        {
            foreach (var series in data.Series)
            {
                foreach (var value in series.Values)
                {
                    if (double.IsNaN(value))
                    {
                        continue;
                    }

                    maximum = Math.Max(maximum, value);
                    minimum = Math.Min(minimum, value);
                }
            }
        }

        if (Math.Abs(maximum - minimum) < double.Epsilon)
        {
            maximum = minimum + 1;
        }

        // A "nice" step: 1, 2, 2.5 or 5 times a power of ten, whichever gives roughly five
        // gridlines. Ticks at 1.7 or 3.3 are technically fine and look like a bug.
        var rough = (maximum - minimum) / 5;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(rough)));
        var normalised = rough / magnitude;

        var step = magnitude * normalised switch
        {
            <= 1 => 1,
            <= 2 => 2,
            <= 2.5 => 2.5,
            <= 5 => 5,
            _ => 10,
        };

        return (Math.Floor(minimum / step) * step, Math.Ceiling(maximum / step) * step, step);
    }

    private static double Project(
        double value, double minimum, double maximum, Rect area, bool horizontal)
    {
        var fraction = (value - minimum) / (maximum - minimum);

        return horizontal
            ? area.Left + (fraction * area.Width)
            : area.Bottom - (fraction * area.Height);
    }

    private static int CategoryCount(ChartData data) =>
        Math.Max(data.Categories.Count, data.Series.Max(s => s.Values.Count));

    private static double ValueAt(ChartSeries series, int index) =>
        index < series.Values.Count ? series.Values[index] : double.NaN;

    private static OfficeColor ColorFor(ChartData data, int index) =>
        index < data.Series.Count && data.Series[index].Color is { } color
            ? color
            : ChartData.DefaultPalette[index % ChartData.DefaultPalette.Length];

    private static string Format(double value, string format)
    {
        if (double.IsNaN(value))
        {
            return string.Empty;
        }

        // Only the number formats a chart axis actually uses are honoured; anything else falls back
        // to a general-purpose rendering rather than half-applying a format string.
        return format switch
        {
            "General" or "" => value.ToString("0.##", CultureInfo.InvariantCulture),
            "0%" => value.ToString("0%", CultureInfo.InvariantCulture),
            "0.0%" => value.ToString("0.0%", CultureInfo.InvariantCulture),
            _ when format.Contains('#') || format.Contains('0') =>
                TryFormat(value, format),
            _ => value.ToString("0.##", CultureInfo.InvariantCulture),
        };
    }

    private static string TryFormat(double value, string format)
    {
        try
        {
            return value.ToString(format, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>A rectangle in top-down PDF canvas coordinates.</summary>
    private readonly record struct Rect(double Left, double Top, double Width, double Height)
    {
        public double Right => Left + Width;

        public double Bottom => Top + Height;

        public Rect Inset(double left = 0, double top = 0, double right = 0, double bottom = 0) =>
            new(Left + left, Top + top, Width - left - right, Height - top - bottom);
    }
}

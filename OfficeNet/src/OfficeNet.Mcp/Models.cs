// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.ComponentModel;

namespace OfficeNet.Mcp;

// The shapes a client sends. They are deliberately flat and forgiving: a language model fills them
// in from a description, so every member is optional unless the block cannot mean anything without
// it, and a type is a plain string rather than an enum whose spelling the model has to guess.

/// <summary>One block of a Word document.</summary>
public sealed record DocBlock
{
    [Description("heading | paragraph | bullets | numbered | table | image | pagebreak | toc | quote")]
    public string Type { get; init; } = "paragraph";

    [Description("The text of a heading, paragraph or quote. **bold** and *italic* are honoured.")]
    public string? Text { get; init; }

    [Description("Heading level, 0 (title) to 9. Defaults to 1.")]
    public int? Level { get; init; }

    [Description("The items of a bullets or numbered list.")]
    public string[]? Items { get; init; }

    [Description("Table rows; the first is the header row unless header is false.")]
    public string[][]? Rows { get; init; }

    [Description("Whether the first table row is a header. Defaults to true.")]
    public bool? Header { get; init; }

    [Description("Workspace path of an image (PNG, JPEG, GIF, BMP).")]
    public string? ImagePath { get; init; }

    [Description("Image width in centimetres; the height follows the aspect ratio.")]
    public double? WidthCm { get; init; }

    [Description("left | center | right | justify")]
    public string? Align { get; init; }
}

/// <summary>A chart for a slide or a sheet.</summary>
public sealed record ChartSpec
{
    [Description("column | columnStacked | bar | barStacked | line | lineMarkers | area | areaStacked | pie | doughnut | scatter | radar")]
    public string Type { get; init; } = "column";

    public string? Title { get; init; }

    [Description("Category labels, one per point.")]
    public string[] Categories { get; init; } = [];

    [Description("The series, each with a name and one value per category.")]
    public SeriesSpec[] Series { get; init; } = [];

    [Description("Number format for labels and the axis, e.g. #,##0 or 0%.")]
    public string? ValueFormat { get; init; }

    public bool? ShowDataLabels { get; init; }
}

/// <summary>One series of a chart.</summary>
public sealed record SeriesSpec
{
    public string Name { get; init; } = "Series";

    public double[] Values { get; init; } = [];
}

/// <summary>One worksheet.</summary>
public sealed record SheetSpec
{
    public string Name { get; init; } = "Sheet1";

    [Description(
        "Rows of cell values starting at A1. Numbers stay numbers, true/false are booleans, " +
        "ISO dates (2026-10-28) become dates, and a string starting with = is a formula.")]
    public object?[][] Rows { get; init; } = [];

    [Description("Make the first row a bold, frozen header. Defaults to true.")]
    public bool? Header { get; init; }

    [Description("Number formats by column letter, e.g. {\"C\": \"#,##0\", \"D\": \"0.0%\"}.")]
    public Dictionary<string, string>? NumberFormats { get; init; }

    [Description("A chart placed to the right of the data.")]
    public ChartSpec? Chart { get; init; }
}

/// <summary>One slide.</summary>
public sealed record SlideSpec
{
    [Description("title | bullets | section | table | chart | image | blank")]
    public string Type { get; init; } = "bullets";

    public string? Title { get; init; }

    [Description("Subtitle of a title or section slide.")]
    public string? Subtitle { get; init; }

    public string[]? Bullets { get; init; }

    [Description("Table rows; the first is the header row.")]
    public string[][]? Rows { get; init; }

    public ChartSpec? Chart { get; init; }

    [Description("Workspace path of an image for an image slide.")]
    public string? ImagePath { get; init; }

    [Description("Speaker notes.")]
    public string? Notes { get; init; }
}

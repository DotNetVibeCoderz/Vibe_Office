using System.Text.Json;
using System.Text.Json.Serialization;

namespace VibeDesk.Application.Documents;

/// <summary>
/// Serialisation settings shared by every content payload. camelCase keeps the JSON small and matches
/// what the JavaScript editor interop expects, and enum-as-string keeps stored data readable.
/// </summary>
public static class ContentJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        WriteIndented = false,
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    /// <summary>Returns a fresh default instance rather than null when the payload is missing/corrupt.</summary>
    public static T Deserialize<T>(string? json) where T : new()
    {
        if (string.IsNullOrWhiteSpace(json)) return new T();
        try
        {
            return JsonSerializer.Deserialize<T>(json, Options) ?? new T();
        }
        catch (JsonException)
        {
            return new T();
        }
    }
}

// ─────────────────────────────────────────── Docs ───────────────────────────────────────────

/// <summary>
/// A text document. The body is HTML because the editor is a <c>contenteditable</c> surface — storing
/// HTML means no lossy round-trip through an intermediate AST on every keystroke.
/// </summary>
public sealed class DocumentModel
{
    public string Html { get; set; } = "<p></p>";
    public int WordCount { get; set; }
    public PageSetup Page { get; set; } = new();

    /// <summary>Accepted suggestion ids, so an accepted suggestion is never re-applied.</summary>
    public List<string> AppliedSuggestions { get; set; } = [];
}

public sealed class PageSetup
{
    public string Size { get; set; } = "a4";
    public string Orientation { get; set; } = "portrait";
    public double MarginCm { get; set; } = 2.5;
    public string FontFamily { get; set; } = "Inter";
    public double FontSizePt { get; set; } = 11;
}

// ────────────────────────────────────────── Sheets ──────────────────────────────────────────

public sealed class SpreadsheetModel
{
    public List<SheetTab> Sheets { get; set; } = [new()];

    /// <summary>Style table referenced by <see cref="Cell.S"/>, deduplicated across all sheets.</summary>
    public Dictionary<string, CellStyle> Styles { get; set; } = [];

    /// <summary>Named ranges usable in formulas, e.g. <c>Revenue → Sheet1!B2:B13</c>.</summary>
    public Dictionary<string, string> NamedRanges { get; set; } = [];
}

public sealed class SheetTab
{
    public string Id { get; set; } = "s1";
    public string Name { get; set; } = "Sheet1";
    public int RowCount { get; set; } = 200;
    public int ColCount { get; set; } = 26;

    /// <summary>
    /// Sparse cell map keyed by A1 address. Sparse rather than a 2-D array because a typical sheet is
    /// mostly empty and we only want to pay for what the user actually filled in.
    /// </summary>
    public Dictionary<string, Cell> Cells { get; set; } = [];

    public Dictionary<string, double> ColWidths { get; set; } = [];
    public Dictionary<string, double> RowHeights { get; set; } = [];

    public int FrozenRows { get; set; }
    public int FrozenCols { get; set; }

    public List<ConditionalFormatRule> ConditionalFormats { get; set; } = [];
    public List<ChartSpec> Charts { get; set; } = [];
    public List<PivotSpec> Pivots { get; set; } = [];
    public string? TabColor { get; set; }
    public bool Hidden { get; set; }
}

/// <summary>
/// A populated cell. Short property names are intentional — they are repeated thousands of times per
/// payload, and the difference between <c>"v"</c> and <c>"value"</c> is a measurable share of the JSON.
/// </summary>
public sealed class Cell
{
    /// <summary>Literal value when <see cref="F"/> is null, otherwise the cached formula result.</summary>
    public string? V { get; set; }

    /// <summary>Formula source including the leading <c>=</c>.</summary>
    public string? F { get; set; }

    /// <summary>Key into <see cref="SpreadsheetModel.Styles"/>.</summary>
    public string? S { get; set; }

    /// <summary>Excel-style number format, e.g. <c>#,##0.00</c> or <c>0%</c>.</summary>
    public string? Fmt { get; set; }

    /// <summary>Note shown on hover; distinct from threaded comments.</summary>
    public string? N { get; set; }
}

public sealed class CellStyle
{
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Underline { get; set; }
    public bool Strike { get; set; }
    public string? Color { get; set; }
    public string? Background { get; set; }
    public string? FontFamily { get; set; }
    public double? FontSize { get; set; }
    /// <summary>left | center | right</summary>
    public string? Align { get; set; }
    /// <summary>top | middle | bottom</summary>
    public string? VAlign { get; set; }
    public bool Wrap { get; set; }
    public string? Border { get; set; }
}

public sealed class ConditionalFormatRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    /// <summary>A1 range the rule applies to, e.g. <c>B2:B20</c>.</summary>
    public string Range { get; set; } = string.Empty;

    /// <summary>greaterThan | lessThan | between | equal | notEqual | contains | isEmpty | notEmpty | formula | colorScale</summary>
    public string Op { get; set; } = "greaterThan";

    public string? Value1 { get; set; }
    public string? Value2 { get; set; }

    public CellStyle Style { get; set; } = new();

    /// <summary>For <c>colorScale</c>: the two/three stop colours, low → high.</summary>
    public List<string>? ScaleColors { get; set; }
}

public sealed class ChartSpec
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Title { get; set; } = "Chart";
    /// <summary>bar | column | line | area | pie | doughnut | scatter</summary>
    public string Kind { get; set; } = "column";

    /// <summary>Range supplying category labels, e.g. <c>A2:A13</c>.</summary>
    public string? CategoryRange { get; set; }
    /// <summary>One range per series, e.g. <c>["B2:B13","C2:C13"]</c>.</summary>
    public List<string> SeriesRanges { get; set; } = [];
    public List<string> SeriesNames { get; set; } = [];

    public bool ShowLegend { get; set; } = true;
    public bool ShowGrid { get; set; } = true;
    public bool Stacked { get; set; }

    /// <summary>Anchor + size in grid units, for positioning the floating chart card.</summary>
    public double X { get; set; } = 40;
    public double Y { get; set; } = 40;
    public double W { get; set; } = 480;
    public double H { get; set; } = 300;
}

public sealed class PivotSpec
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Title { get; set; } = "Pivot table";

    /// <summary>Source range including the header row, e.g. <c>A1:E200</c>.</summary>
    public string SourceRange { get; set; } = string.Empty;

    /// <summary>Header names grouped down the rows.</summary>
    public List<string> Rows { get; set; } = [];
    /// <summary>Header names spread across the columns.</summary>
    public List<string> Columns { get; set; } = [];
    public List<PivotValue> Values { get; set; } = [];

    /// <summary>Optional <c>header=value</c> filters applied before aggregation.</summary>
    public Dictionary<string, string> Filters { get; set; } = [];
}

public sealed class PivotValue
{
    public string Field { get; set; } = string.Empty;
    /// <summary>sum | count | average | min | max</summary>
    public string Aggregate { get; set; } = "sum";
    public string? Label { get; set; }
}

// ────────────────────────────────────────── Slides ──────────────────────────────────────────

public sealed class PresentationModel
{
    /// <summary>Key into <see cref="SlideThemes"/>.</summary>
    public string Theme { get; set; } = "aurora";
    public List<Slide> Slides { get; set; } = [new()];
    public double AspectRatio { get; set; } = 16d / 9d;
}

public sealed class Slide
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    /// <summary>title | titleContent | twoColumn | sectionHeader | blank | imageFull | quote</summary>
    public string Layout { get; set; } = "title";

    public List<SlideElement> Elements { get; set; } = [];

    /// <summary>Speaker notes, shown only in presenter view.</summary>
    public string? Notes { get; set; }

    /// <summary>none | fade | slideLeft | slideUp | zoom | flip</summary>
    public string Transition { get; set; } = "fade";
    public int TransitionMs { get; set; } = 400;

    /// <summary>Overrides the theme background for this slide only.</summary>
    public string? Background { get; set; }
    public bool Hidden { get; set; }
}

/// <summary>
/// One element on a slide. Geometry is in percent of the canvas so a deck renders identically at any
/// zoom level and in presenter/thumbnail views without recomputing layout.
/// </summary>
public sealed class SlideElement
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>text | image | video | audio | shape | chart | table | embed</summary>
    public string Type { get; set; } = "text";

    public double X { get; set; } = 10;
    public double Y { get; set; } = 10;
    public double W { get; set; } = 80;
    public double H { get; set; } = 20;
    public double Rotation { get; set; }
    public int Z { get; set; }

    /// <summary>Inline HTML for <c>text</c> elements.</summary>
    public string? Text { get; set; }

    /// <summary>Media source for image/video/audio/embed elements.</summary>
    public string? Src { get; set; }
    public string? Alt { get; set; }

    /// <summary>For <c>chart</c>: the Drive item and chart id to render live from Sheets.</summary>
    public Guid? SourceSpreadsheetId { get; set; }
    public string? SourceChartId { get; set; }

    public SlideElementStyle Style { get; set; } = new();
    public SlideAnimation? Animation { get; set; }
}

public sealed class SlideElementStyle
{
    public string? Color { get; set; }
    public string? Background { get; set; }
    public string? FontFamily { get; set; }
    public double? FontSize { get; set; }
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    /// <summary>left | center | right</summary>
    public string? Align { get; set; }
    public double? BorderRadius { get; set; }
    public string? Border { get; set; }
    public double? Opacity { get; set; }
    public string? Shadow { get; set; }
    /// <summary>For <c>shape</c>: rect | ellipse | triangle | arrow | line.</summary>
    public string? Shape { get; set; }
}

public sealed class SlideAnimation
{
    /// <summary>none | fadeIn | fadeInUp | slideInLeft | slideInRight | zoomIn | bounceIn | typewriter</summary>
    public string Type { get; set; } = "fadeIn";
    public int DelayMs { get; set; }
    public int DurationMs { get; set; } = 500;
    /// <summary>Order within the slide's click-through sequence; 0 means "with the slide".</summary>
    public int Order { get; set; }
}

/// <summary>Built-in deck themes. Kept in code so a deck is portable without a theme table.</summary>
public static class SlideThemes
{
    public sealed record Theme(
        string Key,
        string Name,
        string Background,
        string TitleColor,
        string BodyColor,
        string AccentColor,
        string FontFamily);

    public static readonly IReadOnlyList<Theme> All =
    [
        new("aurora", "Aurora", "linear-gradient(135deg,#1e1b4b 0%,#4c1d95 50%,#831843 100%)",
            "#ffffff", "#e9d5ff", "#f0abfc", "Inter"),
        new("paper", "Paper", "#fdfcf8", "#1c1917", "#44403c", "#b45309", "Lora"),
        new("midnight", "Midnight", "#0b1120", "#f8fafc", "#94a3b8", "#38bdf8", "Inter"),
        new("mint", "Mint", "linear-gradient(160deg,#ecfdf5 0%,#d1fae5 100%)",
            "#064e3b", "#065f46", "#0d9488", "Inter"),
        new("sunrise", "Sunrise", "linear-gradient(135deg,#fff7ed 0%,#fed7aa 100%)",
            "#7c2d12", "#9a3412", "#ea580c", "Inter"),
        new("mono", "Mono", "#ffffff", "#111827", "#374151", "#111827", "JetBrains Mono"),
    ];

    public static Theme Get(string? key) =>
        All.FirstOrDefault(t => t.Key == key) ?? All[0];
}

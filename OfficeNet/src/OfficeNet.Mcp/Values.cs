// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using OfficeNet.Core.Charts;
using WordNet;

namespace OfficeNet.Mcp;

/// <summary>Conversions between what a client sends and what the libraries take.</summary>
internal static partial class Values
{
    internal static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    internal static string ToJson(object value) => JsonSerializer.Serialize(value, Json);

    /// <summary>
    /// Unwraps a <see cref="JsonElement"/> into plain .NET values — strings, doubles, booleans,
    /// dictionaries and lists — which is what the template engine and the cell writer understand.
    /// </summary>
    internal static object? Plain(object? value) => value switch
    {
        JsonElement element => Plain(element),
        _ => value,
    };

    internal static object? Plain(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var whole) ? whole : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Object => element.EnumerateObject()
            .ToDictionary(p => p.Name, p => Plain(p.Value), StringComparer.OrdinalIgnoreCase),
        JsonValueKind.Array => element.EnumerateArray().Select(Plain).ToList(),
        _ => null,
    };

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}([T ]\d{2}:\d{2}(:\d{2})?)?$")]
    private static partial Regex IsoDate();

    /// <summary>
    /// A cell value from JSON. An ISO date string becomes a date, because a model writing a date
    /// into a spreadsheet means a date, and a date stored as text cannot be sorted or summed.
    /// </summary>
    internal static object? Cell(object? value)
    {
        var plain = Plain(value);

        if (plain is string text && IsoDate().IsMatch(text) &&
            DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        return plain switch
        {
            long whole => (double)whole,
            _ => plain,
        };
    }

    /// <summary>
    /// <see cref="Plain(JsonElement)"/>, with every ISO date string turned into a
    /// <see cref="DateTime"/>.
    /// </summary>
    /// <remarks>
    /// JSON has no date type, so a model sends <c>"2026-10-28"</c> as text — and text cannot be
    /// formatted, so <c>{{tanggal:dd MMMM yyyy}}</c> printed the ISO string unchanged. Found by
    /// looking at the invoice the screenshot tool produced, not by a test.
    /// </remarks>
    internal static object? TemplateData(JsonElement element) => Dates(Plain(element));

    private static object? Dates(object? value) => value switch
    {
        string text when IsoDate().IsMatch(text) &&
                         DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) => date,
        Dictionary<string, object?> map => map.ToDictionary(p => p.Key, p => Dates(p.Value), StringComparer.OrdinalIgnoreCase),
        List<object?> list => list.Select(Dates).ToList(),
        _ => value,
    };

    internal static ChartData Chart(ChartSpec spec)
    {
        if (!Enum.TryParse<ChartType>(spec.Type, ignoreCase: true, out var type))
        {
            throw new ArgumentException(
                $"Unknown chart type '{spec.Type}'. Use one of: " +
                string.Join(", ", Enum.GetNames<ChartType>().Select(n => char.ToLowerInvariant(n[0]) + n[1..])) + ".");
        }

        if (spec.Series.Length == 0)
        {
            throw new ArgumentException("A chart needs at least one series.");
        }

        foreach (var series in spec.Series)
        {
            if (series.Values.Length != spec.Categories.Length && spec.Categories.Length > 0)
            {
                throw new ArgumentException(
                    $"Series '{series.Name}' has {series.Values.Length} values for {spec.Categories.Length} categories.");
            }
        }

        return new ChartData
        {
            Type = type,
            Title = spec.Title,
            Categories = spec.Categories,
            Series = [.. spec.Series.Select(s => new ChartSeries(s.Name, s.Values))],
            ValueFormat = spec.ValueFormat ?? "General",
            ShowDataLabels = spec.ShowDataLabels ?? false,
        };
    }

    internal static ParagraphAlignment? Alignment(string? align) => align?.ToLowerInvariant() switch
    {
        null or "" => null,
        "left" => ParagraphAlignment.Left,
        "center" or "centre" => ParagraphAlignment.Center,
        "right" => ParagraphAlignment.Right,
        "justify" or "justified" => ParagraphAlignment.Justify,
        _ => throw new ArgumentException($"Unknown alignment '{align}'. Use left, center, right or justify."),
    };

    [GeneratedRegex(@"\*\*(?<b>.+?)\*\*|\*(?<i>[^*]+?)\*")]
    private static partial Regex Emphasis();

    /// <summary>
    /// Writes text into a paragraph, honouring <c>**bold**</c> and <c>*italic*</c>. Models write
    /// emphasis that way whether asked to or not; showing the asterisks would be worse than
    /// understanding the two forms everyone uses.
    /// </summary>
    internal static void AddInline(Paragraph paragraph, string text)
    {
        var position = 0;

        foreach (Match match in Emphasis().Matches(text))
        {
            if (match.Index > position)
            {
                paragraph.AddRun(text[position..match.Index]);
            }

            if (match.Groups["b"].Success)
            {
                paragraph.AddRun(match.Groups["b"].Value, bold: true);
            }
            else
            {
                paragraph.AddRun(match.Groups["i"].Value, italic: true);
            }

            position = match.Index + match.Length;
        }

        if (position < text.Length)
        {
            paragraph.AddRun(text[position..]);
        }
    }
}

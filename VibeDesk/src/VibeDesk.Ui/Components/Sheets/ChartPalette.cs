namespace VibeDesk.Ui.Components.Sheets;

/// <summary>
/// Categorical series colours for charts.
/// </summary>
/// <remarks>
/// These are the validated reference values, not the app's brand palette. The brand's wedelan teal
/// cannot reach the chroma floor at the lightness a categorical slot needs in sRGB, and every
/// brand-derived set tried failed either the chroma floor or CVD separation. Data legibility outranks
/// palette continuity, so the chrome keeps the brand colours and the data uses these.
///
/// Verified with the palette validator in both modes: lightness band, chroma floor, CVD separation
/// (worst adjacent ΔE 9.1 protan light / 8.4 dark) and normal-vision floor all pass. Three light-mode
/// slots sit below 3:1 against the chart surface, which obliges visible relief — hence the legend and
/// the table view on every chart card, both always present.
///
/// Slots are assigned in fixed order and never cycled: a ninth series folds into the last slot rather
/// than reusing slot 1, because repainting on series count would break identity across a filter change.
/// </remarks>
public static class ChartPalette
{
    private static readonly string[] Light =
    [
        "#2a78d6", // blue
        "#eb6834", // orange
        "#1baf7a", // aqua
        "#eda100", // yellow
        "#e87ba4", // magenta
        "#008300", // green
        "#4a3aa7", // violet
        "#e34948", // red
    ];

    private static readonly string[] Dark =
    [
        "#3987e5",
        "#d95926",
        "#199e70",
        "#c98500",
        "#d55181",
        "#008300",
        "#9085e9",
        "#e66767",
    ];

    public static int SlotCount => Light.Length;

    /// <summary>
    /// Colour for a series index. Uses a CSS custom property so the same markup serves both themes —
    /// the dark steps are chosen, not an automatic flip of the light ones.
    /// </summary>
    public static string Token(int index)
    {
        var slot = Math.Clamp(index, 0, Light.Length - 1) + 1;
        return $"var(--vd-series-{slot})";
    }

    /// <summary>Emits the custom-property declarations for both themes.</summary>
    public static string ToCssVariables()
    {
        var light = string.Join("\n  ", Light.Select((c, i) => $"--vd-series-{i + 1}: {c};"));
        var dark = string.Join("\n    ", Dark.Select((c, i) => $"--vd-series-{i + 1}: {c};"));

        return $$"""
        :root {
          {{light}}
        }

        @media (prefers-color-scheme: dark) {
          :root:not([data-theme="light"]) {
            {{dark}}
          }
        }

        :root[data-theme="dark"] {
          {{dark.Replace("\n    ", "\n  ")}}
        }
        """;
    }
}

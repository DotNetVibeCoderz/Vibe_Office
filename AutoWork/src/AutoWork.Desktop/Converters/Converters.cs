using System.Globalization;
using AutoWork.Core.Agents;
using AutoWork.Core.Logging;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace AutoWork.Desktop.Converters;

/// <summary>
/// Maps a subsystem to its hue. This is the one place the Brain/Eyes/Hands colour coding is
/// defined, so the tape, the organ rail and the activity log can never disagree about which
/// colour means which faculty.
/// </summary>
public sealed class OrganBrushConverter : IValueConverter
{
    public static readonly OrganBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            AgentOrgan organ => organ switch
            {
                AgentOrgan.Brain => "Think",
                AgentOrgan.Eyes => "See",
                _ => "Act",
            },
            string text => text switch
            {
                "Brain" => "Think",
                "Eyes" => "See",
                "Hands" => "Act",
                _ => "Think",
            },
            _ => "Think",
        };

        return Lookup(key);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    /// <summary>
    /// Resolves against the theme in force right now. Callers that must survive a theme switch
    /// re-run this on <c>ActualThemeVariantChanged</c> rather than caching the result.
    /// </summary>
    internal static IBrush Lookup(string key)
    {
        var application = Application.Current;
        if (application is null) return Brushes.Gray;

        return application.TryGetResource(key, application.ActualThemeVariant, out var resource)
               && resource is IBrush brush
            ? brush
            : Brushes.Gray;
    }
}

/// <summary>Green for done, red for failed, the acting organ's hue while running.</summary>
public sealed class StatusBrushConverter : IValueConverter
{
    public static readonly StatusBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var status = value as StepStatus? ?? (value is string text && Enum.TryParse<StepStatus>(text, out var parsed)
            ? parsed
            : StepStatus.Pending);

        return OrganBrushConverter.Lookup(status switch
        {
            StepStatus.Succeeded => "Success",
            StepStatus.Failed => "Danger",
            StepStatus.Cancelled or StepStatus.Skipped => "TextFaint",
            _ => "Think",
        });
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class OutcomeBrushConverter : IValueConverter
{
    public static readonly OutcomeBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        OrganBrushConverter.Lookup(value switch
        {
            ActionOutcome.Succeeded => "Success",
            ActionOutcome.Failed => "Danger",
            ActionOutcome.Denied => "Warning",
            _ => "TextFaint",
        });

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>True when the bound value equals the parameter. Used by the segmented pickers.</summary>
public sealed class EqualsConverter : IValueConverter
{
    public static readonly EqualsConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Equals(value?.ToString(), parameter?.ToString());

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true && parameter is not null
            ? parameter
            : Avalonia.Data.BindingOperations.DoNothing;
}

/// <summary>Formats a timestamp for the activity table without dragging in a culture dependency.</summary>
public sealed class TimeConverter : IValueConverter
{
    public static readonly TimeConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DateTimeOffset at
            ? at.Date == DateTimeOffset.Now.Date ? at.ToString("HH:mm:ss") : at.ToString("dd MMM HH:mm")
            : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class JoinConverter : IValueConverter
{
    public static readonly JoinConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is IEnumerable<string> items ? string.Join(", ", items) : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

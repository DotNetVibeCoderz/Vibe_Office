using AutoWork.Desktop.Converters;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AutoWork.Desktop.Views;

/// <summary>
/// One segment of the organ rail: a hairline bar plus a label, lit in its subsystem's hue when
/// that faculty is working and dimmed to a neutral otherwise.
///
/// Composed in code rather than XAML because it is three elements and two states — a template
/// plus a converter plus a style would be more machinery than the thing itself.
/// </summary>
public sealed class OrganLamp : UserControl
{
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<OrganLamp, string>(nameof(Label), "");

    /// <summary>Resource key of the hue: "Think", "See" or "Act".</summary>
    public static readonly StyledProperty<string> HueProperty =
        AvaloniaProperty.Register<OrganLamp, string>(nameof(Hue), "Think");

    public static readonly StyledProperty<bool> IsLitProperty =
        AvaloniaProperty.Register<OrganLamp, bool>(nameof(IsLit));

    private readonly Border _bar;
    private readonly TextBlock _label;

    public OrganLamp()
    {
        _bar = new Border
        {
            Width = 22,
            Height = 3,
            CornerRadius = new CornerRadius(2),
            VerticalAlignment = VerticalAlignment.Center,
        };

        _label = new TextBlock
        {
            FontSize = 11,
            LetterSpacing = 1.2,
            VerticalAlignment = VerticalAlignment.Center,
        };

        Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { _bar, _label },
        };

        // Brushes are resolved imperatively, so a theme switch has to trigger a repaint.
        ActualThemeVariantChanged += (_, _) => Refresh();

        Refresh();
    }

    public string Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string Hue
    {
        get => GetValue(HueProperty);
        set => SetValue(HueProperty, value);
    }

    public bool IsLit
    {
        get => GetValue(IsLitProperty);
        set => SetValue(IsLitProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == LabelProperty || change.Property == HueProperty || change.Property == IsLitProperty)
            Refresh();
    }

    private void Refresh()
    {
        _label.Text = Label?.ToUpperInvariant() ?? "";

        var hue = OrganBrushConverter.Lookup(Hue);
        var muted = OrganBrushConverter.Lookup("TextFaint");

        _bar.Background = IsLit ? hue : muted;
        _bar.Opacity = IsLit ? 1.0 : 0.35;

        _label.Foreground = IsLit ? hue : muted;
        _label.FontWeight = IsLit ? FontWeight.SemiBold : FontWeight.Normal;
    }
}

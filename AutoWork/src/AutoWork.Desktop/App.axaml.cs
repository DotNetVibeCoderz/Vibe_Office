using AutoWork.Core.Configuration;
using AutoWork.Desktop.Services;
using AutoWork.Desktop.ViewModels;
using AutoWork.Desktop.Views;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace AutoWork.Desktop;

public partial class App : Application
{
    private AppServices? _services;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _services = new AppServices();

            ApplyTheme(_services.Config.Current.Appearance.Theme);
            _services.Config.Changed += config => ApplyTheme(config.Appearance.Theme);

            desktop.MainWindow = new MainWindow { DataContext = new MainWindowViewModel(_services) };
            desktop.ShutdownRequested += (_, _) => _services.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ApplyTheme(ThemeMode mode) =>
        RequestedThemeVariant = mode switch
        {
            ThemeMode.Light => ThemeVariant.Light,
            ThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
}

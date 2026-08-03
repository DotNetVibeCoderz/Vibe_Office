using Avalonia;
using Avalonia.Media;

namespace AutoWork.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // Inter is bundled rather than looked up on the system, so the interface renders
            // identically on a stock Windows, macOS and Linux machine.
            .WithInterFont()
            .With(new FontManagerOptions
            {
                DefaultFamilyName = "avares://Avalonia.Fonts.Inter/Assets#Inter",
            })
            .LogToTrace();
}

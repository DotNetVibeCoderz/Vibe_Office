using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Photino.Blazor;
using VibeDesk.Client;
using VibeDesk.Client.Components;

namespace VibeDesk.Desktop;

/// <summary>
/// The cross-platform desktop shell: one native window hosting the same Blazor components the web
/// app renders, talking to VibeDesk.Api over HTTP.
/// </summary>
/// <remarks>
/// Photino rather than WPF, because the spec's WPF Blazor Hybrid is Windows-only and the goal here
/// is Windows, macOS and Linux from one project. Avalonia was the first choice; see Plan.md Fase 7
/// for why it did not survive contact with what is actually published on NuGet.
/// </remarks>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var builder = PhotinoBlazorAppBuilder.CreateDefault(args);

        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables("VIBEDESK_")
            .AddCommandLine(args)
            .Build();

        builder.Services.AddSingleton<IConfiguration>(configuration);
        builder.Services.AddVibeDeskApiClient(configuration);

        builder.RootComponents.Add<ClientRoot>("#app");

        var app = builder.Build();

        app.MainWindow
            .SetTitle("VibeDesk")
            .SetUseOsDefaultSize(false)
            .SetSize(1440, 900)
            .SetUseOsDefaultLocation(true)
            .SetResizable(true);

        // An unhandled exception in a desktop app has nowhere to go: without this the window simply
        // vanishes and the user is left with nothing to report.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            app.MainWindow.ShowMessage("VibeDesk", (e.ExceptionObject as Exception)?.Message ?? "Unknown error");

        app.Run();
    }
}

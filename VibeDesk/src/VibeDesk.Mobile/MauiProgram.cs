using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VibeDesk.Client;

namespace VibeDesk.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        builder.Services.AddMauiBlazorWebView();

        // Configuration is in code rather than appsettings.json: bundling a JSON file into an APK
        // means shipping the server address inside the package, and the address is the one thing a
        // deployment changes. VIBEDESK_Api__BaseAddress overrides it at runtime.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Api:BaseAddress"] = DefaultApiAddress,
            })
            .AddEnvironmentVariables("VIBEDESK_")
            .Build();

        builder.Services.AddSingleton<IConfiguration>(configuration);
        builder.Services.AddVibeDeskApiClient(configuration);

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

    /// <summary>
    /// The Android emulator reaches the host machine on 10.0.2.2, never on localhost — localhost
    /// inside the emulator is the emulated device itself, which is the single most common reason a
    /// mobile build "cannot reach the server".
    /// </summary>
    private static string DefaultApiAddress =>
        DeviceInfo.Platform == DevicePlatform.Android && DeviceInfo.DeviceType == DeviceType.Virtual
            ? "http://10.0.2.2:5299"
            : "https://localhost:7299";
}

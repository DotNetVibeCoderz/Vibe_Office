using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VibeDesk.Application.Assistant;

namespace VibeDesk.Ai;

public static class DependencyInjection
{
    /// <summary>
    /// Registers Mr Clippy. Safe to call unconditionally: with no keys configured the service still
    /// resolves and reports <see cref="IClippyService.IsConfigured"/> as false, which is what lets the
    /// chat panel explain what to set instead of throwing on the user's first message.
    /// </summary>
    public static IServiceCollection AddVibeDeskAssistant(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<AssistantOptions>(configuration.GetSection(AssistantOptions.SectionName));

        // Singleton: it only reads options and builds kernels, and the per-user state lives in the
        // plugins the scoped service attaches.
        services.AddSingleton<KernelFactory>();

        services.AddHttpClient(ClippyService.HttpClientName, client =>
        {
            // Tool calls block the reply, so a slow page must fail fast rather than stall the panel.
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("VibeDesk-Clippy/1.0");
        });

        services.AddScoped<IClippyService, ClippyService>();

        return services;
    }
}

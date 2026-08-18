using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using VibeDesk.Application.Abstractions;
using VibeDesk.Application.Assistant;
using VibeDesk.Application.Calendars;
using VibeDesk.Application.Drive;
using VibeDesk.Application.Platform;
using VibeDesk.Client.Services;
using VibeDesk.Ui.Services;

namespace VibeDesk.Client;

public static class DependencyInjection
{
    public const string HttpClientName = "vibedesk-api";

    /// <summary>
    /// Satisfies every contract <c>VibeDesk.Ui</c> is written against, over HTTP. A host that calls
    /// this needs no reference to <c>VibeDesk.Infrastructure</c> — which is the point.
    /// </summary>
    public static IServiceCollection AddVibeDeskApiClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = configuration.GetSection(ApiClientOptions.SectionName).Get<ApiClientOptions>()
                      ?? new ApiClientOptions();

        services.AddSingleton(options);

        // One named client, and therefore one message handler: the session sets its Authorization
        // header once, and every typed client below inherits it. Separate clients per service would
        // mean re-attaching the token in several places and forgetting it in one.
        services.AddHttpClient(HttpClientName, client =>
        {
            client.BaseAddress = new Uri(options.BaseAddress);
            client.Timeout = options.Timeout;
        });

        services.AddSingleton(sp => new ApiSession(Client(sp)));
        services.AddSingleton<ICurrentUser>(sp => sp.GetRequiredService<ApiSession>());

        services.AddSingleton<IDriveService>(sp => new DriveApiClient(Client(sp)));
        services.AddSingleton<IDocumentContentService>(sp => new DocumentContentApiClient(Client(sp)));
        services.AddSingleton<IVersionService>(sp => new VersionApiClient(Client(sp)));
        services.AddSingleton<IPermissionService>(sp => new PermissionApiClient(Client(sp)));
        services.AddSingleton<ICommentService>(sp => new CommentApiClient(Client(sp)));
        services.AddSingleton<ICalendarService>(sp => new CalendarApiClient(Client(sp)));

        services.AddSingleton<IUserDirectory>(sp => new UserDirectoryApiClient(Client(sp)));
        services.AddSingleton<INotificationService>(sp => new NotificationApiClient(Client(sp)));
        services.AddSingleton<IActivityService>(sp => new ActivityApiClient(Client(sp)));

        services.AddSingleton(sp => new ClippyApiClient(Client(sp)));
        services.AddSingleton<IClippyService>(sp => sp.GetRequiredService<ClippyApiClient>());

        // Blazor authorisation, so [Authorize] on the shared pages means the same thing here.
        services.AddAuthorizationCore();
        services.AddSingleton<AuthenticationStateProvider, SessionAuthenticationStateProvider>();

        // The shared UI's own services. Registered here rather than in each host so desktop and
        // mobile cannot drift apart on what the UI needs.
        services.AddSingleton<ToastService>();
        services.AddSingleton<UiState>();
        services.AddSingleton<MarkdownRenderer>();

        return services;
    }

    private static HttpClient Client(IServiceProvider sp) =>
        sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
}

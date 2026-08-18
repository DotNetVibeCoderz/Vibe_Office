using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using VibeDesk.Application.Abstractions;
using VibeDesk.Application.Calendars;
using VibeDesk.Application.Drive;
using VibeDesk.Application.Platform;
using VibeDesk.Application.Scripting;
using VibeDesk.Infrastructure.Caching;
using VibeDesk.Infrastructure.Configuration;
using VibeDesk.Infrastructure.Identity;
using VibeDesk.Infrastructure.Persistence;
using VibeDesk.Infrastructure.Services;
using VibeDesk.Infrastructure.Storage;

namespace VibeDesk.Infrastructure;

/// <summary>
/// Single registration entry point shared by the web, API, desktop and mobile hosts. Provider choices
/// (database, storage, cache) are resolved from configuration here so no host duplicates that logic.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddVibeDeskInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
        services.Configure<CacheOptions>(configuration.GetSection(CacheOptions.SectionName));

        services.AddVibeDeskDatabase(configuration);
        services.AddVibeDeskStorage(configuration);
        services.AddVibeDeskCache(configuration);
        services.AddVibeDeskServices();

        return services;
    }

    /// <summary>
    /// Registers the configured storage backend, wrapped in the encrypting decorator when
    /// <c>Storage:EncryptAtRest</c> is on. The decorator is applied here rather than inside each
    /// provider so every backend gets encryption for free.
    /// </summary>
    public static IServiceCollection AddVibeDeskStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>()
                      ?? new StorageOptions();

        services.AddSingleton<IStorageProvider>(sp =>
        {
            var storageOptions = sp.GetRequiredService<IOptions<StorageOptions>>();

            IStorageProvider provider = options.Provider switch
            {
                StorageProviderKind.AzureBlob => new AzureBlobStorageProvider(storageOptions),
                StorageProviderKind.S3 or StorageProviderKind.MinIO => new S3StorageProvider(storageOptions),
                _ => new FileSystemStorageProvider(storageOptions),
            };

            return options.EncryptAtRest
                ? new EncryptingStorageProvider(provider, storageOptions)
                : provider;
        });

        return services;
    }

    public static IServiceCollection AddVibeDeskCache(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = configuration.GetSection(CacheOptions.SectionName).Get<CacheOptions>()
                      ?? new CacheOptions();

        if (options.Provider == CacheProviderKind.Redis)
        {
            services.AddSingleton<ICacheService>(sp =>
                new RedisCacheService(sp.GetRequiredService<IOptions<CacheOptions>>()));
        }
        else
        {
            services.AddSingleton<ICacheService>(sp =>
                new MemoryCacheService(sp.GetRequiredService<IOptions<CacheOptions>>()));
        }

        return services;
    }

    /// <summary>
    /// Application services. Scoped because they depend on <see cref="AppDbContext"/> and
    /// <see cref="ICurrentUser"/>, both of which are per-request (per-circuit on Blazor Server).
    /// </summary>
    public static IServiceCollection AddVibeDeskServices(this IServiceCollection services)
    {
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IActivityService, ActivityService>();

        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<IDriveService, DriveService>();
        services.AddScoped<IDocumentContentService, DocumentContentService>();
        services.AddScoped<IVersionService, VersionService>();
        services.AddScoped<ICommentService, CommentService>();
        services.AddScoped<ICalendarService, CalendarService>();

        // Both interfaces, one instance: the trigger lookup is the same query surface as the script
        // service and splitting them would mean two DbContext-bound classes over the same tables.
        services.AddScoped<ScriptService>();
        services.AddScoped<IScriptService>(sp => sp.GetRequiredService<ScriptService>());
        services.AddScoped<IScriptTriggerLookup>(sp => sp.GetRequiredService<ScriptService>());

        services.AddScoped<IApiKeyService, ApiKeyService>();
        services.AddScoped<ISyncService, SyncService>();

        // Hosts with a realtime transport (SignalR) replace this; TryAdd means they win.
        services.TryAddScoped<ICollaborationNotifier, NullCollaborationNotifier>();

        // Hosts with a signed-in user replace this; background work keeps the system identity.
        services.TryAddScoped<ICurrentUser>(_ => new SystemCurrentUser());

        services.AddScoped<SampleDataSeeder>();

        return services;
    }

    /// <summary>
    /// Identity with password and lockout rules, plus the token providers that back 2FA. Registered
    /// separately because the desktop and mobile hosts authenticate against the API instead.
    /// </summary>
    public static IdentityBuilder AddVibeDeskIdentity(this IServiceCollection services)
    {
        return services
            .AddIdentityCore<AppUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedEmail = false;

                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;

                // Slows credential stuffing without locking real users out for long.
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
            })
            .AddRoles<AppRole>()
            .AddEntityFrameworkStores<AppDbContext>()
            // Required for the TOTP authenticator, email confirmation and password reset tokens.
            .AddDefaultTokenProviders();
    }
}

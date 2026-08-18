using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VibeDesk.Application.Abstractions;
using VibeDesk.Application.Platform;
using VibeDesk.Application.Scripting;
using VibeDesk.Scripting.Runtimes;
using VibeDesk.Scripting.Templates;
using VibeDesk.Scripting.Triggers;

namespace VibeDesk.Scripting;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the scripting engine. Call it after the infrastructure services, then call
    /// <see cref="AddVibeDeskScriptingTriggers"/> once every other registration is in place.
    /// </summary>
    public static IServiceCollection AddVibeDeskScripting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = configuration.GetSection(ScriptingOptions.SectionName).Get<ScriptingOptions>()
                      ?? new ScriptingOptions();

        services.Configure<ScriptingOptions>(configuration.GetSection(ScriptingOptions.SectionName));

        if (!options.Enabled)
        {
            // Still register the gallery: the UI lists templates whether or not runs are permitted,
            // and an empty script list is friendlier than a missing service.
            services.AddSingleton<IScriptTemplateGallery, TemplateGallery>();
            return services;
        }

        services.AddSingleton<IScriptTemplateGallery, TemplateGallery>();

        services.AddSingleton<IScriptRuntime, JavaScriptRuntime>();
        services.AddSingleton<IScriptRuntime, PythonRuntime>();
        services.AddSingleton<IScriptRuntime, CSharpRuntime>();

        services.AddHttpClient(ScriptExecutor.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(options.HttpTimeoutSeconds);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("VibeDesk-Script/1.0");
        });

        services.AddScoped<IScriptExecutor, ScriptExecutor>();

        services.AddSingleton<ScriptRunQueue>();
        services.AddSingleton<IScriptEventDispatcher, ScriptEventDispatcher>();

        services.AddScoped<ScriptImpersonation>();

        services.AddHostedService<ScriptWorkerService>();
        services.AddHostedService<ScriptSchedulerService>();

        return services;
    }

    /// <summary>
    /// Wraps the three host services scripting extends. <b>Call this last</b>, after every other
    /// registration.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Decoration replaces a registration, so anything registered afterwards silently replaces the
    /// wrapper and the behaviour disappears without an error. That is not hypothetical: with this
    /// work inside <see cref="AddVibeDeskScripting"/>, the API host's later
    /// <c>AddScoped&lt;ICurrentUser, ApiCurrentUser&gt;()</c> and
    /// <c>AddScoped&lt;ICollaborationNotifier, SignalRCollaborationNotifier&gt;()</c> both discarded
    /// it — event triggers fired and then failed with no current user, and content.saved never fired
    /// at all. Separating it into a call the host makes last is what makes the order explicit.
    /// </para>
    /// <para>
    /// Safe to call when scripting is disabled: there is no dispatcher to wire, so it does nothing.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddVibeDeskScriptingTriggers(this IServiceCollection services)
    {
        if (!services.Any(d => d.ServiceType == typeof(IScriptEventDispatcher))) return services;

        Decorate<IActivityService>(services, (inner, sp) =>
            new ScriptAwareActivityService(inner, sp.GetRequiredService<IScriptEventDispatcher>()));

        Decorate<ICollaborationNotifier>(services, (inner, sp) =>
            new ScriptAwareCollaborationNotifier(inner, sp.GetRequiredService<IScriptEventDispatcher>()));

        Decorate<ICurrentUser>(services, (inner, sp) => new ImpersonatingCurrentUser(
            sp.GetRequiredService<ScriptImpersonation>(), inner, sp.GetRequiredService<IUserDirectory>()));

        return services;
    }

    /// <summary>
    /// Replaces a registration with one that wraps it.
    /// </summary>
    /// <remarks>
    /// Written out rather than pulled from a package because it is fifteen lines and one behaviour.
    /// The original descriptor is captured and rebuilt by hand: taking the existing factory keeps the
    /// host's own choice intact, so decorating <see cref="ICurrentUser"/> works whether the host
    /// registered the HTTP one, the API one, or the background default.
    /// </remarks>
    private static void Decorate<TService>(
        IServiceCollection services,
        Func<TService, IServiceProvider, TService> decorate)
        where TService : class
    {
        var descriptor = services.LastOrDefault(d => d.ServiceType == typeof(TService));

        if (descriptor is null)
        {
            // Nothing to decorate — the host did not register it, so there is no behaviour to extend.
            return;
        }

        services.Remove(descriptor);

        services.Add(new ServiceDescriptor(
            typeof(TService),
            sp => decorate(Create<TService>(sp, descriptor), sp),
            descriptor.Lifetime));
    }

    private static TService Create<TService>(IServiceProvider sp, ServiceDescriptor descriptor)
        where TService : class
    {
        if (descriptor.ImplementationInstance is TService instance) return instance;

        if (descriptor.ImplementationFactory is { } factory) return (TService)factory(sp);

        if (descriptor.ImplementationType is { } type)
        {
            return (TService)ActivatorUtilities.CreateInstance(sp, type);
        }

        throw new InvalidOperationException(
            $"Cannot decorate {typeof(TService).Name}: its registration has no instance, factory or type.");
    }
}

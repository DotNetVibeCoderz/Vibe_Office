using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VibeDesk.Application.Abstractions;
using VibeDesk.Application.Calendars;
using VibeDesk.Application.Drive;
using VibeDesk.Application.Platform;
using VibeDesk.Application.Scripting;
using VibeDesk.Domain;
using VibeDesk.Scripting.Host;

namespace VibeDesk.Scripting;

/// <summary>
/// Picks the runtime, builds the sandbox for one run, and enforces the clock.
/// </summary>
public sealed class ScriptExecutor(
    IEnumerable<IScriptRuntime> runtimes,
    IOptions<ScriptingOptions> options,
    IDriveService drive,
    IDocumentContentService content,
    ICalendarService calendar,
    INotificationService notifications,
    ICurrentUser currentUser,
    IHttpClientFactory httpFactory,
    ILogger<ScriptExecutor> logger) : IScriptExecutor
{
    public const string HttpClientName = "vibedesk-scripting";

    private readonly ScriptingOptions _options = options.Value;
    private readonly Dictionary<ScriptLanguage, IScriptRuntime> _runtimes =
        runtimes.ToDictionary(r => r.Language);

    public IReadOnlyList<ScriptLanguage> AvailableLanguages => [.. _runtimes.Keys.Order()];

    public string? Validate(ScriptLanguage language, string code) =>
        _runtimes.TryGetValue(language, out var runtime)
            ? runtime.Validate(code)
            : $"{language} scripts are not enabled on this server.";

    public async Task<ScriptExecutionResult> ExecuteAsync(
        ScriptExecutionRequest request, CancellationToken ct = default)
    {
        if (!_options.Enabled) return ScriptExecutionResult.Refused("Scripting is disabled on this server.");

        if (!_runtimes.TryGetValue(request.Language, out var runtime))
        {
            return ScriptExecutionResult.Refused($"{request.Language} scripts are not enabled on this server.");
        }

        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return ScriptExecutionResult.Refused("The script is empty.");
        }

        var seconds = Math.Clamp(
            request.TimeoutSeconds ?? _options.DefaultTimeoutSeconds, 1, _options.MaxTimeoutSeconds);

        var host = new ScriptHost(request.Scopes, _options);

        host.Api = new WorkspaceApi(
            host,
            _options,
            drive,
            content,
            calendar,
            notifications,
            currentUser,
            httpFactory.CreateClient(HttpClientName),
            request.AllowedHosts)
        {
            Input = request.Input?.ToDictionary(kv => kv.Key, kv => kv.Value) ?? [],
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(seconds));

        // A dedicated thread, not the thread pool. Every host call blocks on an async service, so a
        // long script would otherwise hold a pool thread for its whole run and starve requests.
        var execution = Task.Factory.StartNew(
            () => runtime.ExecuteAsync(request, host, timeout.Token),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default).Unwrap();

        // Raced against the clock rather than simply awaited. Awaiting would wait for the script to
        // finish however long that took: cancellation is cooperative, and a runtime that cannot
        // observe the token mid-loop — compiled C# cannot — would hold the request open forever.
        // The two-second grace lets a runtime that *does* cooperate report its own timeout first,
        // which produces a better message than this one.
        var finished = await Task.WhenAny(
            execution,
            Task.Delay(TimeSpan.FromSeconds(seconds + 2), ct)).ConfigureAwait(false);

        if (finished != execution)
        {
            logger.LogWarning(
                "Script '{Name}' ({Language}) exceeded {Seconds}s and was abandoned; its thread is " +
                "still running and will finish on its own",
                request.Name, request.Language, seconds);

            // Observe the eventual failure so it never surfaces as an unobserved task exception.
            _ = execution.ContinueWith(
                t => logger.LogDebug(t.Exception, "Abandoned script '{Name}' ended", request.Name),
                TaskContinuationOptions.OnlyOnFaulted);

            return new ScriptExecutionResult(
                ScriptRunStatus.TimedOut, host.Output, null,
                $"The script exceeded its {seconds}s time limit.", seconds * 1000, host.Calls);
        }

        try
        {
            return await execution.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            return new ScriptExecutionResult(
                ScriptRunStatus.TimedOut, host.Output, null,
                $"The script exceeded its {seconds}s time limit.", seconds * 1000, host.Calls);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Script '{Name}' failed outside its runtime", request.Name);

            return new ScriptExecutionResult(
                ScriptRunStatus.Failed, host.Output, null, ex.Message, 0, host.Calls);
        }
    }
}

using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VibeDesk.Application.Abstractions;
using VibeDesk.Application.Platform;
using VibeDesk.Application.Scripting;
using VibeDesk.Domain;

namespace VibeDesk.Scripting.Triggers;

/// <summary>One queued run: which script, as whom, and what set it off.</summary>
/// <param name="Input">
/// What the script reads as <c>input</c>. An event trigger's whole purpose is knowing <i>which</i>
/// file changed, so the item id travels here rather than only in the human-readable detail.
/// </param>
public sealed record ScriptRunRequest(
    Guid ScriptId,
    Guid ActorId,
    ScriptRunTrigger TriggeredBy,
    string? Detail,
    IReadOnlyDictionary<string, string>? Input = null);

/// <summary>
/// The queue between "something happened" and "a script ran".
/// </summary>
/// <remarks>
/// Bounded and drop-newest. An event trigger fires inside whatever request caused the event, so the
/// alternative to bounding is letting a script storm hold up the app that produced it — and a
/// dropped automation is a far better failure than a wedged Drive.
/// </remarks>
public sealed class ScriptRunQueue(IOptions<ScriptingOptions> options, ILogger<ScriptRunQueue> logger)
{
    private readonly Channel<ScriptRunRequest> _channel =
        Channel.CreateBounded<ScriptRunRequest>(new BoundedChannelOptions(500)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
        });

    private readonly ScriptingOptions _options = options.Value;
    private readonly Lock _gate = new();
    private DateTimeOffset _windowStart = DateTimeOffset.UtcNow;
    private int _windowCount;

    public bool TryEnqueue(ScriptRunRequest request)
    {
        if (!WithinRateLimit())
        {
            logger.LogWarning(
                "Script {ScriptId} not queued: more than {Max} event runs in the last minute",
                request.ScriptId, _options.MaxEventRunsPerMinute);

            return false;
        }

        if (_channel.Writer.TryWrite(request)) return true;

        logger.LogWarning("Script run queue is full; dropped a run of {ScriptId}", request.ScriptId);
        return false;
    }

    public IAsyncEnumerable<ScriptRunRequest> ReadAllAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);

    /// <summary>
    /// A rolling one-minute budget. A script whose own writes fire its own trigger would otherwise
    /// loop forever, and that loop is indistinguishable from ordinary work until it is too late.
    /// </summary>
    private bool WithinRateLimit()
    {
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;

            if (now - _windowStart > TimeSpan.FromMinutes(1))
            {
                _windowStart = now;
                _windowCount = 0;
            }

            if (_windowCount >= _options.MaxEventRunsPerMinute) return false;

            _windowCount++;
            return true;
        }
    }
}

/// <summary>
/// Turns workspace events into script runs.
/// </summary>
/// <remarks>
/// Called by decorators around the existing activity and collaboration services, so no feature
/// service knows scripts exist. Matching is deliberately cheap — a name comparison and an optional
/// item filter — because this runs inside the request that caused the event.
/// </remarks>
public sealed class ScriptEventDispatcher(
    IServiceScopeFactory scopes,
    ScriptRunQueue queue,
    ILogger<ScriptEventDispatcher> logger) : IScriptEventDispatcher
{
    public async Task DispatchAsync(
        string eventName, Guid? driveItemId, string? detail, CancellationToken ct = default)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var triggers = scope.ServiceProvider.GetRequiredService<IScriptTriggerLookup>();

            var input = new Dictionary<string, string> { ["event"] = eventName };

            if (driveItemId is { } itemId) input["itemId"] = itemId.ToString();
            if (!string.IsNullOrWhiteSpace(detail)) input["detail"] = detail;

            foreach (var match in await triggers.MatchAsync(eventName, driveItemId, ct).ConfigureAwait(false))
            {
                queue.TryEnqueue(new ScriptRunRequest(
                    match.ScriptId,
                    match.OwnerId,
                    ScriptRunTrigger.Event,
                    $"{eventName} {detail}".Trim(),
                    input));
            }
        }
        catch (Exception ex)
        {
            // A trigger lookup must never fail the operation that produced the event. Renaming a file
            // should not break because an automation is misconfigured.
            logger.LogError(ex, "Could not dispatch script triggers for {Event}", eventName);
        }
    }
}

/// <summary>
/// Runs queued scripts, one at a time, each in its own DI scope and as its own owner.
/// </summary>
/// <remarks>
/// Serial rather than parallel: scripts write to the same documents through the same services, and
/// the throughput gain from concurrency is not worth two automations racing on one spreadsheet.
/// </remarks>
public sealed class ScriptWorkerService(
    ScriptRunQueue queue,
    IServiceScopeFactory scopes,
    ILogger<ScriptWorkerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in queue.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                using var scope = scopes.CreateScope();

                // The run acts as the script's owner, not as whoever tripped the trigger: a script
                // must never gain reach by being fired by someone with more access.
                scope.ServiceProvider.GetRequiredService<ScriptImpersonation>().ActAs(request.ActorId);

                var service = scope.ServiceProvider.GetRequiredService<IScriptService>();

                var run = await service
                    .RunAsync(request.ScriptId, request.Input, request.TriggeredBy, stoppingToken)
                    .ConfigureAwait(false);

                logger.LogInformation(
                    "Script {Script} finished {Status} in {Ms}ms ({Trigger})",
                    run.ScriptName, run.Status, run.DurationMs, request.TriggeredBy);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Queued run of script {ScriptId} failed", request.ScriptId);
            }
        }
    }
}

/// <summary>
/// Wakes periodically, queues the schedules that are due, and computes the next firing.
/// </summary>
public sealed class ScriptSchedulerService(
    IServiceScopeFactory scopes,
    ScriptRunQueue queue,
    IOptions<ScriptingOptions> options,
    ILogger<ScriptSchedulerService> logger) : BackgroundService
{
    private readonly ScriptingOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled || !_options.EnableScheduler) return;

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, _options.SchedulerIntervalSeconds)));

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                using var scope = scopes.CreateScope();
                var lookup = scope.ServiceProvider.GetRequiredService<IScriptTriggerLookup>();

                var now = DateTimeOffset.UtcNow;

                foreach (var due in await lookup.DueAsync(now, stoppingToken).ConfigureAwait(false))
                {
                    queue.TryEnqueue(new ScriptRunRequest(
                        due.ScriptId,
                        due.OwnerId,
                        ScriptRunTrigger.Schedule,
                        due.CronExpression,
                        new Dictionary<string, string>
                        {
                            ["event"] = "schedule",
                            ["cron"] = due.CronExpression ?? string.Empty,
                            ["firedAt"] = now.ToString("O"),
                        }));

                    // Advance first, so a crash mid-run cannot make the same firing repeat forever.
                    await lookup
                        .AdvanceAsync(
                            due.TriggerId,
                            CronSchedule.NextRun(due.CronExpression, due.TimeZoneId, now),
                            stoppingToken)
                        .ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "The script scheduler pass failed");
            }
        }
    }

}

/// <summary>
/// Lets a background run act as the script's owner.
/// </summary>
/// <remarks>
/// Scoped, and only ever set by the worker. In a web request it is never set, so
/// <see cref="ImpersonatingCurrentUser"/> falls through to the signed-in user and nothing changes.
/// </remarks>
public sealed class ScriptImpersonation
{
    public Guid? UserId { get; private set; }

    public void ActAs(Guid userId) => UserId = userId;
}

/// <summary>
/// Wraps whatever <see cref="ICurrentUser"/> the host registered, deferring to
/// <see cref="ScriptImpersonation"/> when a background run has set one.
/// </summary>
public sealed class ImpersonatingCurrentUser(
    ScriptImpersonation impersonation,
    ICurrentUser inner,
    IUserDirectory users) : ICurrentUser
{
    private UserSummaryDto? _cached;

    public Guid? Id => impersonation.UserId ?? inner.Id;

    public string? Email => impersonation.UserId is null ? inner.Email : Resolve()?.Email;

    public string? DisplayName => impersonation.UserId is null ? inner.DisplayName : Resolve()?.DisplayName;

    public bool IsAuthenticated => impersonation.UserId is not null || inner.IsAuthenticated;

    public bool IsInRole(string role) => impersonation.UserId is null
        ? inner.IsInRole(role)
        : Resolve()?.Roles.Contains(role, StringComparer.OrdinalIgnoreCase) == true;

    public Guid RequireId() => Id
        ?? throw new UnauthorizedAccessException("This operation requires a signed-in user.");

    private UserSummaryDto? Resolve() =>
        _cached ??= impersonation.UserId is { } id
            ? users.GetAsync(id).GetAwaiter().GetResult()
            : null;
}

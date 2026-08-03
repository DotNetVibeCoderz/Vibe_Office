using AutoWork.Core.Agents;

namespace AutoWork.Core.Runs;

/// <summary>
/// The handle the UI holds on a run in flight, so a user who sees the agent going the wrong way
/// can stop it, say what they actually meant, and let it carry on — instead of cancelling and
/// re-typing the whole job.
///
/// Pausing takes effect between steps, never inside one. Interrupting mid-step would mean either
/// abandoning a tool call already sent to the provider or leaving a half-finished write behind;
/// waiting for the step boundary costs a few seconds and keeps the conversation valid, which is
/// the same reason compaction only happens there.
/// </summary>
public sealed class RunController
{
    private readonly Lock _gate = new();

    /// <summary>Reset each time the run is paused, so a waiter only wakes on a fresh resume.</summary>
    private TaskCompletionSource _resumed = Completed();

    private string? _correction;

    public bool IsPaused { get; private set; }

    /// <summary>Raised on pause, on resume, and when a correction is queued.</summary>
    public event EventHandler? StateChanged;

    public void Pause()
    {
        lock (_gate)
        {
            if (IsPaused) return;
            IsPaused = true;
            _resumed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Lets the run continue. <paramref name="correction"/> is handed to the model as the user's
    /// next word on the matter before the upcoming step runs; null simply resumes.
    /// </summary>
    public void Resume(string? correction = null)
    {
        TaskCompletionSource toSignal;

        lock (_gate)
        {
            if (!IsPaused) return;

            if (!string.IsNullOrWhiteSpace(correction))
                _correction = _correction is null ? correction.Trim() : _correction + "\n" + correction.Trim();

            IsPaused = false;
            toSignal = _resumed;
        }

        toSignal.TrySetResult();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Queues a correction without pausing. Useful when the user types while the run is already
    /// held, and harmless while it is running — it is picked up at the next step boundary.
    /// </summary>
    public void Steer(string correction)
    {
        if (string.IsNullOrWhiteSpace(correction)) return;

        lock (_gate)
            _correction = _correction is null ? correction.Trim() : _correction + "\n" + correction.Trim();

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Takes the pending correction, if any, and clears it so it is applied once.</summary>
    public string? TakeCorrection()
    {
        lock (_gate)
        {
            var pending = _correction;
            _correction = null;
            return pending;
        }
    }

    /// <summary>
    /// Blocks while paused. Returns immediately when running, so the orchestrator can call this
    /// at every step boundary without paying for it on the normal path.
    /// </summary>
    public async Task WaitIfPausedAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            Task wait;

            lock (_gate)
            {
                if (!IsPaused) return;
                wait = _resumed.Task;
            }

            await wait.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Releases anyone waiting, so a cancelled run does not sit paused forever.</summary>
    public void Abandon()
    {
        lock (_gate)
        {
            IsPaused = false;
            _resumed.TrySetResult();
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static TaskCompletionSource Completed()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();
        return source;
    }
}

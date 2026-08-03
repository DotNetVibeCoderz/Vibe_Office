using AutoWork.Core.Security;

namespace AutoWork.Desktop.Services;

public sealed class PendingApproval
{
    private readonly TaskCompletionSource<ApprovalDecision> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public PendingApproval(ApprovalRequest request) => Request = request;

    public ApprovalRequest Request { get; }
    public Task<ApprovalDecision> Completion => _completion.Task;

    public void Answer(ApprovalDecision decision) => _completion.TrySetResult(decision);
}

/// <summary>
/// Routes approval requests from the agent to the Work view and waits for the person to answer.
///
/// Requests are queued rather than stacked into dialogs: a batch operation can raise several,
/// and burying someone under modal windows is how they end up clicking Allow without reading.
/// A cancelled run releases anything still waiting, so no agent thread is left parked forever.
/// </summary>
public sealed class UiApprovalBroker : IApprovalBroker
{
    /// <summary>Raised on the agent's thread. The view model marshals to the UI thread.</summary>
    public event Action<PendingApproval>? Requested;

    /// <summary>Raised when a request is answered or withdrawn, so the view can clear it.</summary>
    public event Action<PendingApproval>? Resolved;

    private readonly List<PendingApproval> _outstanding = [];
    private readonly Lock _gate = new();

    public async Task<ApprovalDecision> RequestAsync(
        ApprovalRequest request, CancellationToken cancellationToken = default)
    {
        var pending = new PendingApproval(request);

        lock (_gate) _outstanding.Add(pending);

        Requested?.Invoke(pending);

        try
        {
            await using var registration = cancellationToken.Register(() => pending.Answer(ApprovalDecision.Denied));
            return await pending.Completion.ConfigureAwait(false);
        }
        finally
        {
            lock (_gate) _outstanding.Remove(pending);
            Resolved?.Invoke(pending);
        }
    }

    /// <summary>Denies everything still waiting — used when a run is stopped.</summary>
    public void ReleaseAll()
    {
        PendingApproval[] outstanding;
        lock (_gate) outstanding = _outstanding.ToArray();

        foreach (var pending in outstanding)
            pending.Answer(ApprovalDecision.Denied);
    }
}

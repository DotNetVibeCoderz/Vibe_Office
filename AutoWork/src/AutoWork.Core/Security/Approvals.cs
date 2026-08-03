namespace AutoWork.Core.Security;

public enum ApprovalKind
{
    WriteFiles,
    DeleteFiles,
    RunCommand,
    ControlInput,
    CaptureScreen,
    NetworkAccess,
    Other,
}

public enum ApprovalDecision
{
    Denied = 0,
    /// <summary>Allow this one action.</summary>
    Approved = 1,
    /// <summary>Allow every further action of this kind until the run ends.</summary>
    ApprovedForRun = 2,
}

/// <summary>What the user is being asked to allow. Phrased for a person, not a log parser.</summary>
public sealed record ApprovalRequest
{
    public string Id { get; init; } = Guid.NewGuid().ToString("n")[..8];
    public string RunId { get; init; } = "";
    public ApprovalKind Kind { get; init; } = ApprovalKind.Other;

    /// <summary>One line, active voice: "Delete 12 files in ~/Downloads".</summary>
    public required string Title { get; init; }

    /// <summary>The specifics — the command line, the file list, the destination.</summary>
    public string Detail { get; init; } = "";

    public IReadOnlyList<string> AffectedPaths { get; init; } = [];

    public DateTimeOffset RequestedAt { get; init; } = DateTimeOffset.Now;
}

public interface IApprovalBroker
{
    Task<ApprovalDecision> RequestAsync(ApprovalRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Used by tests and by explicitly unattended runs.</summary>
public sealed class AutoApproveBroker : IApprovalBroker
{
    public Task<ApprovalDecision> RequestAsync(ApprovalRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(ApprovalDecision.Approved);
}

/// <summary>The safe default when no UI is attached.</summary>
public sealed class DenyAllBroker : IApprovalBroker
{
    public Task<ApprovalDecision> RequestAsync(ApprovalRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(ApprovalDecision.Denied);
}

/// <summary>
/// Wraps a broker and remembers "allow for the rest of this run" answers, so a batch of 200
/// renames asks once instead of 200 times. Memory is scoped per run and per kind, and is
/// dropped when the run ends.
/// </summary>
public sealed class ApprovalCoordinator : IApprovalBroker
{
    private readonly IApprovalBroker _inner;
    private readonly Dictionary<(string RunId, ApprovalKind Kind), bool> _standing = [];
    private readonly Lock _gate = new();

    public ApprovalCoordinator(IApprovalBroker inner) => _inner = inner;

    public async Task<ApprovalDecision> RequestAsync(ApprovalRequest request, CancellationToken cancellationToken = default)
    {
        var key = (request.RunId, request.Kind);

        lock (_gate)
        {
            if (_standing.TryGetValue(key, out var granted))
                return granted ? ApprovalDecision.Approved : ApprovalDecision.Denied;
        }

        var decision = await _inner.RequestAsync(request, cancellationToken).ConfigureAwait(false);

        if (decision == ApprovalDecision.ApprovedForRun)
        {
            lock (_gate) _standing[key] = true;
            return ApprovalDecision.Approved;
        }

        return decision;
    }

    public void ForgetRun(string runId)
    {
        lock (_gate)
        {
            foreach (var key in _standing.Keys.Where(k => k.RunId == runId).ToList())
                _standing.Remove(key);
        }
    }
}

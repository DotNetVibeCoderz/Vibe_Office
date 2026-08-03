namespace AutoWork.Core.Security;

/// <summary>
/// Thrown when a tool asks for something the permission policy forbids. Tools let this
/// propagate: the agent loop turns it into a tool error the model can read and route around,
/// and the action log records the attempt.
/// </summary>
public sealed class SandboxViolationException : Exception
{
    public SandboxViolationException(string message, string? path = null, string? reason = null)
        : base(message)
    {
        Path = path;
        Reason = reason;
    }

    public string? Path { get; }

    /// <summary>Short machine-readable cause, e.g. "outside-roots", "denied-pattern", "read-only".</summary>
    public string? Reason { get; }
}

using System.Diagnostics;
using AutoWork.Core.Agents;
using AutoWork.Core.Logging;
using AutoWork.Core.Security;

namespace AutoWork.Tools;

/// <summary>
/// Shared plumbing for every tool group: permission checks, approval prompts, action logging
/// and a consistent result vocabulary.
///
/// Tools return strings rather than throwing, because the string goes straight back to the
/// model. A refusal the model can read ("REFUSED: ~/Documents is read-only") lets it pick a
/// different approach; an exception just ends the turn.
/// </summary>
public abstract class ToolSetBase
{
    protected ToolSetBase(ToolContext context) => Context = context;

    protected ToolContext Context { get; }
    protected PathGuard Guard => Context.Guard;
    protected IActionLog Log => Context.Log;
    protected string RunId => Context.RunId;

    protected abstract AgentOrgan Organ { get; }

    /// <summary>Permission or policy said no. The model should try something else.</summary>
    protected static string Refused(string reason) => $"REFUSED: {reason}";

    /// <summary>Something went wrong mechanically. Often retryable.</summary>
    protected static string Failed(string message) => $"ERROR: {message}";

    protected static string Ok(string message) => message;

    /// <summary>
    /// Runs the body, converting the exceptions tools realistically hit into readable results
    /// and recording the outcome in the action log.
    /// </summary>
    protected async Task<string> GuardedAsync(
        string action,
        string summary,
        Func<Task<string>> body,
        IReadOnlyList<string>? paths = null,
        ApprovalKind? approval = null,
        string? approvalDetail = null,
        Core.Diff.TextDiffResult? preview = null)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (approval is { } kind)
            {
                var decision = await Context.Approvals.RequestAsync(new ApprovalRequest
                {
                    RunId = RunId,
                    Kind = kind,
                    Title = summary,
                    Detail = approvalDetail ?? "",
                    AffectedPaths = paths ?? [],
                    Preview = preview,
                }).ConfigureAwait(false);

                if (decision == ApprovalDecision.Denied)
                {
                    Log.Denied(RunId, Organ, action, summary, "The user declined this action.", Display(paths));
                    return Refused("The user declined this action. Do not retry it; continue with the rest of the task or stop and explain.");
                }
            }

            var result = await body().ConfigureAwait(false);
            stopwatch.Stop();

            var failed = result.StartsWith("ERROR:", StringComparison.Ordinal)
                      || result.StartsWith("REFUSED:", StringComparison.Ordinal);

            if (failed)
                Log.Failure(RunId, Organ, action, summary, result, Display(paths));
            else
                Log.Success(RunId, Organ, action, summary, Display(paths), stopwatch.ElapsedMilliseconds);

            return result;
        }
        catch (SandboxViolationException ex)
        {
            Log.Denied(RunId, Organ, action, summary, ex.Message, Display(paths));
            return Refused(ex.Message);
        }
        catch (OperationCanceledException)
        {
            Log.Append(new ActionLogEntry
            {
                RunId = RunId, Organ = Organ, Action = action, Summary = summary,
                Outcome = ActionOutcome.Cancelled, Paths = Display(paths),
            });
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            Log.Failure(RunId, Organ, action, summary, ex.Message, Display(paths));
            return Failed($"The operating system denied access: {ex.Message}");
        }
        catch (IOException ex)
        {
            Log.Failure(RunId, Organ, action, summary, ex.Message, Display(paths));
            return Failed(ex.Message);
        }
        catch (Exception ex)
        {
            Log.Failure(RunId, Organ, action, summary, ex.Message, Display(paths));
            return Failed($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    protected Task<string> GuardedAsync(
        string action, string summary, Func<string> body,
        IReadOnlyList<string>? paths = null,
        ApprovalKind? approval = null, string? approvalDetail = null) =>
        GuardedAsync(action, summary, () => Task.FromResult(body()), paths, approval, approvalDetail);

    private static IReadOnlyList<string> Display(IReadOnlyList<string>? paths) =>
        paths is null ? [] : paths.Select(PathGuard.Describe).ToArray();

    /// <summary>
    /// Resolves a path the model supplied. Bare names are treated as relative to the run's
    /// working directory, so "report.xlsx" lands somewhere sensible instead of the process CWD.
    /// </summary>
    protected string Locate(string path) =>
        Path.IsPathRooted(path) || path.StartsWith('~')
            ? ExpandHome(path)
            : Path.Combine(Context.WorkingDirectory, path);

    private static string ExpandHome(string path) =>
        path.StartsWith('~')
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[1..].TrimStart('/', '\\'))
            : path;

    protected static string Human(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.##} GB",
    };

    /// <summary>Keeps tool output from swallowing the context window.</summary>
    protected static string Cap(string text, int maxChars = 12_000) =>
        text.Length <= maxChars
            ? text
            : text[..maxChars] + $"\n… [truncated, {text.Length - maxChars:N0} more characters]";
}

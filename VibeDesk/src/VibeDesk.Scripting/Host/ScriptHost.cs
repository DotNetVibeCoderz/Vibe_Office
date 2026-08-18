using System.Diagnostics;
using System.Text;
using VibeDesk.Application.Abstractions;
using VibeDesk.Application.Scripting;
using VibeDesk.Domain;

namespace VibeDesk.Scripting.Host;

/// <summary>
/// Console, scope enforcement and the audit trail for one run. Shared by all three runtimes, so a
/// permission check cannot be right in JavaScript and wrong in Python.
/// </summary>
public sealed class ScriptHost(ScriptScope granted, ScriptingOptions options) : IScriptHost
{
    private readonly StringBuilder _output = new();
    private readonly List<ScriptApiCallDto> _calls = [];
    private bool _truncated;

    public ScriptScope Granted { get; } = granted;

    /// <summary>
    /// The workspace API this run is bound to. Lives on the host rather than being injected into the
    /// runtime because a runtime is resolved once from DI and reused, while the API is rebuilt for
    /// every run with that run's scopes and that user's services.
    /// </summary>
    public WorkspaceApi Api { get; internal set; } = null!;

    public string Output => _output.ToString();

    public IReadOnlyList<ScriptApiCallDto> Calls => _calls;

    public void Log(string message)
    {
        if (_truncated) return;

        if (_output.Length + message.Length > options.MaxOutputChars)
        {
            _output.AppendLine("… output truncated");
            _truncated = true;
            return;
        }

        _output.AppendLine(message);
    }

    public void Require(ScriptScope required, string operation)
    {
        if ((Granted & required) == required) return;

        // Named rather than generic: "this script cannot do X" is the whole point of scopes, and a
        // vague denial sends the author hunting through their own code instead of their manifest.
        throw new ScriptPermissionException(
            $"This script does not hold the '{required}' scope, which {operation} requires. " +
            "Grant it in the script's settings and run again.");
    }

    public void RecordCall(string method, string? target, bool succeeded, int durationMs)
    {
        // Capped: a loop over ten thousand cells should not produce ten thousand audit rows.
        if (_calls.Count >= 500) return;

        _calls.Add(new ScriptApiCallDto(method, Trim(target), succeeded, durationMs));
    }

    /// <summary>Runs a host operation, timing it and recording it whether or not it succeeds.</summary>
    public T Track<T>(string method, string? target, Func<T> operation)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            var result = operation();
            RecordCall(method, target, true, (int)sw.ElapsedMilliseconds);
            return result;
        }
        catch (Exception)
        {
            RecordCall(method, target, false, (int)sw.ElapsedMilliseconds);
            throw;
        }
    }

    public void Track(string method, string? target, Action operation) =>
        Track<object?>(method, target, () => { operation(); return null; });

    private static string? Trim(string? value) =>
        value is { Length: > 160 } ? value[..160] + "…" : value;
}

/// <summary>
/// A script asked for something outside its granted scopes. Distinct from
/// <see cref="ForbiddenException"/> so the run is reported as the script's fault, not the user's.
/// </summary>
public sealed class ScriptPermissionException(string message) : Exception(message);

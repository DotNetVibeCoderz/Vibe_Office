using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using IronPython.Hosting;
using IronPython.Runtime.Exceptions;
using Microsoft.Extensions.Options;
using Microsoft.Scripting;
using Microsoft.Scripting.Hosting;
using VibeDesk.Application.Scripting;
using VibeDesk.Domain;
using VibeDesk.Scripting.Host;
using VibeDeskHost = VibeDesk.Scripting.Host.ScriptHost;

namespace VibeDesk.Scripting.Runtimes;

/// <summary>
/// Python, on IronPython.
/// </summary>
/// <remarks>
/// <para>
/// IronPython is a pure-.NET implementation, which is what makes it constrainable — a CPython
/// subprocess would be a whole operating-system process to contain. The cost is real and worth
/// stating plainly: <b>C-extension packages do not load</b>. There is no NumPy and no pandas here,
/// because neither is Python code. Tabular work is served by <see cref="DataFrame"/> instead, which
/// covers filtering, grouping, aggregation and pivoting and behaves identically in all three
/// languages.
/// </para>
/// <para>
/// Interruption is handled by a trace hook: IronPython calls it per line, so a runaway loop is
/// stopped at the next statement rather than running until the process is recycled.
/// </para>
/// </remarks>
public sealed partial class PythonRuntime(IOptions<ScriptingOptions> options) : IScriptRuntime
{
    private readonly ScriptingOptions _options = options.Value;

    public ScriptLanguage Language => ScriptLanguage.Python;

    /// <summary>
    /// Modules that would hand a script the machine. IronPython exposes the CLR through <c>clr</c>,
    /// so that one matters more here than in CPython.
    /// </summary>
    private static readonly string[] DeniedModules =
    [
        "clr", "System", "Microsoft", "os", "sys", "subprocess", "socket", "shutil", "ctypes",
        "importlib", "imp", "pickle", "marshal", "multiprocessing", "threading", "asyncio",
        "tempfile", "pathlib", "glob", "signal", "resource", "pty", "fcntl", "winreg", "msvcrt",
    ];

    public string? Validate(string code)
    {
        if (Denied(code) is { } denied) return denied;

        try
        {
            var engine = Python.CreateEngine();

            // Compile, do not run: syntax errors belong in the editor.
            engine.CreateScriptSourceFromString(code, Microsoft.Scripting.SourceCodeKind.Statements)
                .Compile();

            return null;
        }
        catch (Exception ex)
        {
            return $"Syntax error: {ex.Message}";
        }
    }

    public Task<ScriptExecutionResult> ExecuteAsync(
        ScriptExecutionRequest request,
        IScriptHost host,
        CancellationToken ct = default)
    {
        var scriptHost = (VibeDeskHost)host;
        var sw = Stopwatch.StartNew();

        if (Denied(request.Code) is { } denied)
        {
            return Task.FromResult(new ScriptExecutionResult(
                ScriptRunStatus.Refused, string.Empty, null, denied, 0, []));
        }

        // Tracing and Frames must be on at engine creation or SetTrace below is never called — the
        // hook is silently ignored, and a `while True:` then runs until the host process is recycled.
        var engine = Python.CreateEngine(new Dictionary<string, object>
        {
            ["Tracing"] = true,
            ["Frames"] = true,
        });

        // No search paths: an empty list means `import` cannot reach the filesystem for a module,
        // only the built-ins that survived the source check above.
        engine.SetSearchPaths([]);

        using var output = new MemoryStream();
        engine.Runtime.IO.SetOutput(output, Encoding.UTF8);
        engine.Runtime.IO.SetErrorOutput(output, Encoding.UTF8);

        // Per-line hook. Without it a `while True:` runs until the host process is recycled, because
        // .NET has no way to abort a running thread.
        //
        // The hook must return *itself*: a trace function that returns null tells IronPython to stop
        // tracing that frame, so the check silently stops happening after the first line — exactly as
        // useless as having no hook at all.
        TracebackDelegate? hook = null;
        hook = (frame, kind, payload) =>
        {
            ct.ThrowIfCancellationRequested();
            return hook;
        };

        engine.SetTrace(hook);

        var scope = engine.CreateScope();
        scope.SetVariable("api", scriptHost.Api);
        scope.SetVariable("input", request.Input ?? new Dictionary<string, string>());

        try
        {
            var source = engine.CreateScriptSourceFromString(
                request.Code, Microsoft.Scripting.SourceCodeKind.Statements);

            source.Execute(scope);

            // A script signals its answer by assigning `result`, since Python statements have no
            // completion value the way a JavaScript program does.
            string? resultJson = null;

            if (scope.TryGetVariable("result", out object? value) && value is not null)
            {
                resultJson = JsonSerializer.Serialize(Plain(value), JsonHelper.Options);
            }

            Drain(output, scriptHost);

            return Task.FromResult(new ScriptExecutionResult(
                ScriptRunStatus.Succeeded, scriptHost.Output, resultJson, null,
                (int)sw.ElapsedMilliseconds, scriptHost.Calls));
        }
        catch (Exception ex)
        {
            Drain(output, scriptHost);

            var inner = Innermost(ex);

            var (status, message) = inner switch
            {
                OperationCanceledException => (
                    ScriptRunStatus.TimedOut, "The script exceeded its time limit."),
                ScriptPermissionException => (ScriptRunStatus.Refused, inner.Message),
                _ => (ScriptRunStatus.Failed, Describe(engine, ex)),
            };

            return Task.FromResult(new ScriptExecutionResult(
                status, scriptHost.Output, null, message, (int)sw.ElapsedMilliseconds, scriptHost.Calls));
        }
    }

    /// <summary>Formats the Python traceback, which is what a Python author expects to read.</summary>
    private static string Describe(ScriptEngine engine, Exception ex)
    {
        try
        {
            var details = engine.GetService<ExceptionOperations>().FormatException(ex);

            return string.IsNullOrWhiteSpace(details) ? ex.Message : details.Trim();
        }
        catch (Exception)
        {
            return ex.Message;
        }
    }

    private static void Drain(MemoryStream stream, VibeDeskHost host)
    {
        if (stream.Length == 0) return;

        var text = Encoding.UTF8.GetString(stream.ToArray()).TrimEnd();

        if (text.Length > 0) host.Log(text);
    }

    private static Exception Innermost(Exception ex)
    {
        var current = ex;
        while (current.InnerException is { } inner) current = inner;
        return current;
    }

    /// <summary>
    /// Refuses a source file that imports its way out. A text check rather than an AST walk on
    /// purpose: it also catches <c>__import__("os")</c> and the string-eval routes, which an import
    /// analysis would miss.
    /// </summary>
    private static string? Denied(string code)
    {
        foreach (Match match in ImportPattern().Matches(code))
        {
            var module = match.Groups[1].Value.Split('.')[0];

            if (DeniedModules.Contains(module, StringComparer.OrdinalIgnoreCase))
            {
                return $"'import {module}' is not permitted in a script.";
            }
        }

        if (DynamicImportPattern().IsMatch(code))
        {
            return "__import__() is not permitted in a script.";
        }

        if (CompilePattern().IsMatch(code))
        {
            return "eval(), exec() and compile() are not permitted in a script.";
        }

        if (OpenPattern().IsMatch(code))
        {
            return "open() is not permitted: scripts reach files through api.drive, not the filesystem.";
        }

        return null;
    }

    private static object? Plain(object? value) => value switch
    {
        null => null,
        string or bool or int or long or double or decimal => value,
        // IronPython dictionaries and lists implement the non-generic BCL interfaces, so matching on
        // those covers every Python container without naming IronPython internal types.
        System.Collections.IDictionary dict => dict.Keys.Cast<object>().ToDictionary(
            k => k?.ToString() ?? string.Empty, k => Plain(dict[k])),
        System.Collections.IEnumerable seq => seq.Cast<object?>().Select(Plain).ToList(),
        _ => value.ToString(),
    };

    [GeneratedRegex(@"^\s*(?:import|from)\s+([A-Za-z_][\w.]*)", RegexOptions.Multiline)]
    private static partial Regex ImportPattern();

    [GeneratedRegex(@"__import__\s*\(")]
    private static partial Regex DynamicImportPattern();

    [GeneratedRegex(@"\b(?:eval|exec|compile)\s*\(")]
    private static partial Regex CompilePattern();

    [GeneratedRegex(@"(?<![\w.])open\s*\(")]
    private static partial Regex OpenPattern();
}

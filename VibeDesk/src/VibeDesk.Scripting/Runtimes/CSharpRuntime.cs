using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.Extensions.Options;
using VibeDesk.Application.Scripting;
using VibeDesk.Domain;
using VibeDesk.Scripting.Host;
using VibeDesk.Scripting.Sandbox;

namespace VibeDesk.Scripting.Runtimes;

/// <summary>The globals a C# script sees: <c>Api</c>, <c>Input</c> and <c>Log</c>.</summary>
public sealed class CSharpGlobals
{
    public required WorkspaceApi Api { get; init; }

    public required IReadOnlyDictionary<string, string> Input { get; init; }

    public required Action<object?> Log { get; init; }
}

/// <summary>
/// C#, on Roslyn scripting.
/// </summary>
/// <remarks>
/// <para>
/// This is the runtime that needed real work to make safe. Jint cannot see the CLR and IronPython
/// can be denied its modules, but Roslyn compiles to ordinary IL with the whole framework in scope —
/// <c>File.Delete</c> is one line away. So C# source is analysed and refused before it runs; see
/// <see cref="CSharpGuard"/> for what that covers and, just as importantly, what it does not.
/// </para>
/// <para>
/// The other honest limitation: a compiled loop cannot be interrupted. JavaScript is stopped by
/// Jint's statement budget and Python by a trace hook, but .NET has no way to abort a running thread,
/// so a C# script that never yields will run until it finishes. The timeout still reports and the
/// run is marked timed out, but the thread is abandoned rather than killed.
/// </para>
/// </remarks>
public sealed class CSharpRuntime : IScriptRuntime
{
    private readonly ScriptingOptions _options;
    private readonly ImmutableArray<MetadataReference> _references;
    private readonly ScriptOptions _scriptOptions;

    public CSharpRuntime(IOptions<ScriptingOptions> options)
    {
        _options = options.Value;

        // A deliberately small reference set. It is not the security boundary — most of these types
        // are forwarded from System.Runtime, so withholding an assembly does not withhold a type —
        // but it keeps the surface an author can reach for close to what the guard permits.
        var assemblies = new List<Assembly>
        {
            typeof(object).Assembly,
            typeof(Enumerable).Assembly,
            typeof(System.Collections.Generic.List<>).Assembly,
            typeof(System.Text.RegularExpressions.Regex).Assembly,
            typeof(WorkspaceApi).Assembly,
            typeof(DataFrame).Assembly,
        };

        foreach (var name in _options.CSharpAssemblies)
        {
            try
            {
                assemblies.Add(Assembly.Load(name));
            }
            catch (Exception)
            {
                // A misspelled entry in configuration must not stop the whole scripting engine from
                // starting; the script that needed it fails with a normal "type not found".
            }
        }

        _references = [.. assemblies
            .Distinct()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => (MetadataReference)MetadataReference.CreateFromFile(a.Location))];

        _scriptOptions = ScriptOptions.Default
            .WithReferences(_references)
            .WithImports(
                "System",
                "System.Linq",
                "System.Collections.Generic",
                "System.Text.RegularExpressions",
                "VibeDesk.Scripting.Host")
            .WithAllowUnsafe(false)
            .WithEmitDebugInformation(false);
    }

    public ScriptLanguage Language => ScriptLanguage.CSharp;

    public string? Validate(string code) => CSharpGuard.Inspect(code, _references);

    public async Task<ScriptExecutionResult> ExecuteAsync(
        ScriptExecutionRequest request,
        IScriptHost host,
        CancellationToken ct = default)
    {
        var scriptHost = (ScriptHost)host;
        var sw = Stopwatch.StartNew();

        // Refuse first. For a compiled language this is the only point at which refusal is cheap and
        // total — once it is IL, the decision has already been made.
        if (CSharpGuard.Inspect(request.Code, _references) is { } refusal)
        {
            return new ScriptExecutionResult(
                ScriptRunStatus.Refused, string.Empty, null, refusal, (int)sw.ElapsedMilliseconds, []);
        }

        var globals = new CSharpGlobals
        {
            Api = scriptHost.Api,
            Input = request.Input ?? new Dictionary<string, string>(),
            Log = scriptHost.Api.Log,
        };

        try
        {
            var state = await CSharpScript.RunAsync(
                request.Code, _scriptOptions, globals, typeof(CSharpGlobals), ct).ConfigureAwait(false);

            var resultJson = state.ReturnValue is null
                ? null
                : JsonSerializer.Serialize(state.ReturnValue, JsonHelper.Options);

            return new ScriptExecutionResult(
                ScriptRunStatus.Succeeded, scriptHost.Output, resultJson, null,
                (int)sw.ElapsedMilliseconds, scriptHost.Calls);
        }
        catch (CompilationErrorException ex)
        {
            var message = string.Join(
                "\n",
                ex.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Take(10)
                    .Select(d => $"{d.Id}: {d.GetMessage()} (line {d.Location.GetLineSpan().StartLinePosition.Line + 1})"));

            return new ScriptExecutionResult(
                ScriptRunStatus.Failed, scriptHost.Output, null,
                message.Length > 0 ? message : ex.Message,
                (int)sw.ElapsedMilliseconds, scriptHost.Calls);
        }
        catch (Exception ex)
        {
            var inner = Innermost(ex);

            var (status, message) = inner switch
            {
                OperationCanceledException => (
                    ScriptRunStatus.TimedOut, "The script exceeded its time limit."),
                ScriptPermissionException => (ScriptRunStatus.Refused, inner.Message),
                _ => (ScriptRunStatus.Failed, inner.Message),
            };

            return new ScriptExecutionResult(
                status, scriptHost.Output, null, message, (int)sw.ElapsedMilliseconds, scriptHost.Calls);
        }
    }

    private static Exception Innermost(Exception ex)
    {
        var current = ex;
        while (current.InnerException is { } inner) current = inner;
        return current;
    }
}

using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Jint;
using Jint.Runtime;
using Jint.Runtime.Interop;
using Microsoft.Extensions.Options;
using VibeDesk.Application.Scripting;
using VibeDesk.Domain;
using VibeDesk.Scripting.Host;

namespace VibeDesk.Scripting.Runtimes;

/// <summary>
/// JavaScript, on Jint.
/// </summary>
/// <remarks>
/// Jint is an interpreter written in C#: it reaches nothing of the CLR beyond the objects handed to
/// it. The sandbox here is therefore a matter of not handing it anything dangerous and capping what
/// it can spend — which is why JavaScript needs no equivalent of <see cref="Sandbox.CSharpGuard"/>.
/// </remarks>
public sealed class JavaScriptRuntime(IOptions<ScriptingOptions> options) : IScriptRuntime
{
    private readonly ScriptingOptions _options = options.Value;

    public ScriptLanguage Language => ScriptLanguage.JavaScript;

    public string? Validate(string code)
    {
        try
        {
            // Parse only. A syntax error belongs in the editor, not in a scheduled run at 3am.
            Engine.PrepareScript(code);
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
        var scriptHost = (ScriptHost)host;
        var sw = Stopwatch.StartNew();

        var engine = new Engine(o =>
        {
            o.LimitMemory(_options.MaxMemoryMb * 1024L * 1024L)
                .MaxStatements(_options.MaxStatements)
                .LimitRecursion(256)
                .CancellationToken(ct);

            // .NET members reach JavaScript in camelCase, so `item.name` works and the API does not
            // read like C# transliterated. The CLR spelling still resolves, so a snippet copied from
            // the C# docs keeps working.
            o.SetTypeResolver(new TypeResolver { MemberNameCreator = MemberNames });
        });

        engine.SetValue("api", scriptHost.Api);
        engine.SetValue("input", request.Input ?? new Dictionary<string, string>());

        // console.log, because nobody writing JavaScript reaches for api.log first.
        engine.Execute(
            """
            var console = {
                log: function () {
                    api.log(Array.prototype.slice.call(arguments).map(function (v) {
                        return (v && typeof v === 'object') ? api.toJson(v) : String(v);
                    }).join(' '));
                }
            };
            console.error = console.warn = console.info = console.debug = console.log;
            """);

        try
        {
            var completion = engine.Evaluate(request.Code);

            var result = completion.IsUndefined() || completion.IsNull()
                ? null
                : JsonSerializer.Serialize(completion.ToObject(), JsonHelper.Options);

            return Task.FromResult(new ScriptExecutionResult(
                ScriptRunStatus.Succeeded, scriptHost.Output, result, null,
                (int)sw.ElapsedMilliseconds, scriptHost.Calls));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Failure(ex, scriptHost, (int)sw.ElapsedMilliseconds, ct));
        }
    }

    private static ScriptExecutionResult Failure(
        Exception ex, ScriptHost host, int durationMs, CancellationToken ct)
    {
        var inner = ex is TargetInvocationException { InnerException: { } t } ? t : ex;

        var (status, message) = inner switch
        {
            MemoryLimitExceededException => (
                ScriptRunStatus.TimedOut, "The script exceeded its memory limit."),
            StatementsCountOverflowException => (
                ScriptRunStatus.TimedOut,
                "The script ran too many statements — it is probably looping forever."),
            RecursionDepthOverflowException => (
                ScriptRunStatus.Failed, "The script recursed too deeply."),
            ExecutionCanceledException or OperationCanceledException => (
                ScriptRunStatus.TimedOut, "The script exceeded its time limit."),
            ScriptPermissionException => (ScriptRunStatus.Refused, inner.Message),
            JavaScriptException js => (ScriptRunStatus.Failed, Describe(js)),
            _ => (ScriptRunStatus.Failed, Unwrap(inner)),
        };

        return new ScriptExecutionResult(status, host.Output, null, message, durationMs, host.Calls);
    }

    /// <summary>
    /// A JavaScript error with its script line, not Jint's own stack — the latter is interpreter
    /// internals and tells the script's author nothing.
    /// </summary>
    private static string Describe(JavaScriptException ex)
    {
        var line = ex.Location.Start.Line;

        return line > 0 ? $"{ex.Message} (line {line})" : ex.Message;
    }

    /// <summary>Host exceptions arrive wrapped; the script's author needs the innermost message.</summary>
    private static string Unwrap(Exception ex)
    {
        var current = ex;
        while (current.InnerException is { } inner) current = inner;

        return current.Message;
    }

    private static IEnumerable<string> MemberNames(MemberInfo member)
    {
        var name = member.Name;

        yield return char.ToLowerInvariant(name[0]) + name[1..];
        yield return name;
    }
}

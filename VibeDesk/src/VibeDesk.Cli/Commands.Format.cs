using VibeDesk.Application.Scripting;
using VibeDesk.Domain;

namespace VibeDesk.Cli;

public static partial class Commands
{
    /// <summary>
    /// One shape for a finished run, whether it came from an ad-hoc execution or a recorded one, so
    /// the two never drift into looking like different tools.
    /// </summary>
    private static void Report(
        string status, string? output, string? resultJson, string? error,
        int durationMs, IReadOnlyList<ScriptApiCallDto> calls)
    {
        if (!string.IsNullOrWhiteSpace(output)) Output.Write(output.TrimEnd());

        if (!string.IsNullOrWhiteSpace(resultJson)) Output.Write("→ " + resultJson);

        if (!string.IsNullOrWhiteSpace(error)) Output.Error(error);

        // Only the failed calls, and only when there are some: a successful run's audit trail belongs
        // in the app, not in the terminal the author is watching.
        foreach (var call in calls.Where(c => !c.Succeeded))
        {
            Output.Muted($"    failed: {call.Method}{(call.Target is null ? "" : " " + call.Target)}");
        }

        Output.Muted($"{Output.Status(status)} in {durationMs} ms, {calls.Count} API call(s)");
    }

    private static ScriptLanguage? LanguageOf(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".js" => ScriptLanguage.JavaScript,
            ".py" => ScriptLanguage.Python,
            ".csx" or ".cs" => ScriptLanguage.CSharp,
            _ => null,
        };

    private static string LanguageName(ScriptLanguage language) => language switch
    {
        ScriptLanguage.Python => "Python",
        ScriptLanguage.CSharp => "C#",
        _ => "JavaScript",
    };

    /// <summary>A filename from a script name, for <c>pull</c> when no path was given.</summary>
    private static string Slug(string name)
    {
        var cleaned = new string([.. name.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-')]);
        var joined = string.Join('-', cleaned.Split('-', StringSplitOptions.RemoveEmptyEntries));

        return joined.Length == 0 ? "script" : joined;
    }
}

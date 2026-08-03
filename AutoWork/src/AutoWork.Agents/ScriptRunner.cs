using System.Diagnostics;
using System.Text;

namespace AutoWork.Agents;

/// <summary>
/// Runs a skill's bundled script with the right interpreter.
///
/// Scripts are launched directly rather than through a shell: the arguments are passed as a list,
/// so a filename containing a space or a quote is an argument and not an injection. That matters
/// more here than in the shell tool, because the arguments come from the model.
///
/// The interpreter is chosen from the extension and nothing else. There is no "just run it and
/// see" path — an unknown extension is refused by name, because guessing how to execute an
/// unfamiliar file is precisely the wrong instinct for code downloaded off the internet.
/// </summary>
internal sealed record ScriptRunner(string Interpreter, IReadOnlyList<string> LeadingArguments)
{
    /// <summary>Extensions that can be run, and what runs them.</summary>
    private static readonly Dictionary<string, ScriptRunner> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        // "python" first: on Windows "python3" often is not on PATH at all, and a launcher that
        // is missing looks identical to a script that failed.
        [".py"] = new(OperatingSystem.IsWindows() ? "python" : "python3", []),
        [".js"] = new("node", []),
        [".mjs"] = new("node", []),
        [".sh"] = new(OperatingSystem.IsWindows() ? "bash" : "/bin/sh", []),
        [".ps1"] = new("powershell", ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File"]),
    };

    public static IReadOnlyList<string> SupportedExtensions { get; } = [.. ByExtension.Keys];

    public static ScriptRunner? For(string path) =>
        ByExtension.GetValueOrDefault(Path.GetExtension(path));

    /// <summary>Python is the one that gets a per-skill environment; the rest run as they are.</summary>
    public bool UsesPython => Interpreter.Contains("python", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// <paramref name="interpreterOverride"/> lets a skill's own virtual environment be used in
    /// place of the system interpreter, which is what makes its installed libraries reachable.
    /// </summary>
    public async Task<string> ExecuteAsync(
        string scriptPath, IReadOnlyList<string> arguments, string workingDirectory, TimeSpan timeout,
        string? interpreterOverride = null)
    {
        var executable = interpreterOverride ?? Interpreter;

        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var leading in LeadingArguments) startInfo.ArgumentList.Add(leading);
        startInfo.ArgumentList.Add(scriptPath);
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        try
        {
            if (!process.Start()) return $"ERROR: {Interpreter} could not be started.";
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // The overwhelmingly common failure: the interpreter is not installed. Say which one.
            return $"ERROR: \"{executable}\" is not installed or not on PATH, so this script cannot run.";
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cancellation = new CancellationTokenSource(timeout);

        try
        {
            await process.WaitForExitAsync(cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return $"ERROR: the script was still running after {timeout.TotalSeconds:0} seconds and was stopped.\n" +
                   Compose(stdout, stderr);
        }

        var body = Compose(stdout, stderr);

        return process.ExitCode == 0
            ? (body.Length == 0 ? "The script finished with no output." : body)
            : $"ERROR: the script exited with code {process.ExitCode}.\n{body}";
    }

    private static void TryKill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }

    private static string Compose(StringBuilder stdout, StringBuilder stderr)
    {
        var builder = new StringBuilder();

        if (stdout.Length > 0) builder.Append(Cap(stdout.ToString(), 8000));

        if (stderr.Length > 0)
        {
            if (builder.Length > 0) builder.AppendLine();
            builder.AppendLine("stderr:").Append(Cap(stderr.ToString(), 4000));
        }

        return builder.ToString().TrimEnd();
    }

    private static string Cap(string text, int max) =>
        text.Length <= max ? text : text[..max] + $"\n… [truncated, {text.Length - max:N0} more characters]";
}

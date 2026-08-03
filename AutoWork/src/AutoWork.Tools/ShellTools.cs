using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using AutoWork.Core.Agents;
using AutoWork.Core.Security;
using Microsoft.Extensions.AI;

namespace AutoWork.Tools;

/// <summary>
/// Shell access. Off by default, approval-gated when on, and always run with the working
/// directory pinned inside a granted folder — a command can still do anything the user can,
/// so the guarantees here are about visibility and consent rather than containment.
/// </summary>
public sealed class ShellTools : ToolSetBase, IToolProvider
{
    public ShellTools(ToolContext context) : base(context) { }

    protected override AgentOrgan Organ => AgentOrgan.Hands;

    public string Name => "Shell";

    public IEnumerable<ToolDescriptor> GetTools(ToolContext context)
    {
        // Advertising a tool the policy forbids just invites the model to keep trying it.
        if (!context.Guard.Policy.AllowShell) yield break;

        var tools = new ShellTools(context);

        yield return new ToolDescriptor
        {
            Function = AIFunctionFactory.Create(tools.RunAsync, "shell_run",
                "Run a shell command and return its output. Prefer the dedicated file and document tools when they can do the job — use this only for things they cannot."),
            Organ = AgentOrgan.Hands,
            Risk = ToolRisk.System,
            Category = "Shell",
            ApprovalKind = ApprovalKind.RunCommand,
        };
    }

    [Description("Run a shell command.")]
    private Task<string> RunAsync(
        [Description("The command line to run.")] string command,
        [Description("Folder to run it in. Must be a granted folder.")] string? workingDirectory = null,
        [Description("Seconds to wait before giving up.")] int timeoutSeconds = 60)
    {
        var directory = Locate(workingDirectory ?? Context.WorkingDirectory);

        return GuardedAsync("shell.run", $"Run: {Shorten(command)}", async () =>
        {
            if (!Guard.Policy.AllowShell)
                return Refused("Running commands is turned off in Settings › Permissions.");

            var canonicalDirectory = Guard.EnsureReadable(directory);
            if (!Directory.Exists(canonicalDirectory))
                return Failed($"{PathGuard.Describe(canonicalDirectory)} is not a folder.");

            var (permitted, executable) = InspectCommand(command);
            if (!permitted)
                return Refused($"\"{executable}\" is not in the allowed command list in Settings › Permissions.");

            var timeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 1, Context.Options.ToolTimeoutSeconds));
            return await ExecuteAsync(command, canonicalDirectory, timeout).ConfigureAwait(false);
        },
        [directory],
        Guard.Policy.ShellRequiresApproval ? ApprovalKind.RunCommand : null,
        $"{command}\n\nWorking directory: {PathGuard.Describe(directory)}");
    }

    private (bool Permitted, string Executable) InspectCommand(string command)
    {
        var executable = FirstToken(command);

        var allowList = Guard.Policy.ShellAllowList;
        if (allowList.Count == 0) return (true, executable);

        var name = Path.GetFileNameWithoutExtension(executable);
        var permitted = allowList.Any(allowed =>
            string.Equals(allowed, executable, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetFileNameWithoutExtension(allowed), name, StringComparison.OrdinalIgnoreCase));

        return (permitted, executable);
    }

    /// <summary>First token, honouring a quoted executable path.</summary>
    private static string FirstToken(string command)
    {
        var trimmed = command.TrimStart();
        if (trimmed.Length == 0) return "";

        if (trimmed[0] is '"' or '\'')
        {
            var quote = trimmed[0];
            var close = trimmed.IndexOf(quote, 1);
            return close > 0 ? trimmed[1..close] : trimmed[1..];
        }

        var space = trimmed.IndexOf(' ');
        return space < 0 ? trimmed : trimmed[..space];
    }

    private static async Task<string> ExecuteAsync(string command, string workingDirectory, TimeSpan timeout)
    {
        var startInfo = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe") { ArgumentList = { "/d", "/c", command } }
            : new ProcessStartInfo("/bin/sh") { ArgumentList = { "-c", command } };

        startInfo.WorkingDirectory = workingDirectory;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;

        using var process = new Process { StartInfo = startInfo };

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        if (!process.Start())
            return Failed("The command could not be started.");

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
            return Failed($"The command was still running after {timeout.TotalSeconds:0} seconds and was stopped.\n" +
                          Compose(stdout, stderr));
        }

        var body = Compose(stdout, stderr);

        return process.ExitCode == 0
            ? Ok(body.Length == 0 ? "Command finished with no output." : body)
            : Failed($"Command exited with code {process.ExitCode}.\n{body}");
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

    private static string Shorten(string command) =>
        command.Length <= 90 ? command : command[..90] + "…";
}

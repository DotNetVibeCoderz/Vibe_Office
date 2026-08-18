using VibeDesk.Application.Scripting;
using VibeDesk.Domain;

namespace VibeDesk.Cli;

/// <summary>
/// Command dispatch. Hand-rolled rather than a parser library: the surface is nine verbs, and a
/// dependency-free single file is easier to ship as one executable.
/// </summary>
public static partial class Commands
{
    public static async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Help();
            return 0;
        }

        var rest = args[1..];

        return args[0] switch
        {
            "login" => await LoginAsync(rest, ct),
            "logout" => Logout(),
            "config" => Config(rest),
            "whoami" => await WhoAmIAsync(ct),
            "list" or "ls" => await ListAsync(ct),
            "run" => await RunScriptAsync(rest, ct),
            "check" => await CheckAsync(rest, ct),
            "pull" => await PullAsync(rest, ct),
            "push" => await PushAsync(rest, ct),
            "enable" => await SetEnabledAsync(rest, true, ct),
            "disable" => await SetEnabledAsync(rest, false, ct),
            "logs" => await LogsAsync(rest, ct),
            "templates" => await TemplatesAsync(rest, ct),
            _ => Unknown(args[0]),
        };
    }

    private static int Unknown(string verb)
    {
        Output.Error($"Unknown command '{verb}'.");
        Output.Hint("Run 'vibedesk help' for the list.");
        return 1;
    }

    private static void Help()
    {
        Output.Heading("vibedesk — run VibeDesk scripts from the terminal");
        Output.Write("");
        Output.Write("  login [email]              Sign in and store a token");
        Output.Write("  logout                     Forget the stored credential");
        Output.Write("  config [url] [--key K]     Show or set the server and API key");
        Output.Write("  whoami                     Who the stored credential belongs to");
        Output.Write("");
        Output.Write("  list                       Your scripts");
        Output.Write("  run <file|id> [options]    Run a local file, or a saved script by id");
        Output.Write("  check <file>               Static check without running");
        Output.Write("  pull <id> [file]           Write a saved script's source to disk");
        Output.Write("  push <file> [--id ID]      Save a local file as a script");
        Output.Write("  enable <id>                Arm a script so its triggers fire");
        Output.Write("  disable <id>               Stop a script running from any source");
        Output.Write("  logs [id] [--take N]       Recent runs");
        Output.Write("  templates [keyword]        Browse the template gallery");
        Output.Write("");
        Output.Heading("Options for run and push");
        Output.Write("  --scope <name>             Grant a scope; repeatable (DriveRead, SheetsWrite, …)");
        Output.Write("  --host <hostname>          Allow one outbound host; repeatable");
        Output.Write("  --input <key=value>        A value the script reads from 'input'; repeatable");
        Output.Write("  --timeout <seconds>        Override the server default");
        Output.Write("  --name <name>              Script name (push)");
        Output.Write("");
        Output.Muted("A local file's language comes from its extension: .js, .py, .csx");
        Output.Muted($"Config: {CliConfig.Path}");
    }
}

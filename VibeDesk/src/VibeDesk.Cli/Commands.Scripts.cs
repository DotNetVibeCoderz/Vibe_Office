using VibeDesk.Application.Scripting;
using VibeDesk.Domain;

namespace VibeDesk.Cli;

public static partial class Commands
{
    private static async Task<int> ListAsync(CancellationToken ct)
    {
        using var api = new ApiClient(CliConfig.Load());

        var scripts = await api.ListAsync(ct);

        if (scripts.Count == 0)
        {
            Output.Muted("No scripts yet. Create one in the web app, or push a local file.");
            return 0;
        }

        foreach (var script in scripts.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
        {
            var last = script.LastRunStatus is { } status ? Output.Status(status.ToString()) : "—";

            Output.Write(
                $"{script.Id}  {Output.Pad(script.LanguageLabel, 11)}" +
                $"{Output.Pad(script.Name, 34)}{Output.Pad(last, 12)}" +
                $"{(script.IsEnabled ? "" : "disabled")}");
        }

        return 0;
    }

    private static async Task<int> RunScriptAsync(string[] args, CancellationToken ct)
    {
        var options = CliOptions.Parse(args);

        if (options.Target is null)
        {
            Output.Error("Usage: vibedesk run <file|id> [--scope NAME] [--input k=v]");
            return 1;
        }

        using var api = new ApiClient(CliConfig.Load());

        // A saved script runs with the scopes and hosts it was saved with. The flags below cannot
        // widen them — that is the point of storing permissions with the script rather than the call.
        if (Guid.TryParse(options.Target, out var id))
        {
            var run = await api.RunSavedAsync(id, options.Input.Count > 0 ? options.Input : null, ct);

            Report(run.Status.ToString(), run.Output, run.ResultJson, run.Error, run.DurationMs, run.ApiCalls);

            return run.Status == ScriptRunStatus.Succeeded ? 0 : 2;
        }

        if (!File.Exists(options.Target))
        {
            Output.Error($"No such file, and not a script id: {options.Target}");
            return 1;
        }

        if (LanguageOf(options.Target) is not { } language)
        {
            Output.Error("Unrecognised extension. Use .js, .py or .csx.");
            return 1;
        }

        var result = await api.RunSourceAsync(new
        {
            language,
            code = await File.ReadAllTextAsync(options.Target, ct),
            scopes = options.Scopes,
            allowedHosts = options.Hosts.Count > 0 ? string.Join(',', options.Hosts) : null,
            timeoutSeconds = options.Timeout,
            name = Path.GetFileName(options.Target),
            input = options.Input.Count > 0 ? options.Input : null,
        }, ct);

        Report(result.Status.ToString(), result.Output, result.ResultJson, result.Error,
            result.DurationMs, result.ApiCalls);

        return result.Succeeded ? 0 : 2;
    }

    private static async Task<int> CheckAsync(string[] args, CancellationToken ct)
    {
        var path = args.FirstOrDefault(a => !a.StartsWith('-'));

        if (path is null || !File.Exists(path))
        {
            Output.Error("Usage: vibedesk check <file>");
            return 1;
        }

        if (LanguageOf(path) is not { } language)
        {
            Output.Error("Unrecognised extension. Use .js, .py or .csx.");
            return 1;
        }

        using var api = new ApiClient(CliConfig.Load());

        var problem = await api.ValidateAsync(language, await File.ReadAllTextAsync(path, ct), ct);

        if (problem is null)
        {
            Output.Success("No problems found. This is a static check, not a guarantee.");
            return 0;
        }

        Output.Error(problem);
        return 2;
    }
}

using VibeDesk.Application.Scripting;
using VibeDesk.Domain;

namespace VibeDesk.Cli;

public static partial class Commands
{
    private static async Task<int> PullAsync(string[] args, CancellationToken ct)
    {
        if (args.Length == 0 || !Guid.TryParse(args[0], out var id))
        {
            Output.Error("Usage: vibedesk pull <id> [file] [--force]");
            return 1;
        }

        using var api = new ApiClient(CliConfig.Load());

        var detail = await api.GetAsync(id, ct);
        var path = args.Skip(1).FirstOrDefault(a => !a.StartsWith('-'))
                   ?? Slug(detail.Script.Name) + detail.Script.Extension;

        if (File.Exists(path) && !args.Contains("--force"))
        {
            Output.Error($"{path} already exists. Pass --force to overwrite it.");
            return 1;
        }

        await File.WriteAllTextAsync(path, detail.Code, ct);

        Output.Success($"Wrote {path} (v{detail.Script.VersionNumber}).");
        return 0;
    }

    private static async Task<int> PushAsync(string[] args, CancellationToken ct)
    {
        var options = CliOptions.Parse(args);

        if (options.Target is null || !File.Exists(options.Target))
        {
            Output.Error("Usage: vibedesk push <file> [--id ID] [--name NAME] [--scope NAME]");
            return 1;
        }

        if (LanguageOf(options.Target) is not { } language)
        {
            Output.Error("Unrecognised extension. Use .js, .py or .csx.");
            return 1;
        }

        using var api = new ApiClient(CliConfig.Load());

        // Scopes travel only when the flag was given. An update keeps whatever the script already
        // had: pushing an edit should not quietly revoke the grants it needs to run.
        var scopes = options.ScopesGiven
            ? options.Scopes
            : options.Id is { } existing ? (await api.GetAsync(existing, ct)).Script.Scopes : ScriptScope.None;

        var saved = await api.SaveAsync(new ScriptInput
        {
            Id = options.Id,
            Name = options.Name ?? Path.GetFileNameWithoutExtension(options.Target),
            Language = language,
            Code = await File.ReadAllTextAsync(options.Target, ct),
            Scopes = scopes,
            AllowedHosts = options.Hosts.Count > 0 ? string.Join(',', options.Hosts) : null,
            TimeoutSeconds = options.Timeout,
            // Never enabled by a push. Turning on something that fires from a trigger is a decision,
            // and it belongs in the app where the triggers are visible.
            IsEnabled = false,
            VersionNote = $"Pushed from {Path.GetFileName(options.Target)}",
        }, ct);

        Output.Success($"Saved '{saved.Name}' as v{saved.VersionNumber}.");
        Output.Muted(saved.Id.ToString());

        return 0;
    }

    private static async Task<int> LogsAsync(string[] args, CancellationToken ct)
    {
        var options = CliOptions.Parse(args);
        Guid? scriptId = Guid.TryParse(options.Target, out var id) ? id : null;

        using var api = new ApiClient(CliConfig.Load());

        var runs = await api.RunsAsync(scriptId, options.Take ?? 20, ct);

        if (runs.Count == 0)
        {
            Output.Muted("No runs recorded.");
            return 0;
        }

        foreach (var run in runs)
        {
            Output.Write(
                $"{run.StartedAt.ToLocalTime():yyyy-MM-dd HH:mm}  " +
                $"{Output.Pad(Output.Status(run.Status.ToString()), 12)}" +
                $"{Output.Pad(run.ScriptName, 30)}" +
                $"{Output.Pad(run.TriggeredBy.ToString(), 10)}{run.DurationMs} ms");

            if (!string.IsNullOrWhiteSpace(run.Error)) Output.Muted("    " + run.Error);
        }

        return 0;
    }

    private static async Task<int> TemplatesAsync(string[] args, CancellationToken ct)
    {
        var keyword = args.FirstOrDefault(a => !a.StartsWith('-'));

        using var api = new ApiClient(CliConfig.Load());

        var templates = await api.TemplatesAsync(keyword, ct);

        if (templates.Count == 0)
        {
            Output.Muted("No template matches that.");
            return 0;
        }

        foreach (var group in templates.GroupBy(t => t.Category).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            Output.Heading(group.Key);

            foreach (var template in group)
            {
                Output.Write($"  {Output.Pad(template.Id, 30)}" +
                             $"{Output.Pad(LanguageName(template.Language), 11)}{template.Name}");
                Output.Muted($"    {template.Summary}");
            }
        }

        Output.Muted("Create one from the Templates tab in the web app, then 'vibedesk pull <id>'.");
        return 0;
    }

    /// <summary>
    /// Flips the one flag that decides whether triggers fire. Separate from push on purpose: pushing
    /// code and arming it are different decisions, and a CLI that did both at once would arm a
    /// scheduled script the moment someone saved a typo.
    /// </summary>
    private static async Task<int> SetEnabledAsync(string[] args, bool enabled, CancellationToken ct)
    {
        if (args.Length == 0 || !Guid.TryParse(args[0], out var id))
        {
            Output.Error($"Usage: vibedesk {(enabled ? "enable" : "disable")} <id>");
            return 1;
        }

        using var api = new ApiClient(CliConfig.Load());

        var detail = await api.GetAsync(id, ct);
        var script = detail.Script;

        if (script.IsEnabled == enabled)
        {
            Output.Muted($"'{script.Name}' is already {(enabled ? "enabled" : "disabled")}.");
            return 0;
        }

        // A full save with the code unchanged, which the server treats as an edit rather than a new
        // version — so arming a script does not litter its history.
        await api.SaveAsync(new ScriptInput
        {
            Id = script.Id,
            Name = script.Name,
            Description = script.Description,
            Language = script.Language,
            Code = detail.Code,
            Scopes = script.Scopes,
            AllowedHosts = script.AllowedHosts,
            TimeoutSeconds = script.TimeoutSeconds,
            IsEnabled = enabled,
        }, ct);

        Output.Success($"'{script.Name}' is now {(enabled ? "enabled" : "disabled")}.");

        if (enabled && script.TriggerCount > 0)
        {
            Output.Muted($"{script.TriggerCount} trigger(s) are now live.");
        }

        return 0;
    }
}

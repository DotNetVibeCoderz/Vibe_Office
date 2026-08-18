using VibeDesk.Application.Scripting;
using VibeDesk.Domain;

namespace VibeDesk.Cli;

/// <summary>The flags shared by run, push and logs, parsed once.</summary>
public sealed class CliOptions
{
    /// <summary>The first non-flag argument: a file path, a script id, or nothing.</summary>
    public string? Target { get; private set; }

    public Guid? Id { get; private set; }
    public string? Name { get; private set; }
    public int? Timeout { get; private set; }
    public int? Take { get; private set; }
    public ScriptScope Scopes { get; private set; } = ScriptScope.None;

    /// <summary>
    /// Whether --scope appeared at all. Distinct from an empty scope set: "grant nothing" and
    /// "leave the grants alone" are different instructions to push.
    /// </summary>
    public bool ScopesGiven { get; private set; }

    public List<string> Hosts { get; } = [];
    public Dictionary<string, string> Input { get; } = [];

    public static CliOptions Parse(string[] args)
    {
        var options = new CliOptions();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            string? Next() => i + 1 < args.Length ? args[++i] : null;

            switch (arg)
            {
                case "--scope" or "-s" when Next() is { } value:
                    options.ScopesGiven = true;
                    options.Scopes |= ParseScope(value);
                    break;

                case "--host" when Next() is { } value:
                    options.Hosts.Add(value.Trim());
                    break;

                case "--input" or "-i" when Next() is { } value:
                    var split = value.IndexOf('=');

                    if (split <= 0)
                    {
                        throw new CliException($"--input expects key=value, got '{value}'.");
                    }

                    options.Input[value[..split]] = value[(split + 1)..];
                    break;

                case "--timeout" or "-t" when Next() is { } value:
                    options.Timeout = int.TryParse(value, out var seconds) && seconds > 0
                        ? seconds
                        : throw new CliException($"--timeout expects a positive number of seconds, got '{value}'.");
                    break;

                case "--take" when Next() is { } value:
                    options.Take = int.TryParse(value, out var take) && take > 0 ? take : null;
                    break;

                case "--name" or "-n" when Next() is { } value:
                    options.Name = value;
                    break;

                case "--id" when Next() is { } value:
                    options.Id = Guid.TryParse(value, out var id)
                        ? id
                        : throw new CliException($"--id expects a script id, got '{value}'.");
                    break;

                default:
                    if (!arg.StartsWith('-')) options.Target ??= arg;
                    break;
            }
        }

        return options;
    }

    /// <summary>
    /// Accepts a scope name, or one of the two shorthands people actually reach for. Unknown names
    /// are refused with the full list rather than silently ignored — a typo that granted nothing
    /// would surface much later as a permission error inside the script.
    /// </summary>
    private static ScriptScope ParseScope(string name) => name.Trim().ToLowerInvariant() switch
    {
        "read" or "readonly" => ScriptScope.ReadOnly,
        "all" => ScriptScope.All,
        _ => Enum.TryParse<ScriptScope>(name.Trim(), ignoreCase: true, out var scope)
            ? scope
            : throw new CliException(
                $"Unknown scope '{name}'. Known: {string.Join(", ", Enum.GetNames<ScriptScope>())}."),
    };
}

namespace VibeDesk.Scripting;

/// <summary>
/// The <c>Scripting</c> section of appsettings. Every value here is a ceiling rather than a target:
/// a script that needs more than these is a script that should be doing less.
/// </summary>
public sealed class ScriptingOptions
{
    public const string SectionName = "Scripting";

    public bool Enabled { get; set; } = true;

    /// <summary>Wall-clock ceiling per run, unless the script asks for less.</summary>
    public int DefaultTimeoutSeconds { get; set; } = 30;

    /// <summary>Hard ceiling a script cannot raise, whatever it asks for.</summary>
    public int MaxTimeoutSeconds { get; set; } = 300;

    /// <summary>Memory ceiling for the JavaScript engine, in megabytes.</summary>
    public int MaxMemoryMb { get; set; } = 96;

    /// <summary>
    /// Statement ceiling for the JavaScript engine. Catches <c>while (true)</c> long before the
    /// timeout does, and with a far clearer error.
    /// </summary>
    public int MaxStatements { get; set; } = 5_000_000;

    /// <summary>Console output kept per run. Beyond this the log is truncated, not the run.</summary>
    public int MaxOutputChars { get; set; } = 64_000;

    /// <summary>Rows any single host call will return, so one query cannot exhaust memory.</summary>
    public int MaxRows { get; set; } = 50_000;

    /// <summary>Response size the HTTP host API will read, in kilobytes.</summary>
    public int MaxHttpResponseKb { get; set; } = 2_048;

    public int HttpTimeoutSeconds { get; set; } = 20;

    /// <summary>Outbound calls allowed in one run.</summary>
    public int MaxHttpCalls { get; set; } = 50;

    /// <summary>Runs kept per script; older ones are pruned. Audit value decays fast.</summary>
    public int MaxRunsPerScript { get; set; } = 200;

    /// <summary>How often the scheduler wakes to look for due scripts.</summary>
    public int SchedulerIntervalSeconds { get; set; } = 30;

    public bool EnableScheduler { get; set; } = true;

    /// <summary>Event-triggered runs allowed per minute across the workspace, as a runaway guard.</summary>
    public int MaxEventRunsPerMinute { get; set; } = 60;

    /// <summary>
    /// Extra assemblies C# scripts may reference, by simple name. The default set is deliberately
    /// small; adding one widens what a C# script can reach, so it is a deployment decision.
    /// </summary>
    public List<string> CSharpAssemblies { get; set; } = [];
}

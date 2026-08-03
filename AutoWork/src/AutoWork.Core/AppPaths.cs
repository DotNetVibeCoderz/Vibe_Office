namespace AutoWork.Core;

/// <summary>
/// Every file AutoWork owns lives under one root so the installer, the uninstaller and the
/// "reset everything" button all have a single thing to point at.
/// </summary>
public static class AppPaths
{
    private static readonly Lazy<string> _root = new(ResolveRoot, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Overridable for tests and for portable installs (AUTOWORK_HOME).</summary>
    public static string Root => _root.Value;

    public static string ConfigFile => Path.Combine(Root, "config.json");
    public static string SecretsFile => Path.Combine(Root, "secrets.json");
    public static string LogsDirectory => Path.Combine(Root, "logs");
    public static string ActionLogFile => Path.Combine(LogsDirectory, "actions.jsonl");
    public static string KnowledgeDirectory => Path.Combine(Root, "knowledge");
    public static string RunsDirectory => Path.Combine(Root, "runs");
    public static string ScreenshotsDirectory => Path.Combine(Root, "screenshots");

    /// <summary>
    /// Where the agent puts files when a job names no destination, and the folder the starter
    /// permission policy grants.
    ///
    /// Deliberately **not** under <see cref="Root"/>. Everything under Root is an AutoWork
    /// internal location, which <c>PathGuard</c> refuses unconditionally — so a workspace in
    /// there would be granted and then rejected on every single write, leaving a fresh install
    /// unable to produce a file. It also belongs to the user rather than to the app: uninstalling
    /// AutoWork must not take their work with it.
    /// </summary>
    public static string WorkspaceDirectory => _workspace.Value;

    private static readonly Lazy<string> _workspace = new(ResolveWorkspace, LazyThreadSafetyMode.ExecutionAndPublication);

    private static string ResolveWorkspace()
    {
        var overridden = Environment.GetEnvironmentVariable("AUTOWORK_WORKSPACE");
        if (!string.IsNullOrWhiteSpace(overridden)) return Path.GetFullPath(overridden);

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(documents))
            documents = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return Path.Combine(documents, "AutoWork");
    }

    private static string ResolveRoot()
    {
        var overridden = Environment.GetEnvironmentVariable("AUTOWORK_HOME");
        if (!string.IsNullOrWhiteSpace(overridden))
            return Path.GetFullPath(overridden);

        // ApplicationData maps to %APPDATA% on Windows, ~/.config on Linux,
        // and ~/Library/Application Support on macOS.
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(baseDir))
            baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".autowork");

        return Path.Combine(baseDir, "AutoWork");
    }

    /// <summary>Creates the directory tree. Safe to call repeatedly.</summary>
    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(KnowledgeDirectory);
        Directory.CreateDirectory(RunsDirectory);
        Directory.CreateDirectory(ScreenshotsDirectory);

        // Lives outside Root and belongs to the user, but must exist before it can be granted.
        Directory.CreateDirectory(WorkspaceDirectory);
    }
}

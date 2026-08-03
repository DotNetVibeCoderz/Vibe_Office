namespace AutoWork.Core.Security;

public enum FolderAccess
{
    /// <summary>Read and list only.</summary>
    Read = 0,
    /// <summary>Read, create, modify. Deletion still honours <see cref="PermissionPolicy.AllowDelete"/>.</summary>
    ReadWrite = 1,
}

/// <summary>A folder the agent is allowed to touch, and how far it may go inside it.</summary>
public sealed class PermissionRoot
{
    public string Path { get; set; } = "";
    public FolderAccess Access { get; set; } = FolderAccess.Read;

    /// <summary>Off by default: a granted root does not imply its subfolders until the user says so.</summary>
    public bool IncludeSubfolders { get; set; } = true;

    public override string ToString() => $"{Path} [{Access}]";
}

/// <summary>
/// The sandbox boundary, expressed as data so it can be reviewed, diffed and shipped.
/// Default state is deny-everything: an AutoWork install that has never been configured
/// cannot read or write a single file.
/// </summary>
public sealed class PermissionPolicy
{
    /// <summary>Folders the agent may touch. Empty means nothing is reachable.</summary>
    public List<PermissionRoot> Roots { get; set; } = [];

    /// <summary>
    /// Glob patterns refused even inside an allowed root. Supports *, ? and **.
    /// Seeded with the obvious credential traps.
    /// </summary>
    public List<string> DeniedPatterns { get; set; } =
    [
        "**/.ssh/**",
        "**/.aws/**",
        "**/.gnupg/**",
        "**/.git/config",
        "**/*.pem",
        "**/*.key",
        "**/*.pfx",
        "**/id_rsa*",
        "**/.env",
        "**/.env.*",
        "**/secrets.json",
        "**/AppData/Local/Microsoft/Credentials/**",
        "**/Library/Keychains/**",
    ];

    /// <summary>Permanently off-limits regardless of roots — OS and AutoWork's own state.</summary>
    public bool ProtectSystemDirectories { get; set; } = true;

    public bool AllowDelete { get; set; } = false;

    /// <summary>Deleted files are moved to a timestamped folder instead of being unlinked.</summary>
    public bool SoftDelete { get; set; } = true;

    public bool AllowShell { get; set; } = false;
    public bool ShellRequiresApproval { get; set; } = true;

    /// <summary>
    /// Whether MCP servers may be started at all. Off by default: a stdio server is a program
    /// launched with the user's full rights, so it is the same class of capability as the shell
    /// and gets the same explicit switch rather than riding in on "add a server".
    /// </summary>
    public bool AllowMcpServers { get; set; } = false;

    /// <summary>If non-empty, only commands whose executable matches one of these may run.</summary>
    public List<string> ShellAllowList { get; set; } = [];

    public bool AllowNetwork { get; set; } = true;

    /// <summary>If non-empty, outbound host allowlist for the web tools.</summary>
    public List<string> NetworkAllowList { get; set; } = [];

    public bool AllowScreenCapture { get; set; } = true;

    /// <summary>Synthetic mouse/keyboard. Off by default — it is the most invasive capability.</summary>
    public bool AllowInputControl { get; set; } = false;
    public bool InputControlRequiresApproval { get; set; } = true;

    /// <summary>Refuse to read anything larger than this into the model's context.</summary>
    public long MaxReadBytes { get; set; } = 32L * 1024 * 1024;

    /// <summary>Ceiling on files a single batch operation may touch.</summary>
    public int MaxBatchSize { get; set; } = 500;

    public static PermissionPolicy CreateDefault() => new();

    /// <summary>
    /// A conservative starting point for first run: read/write in a dedicated workspace folder
    /// only. The user widens this deliberately from Settings.
    /// </summary>
    public static PermissionPolicy CreateStarter(string workspacePath) => new()
    {
        Roots = [new PermissionRoot { Path = workspacePath, Access = FolderAccess.ReadWrite, IncludeSubfolders = true }],
        AllowDelete = true,
        SoftDelete = true,
    };
}

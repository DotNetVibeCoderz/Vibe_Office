using AutoWork.Core;
using AutoWork.Core.Security;

namespace AutoWork.Tests;

/// <summary>
/// The sandbox is the load-bearing safety claim of the whole product: "AutoWork can only reach
/// the folders you grant". These tests exist to make that claim falsifiable, so they are
/// written as attempted escapes rather than as happy paths.
/// </summary>
public sealed class SandboxTests : IDisposable
{
    private readonly string _root;
    private readonly string _granted;
    private readonly string _offLimits;
    private readonly PathGuard _guard;

    public SandboxTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "autowork-sandbox", Guid.NewGuid().ToString("n")[..8]);
        _granted = Path.Combine(_root, "granted");
        _offLimits = Path.Combine(_root, "private");

        Directory.CreateDirectory(_granted);
        Directory.CreateDirectory(_offLimits);
        File.WriteAllText(Path.Combine(_offLimits, "secrets.txt"), "not for the agent");

        _guard = new PathGuard(new PermissionPolicy
        {
            Roots = [new PermissionRoot { Path = _granted, Access = FolderAccess.ReadWrite, IncludeSubfolders = true }],
            AllowDelete = true,
        });
    }

    [Fact]
    public void Reads_inside_a_granted_folder_are_allowed()
    {
        var target = Path.Combine(_granted, "notes.txt");
        File.WriteAllText(target, "hello");

        var resolved = _guard.EnsureReadable(target);

        Assert.Equal(Path.GetFullPath(target), resolved);
    }

    [Fact]
    public void Relative_traversal_out_of_the_granted_folder_is_refused()
    {
        var escape = Path.Combine(_granted, "..", "private", "secrets.txt");

        var error = Assert.Throws<SandboxViolationException>(() => _guard.EnsureReadable(escape));

        Assert.Equal("outside-roots", error.Reason);
    }

    [Fact]
    public void An_absolute_path_outside_every_root_is_refused()
    {
        var error = Assert.Throws<SandboxViolationException>(
            () => _guard.EnsureReadable(Path.Combine(_offLimits, "secrets.txt")));

        Assert.Equal("outside-roots", error.Reason);
    }

    [Fact]
    public void A_sibling_folder_with_the_granted_folder_as_a_name_prefix_is_refused()
    {
        // "granted-other" must not be treated as living inside "granted" — a plain
        // StartsWith containment check would wave this straight through.
        var sibling = Path.Combine(_root, "granted-other");
        Directory.CreateDirectory(sibling);

        var error = Assert.Throws<SandboxViolationException>(
            () => _guard.EnsureReadable(Path.Combine(sibling, "file.txt")));

        Assert.Equal("outside-roots", error.Reason);
    }

    [Fact]
    public void Writing_to_a_read_only_root_is_refused()
    {
        var readOnly = Path.Combine(_root, "reference");
        Directory.CreateDirectory(readOnly);

        var guard = new PathGuard(new PermissionPolicy
        {
            Roots = [new PermissionRoot { Path = readOnly, Access = FolderAccess.Read, IncludeSubfolders = true }],
        });

        var target = Path.Combine(readOnly, "report.txt");

        Assert.Equal(Path.GetFullPath(target), guard.EnsureReadable(target));

        var error = Assert.Throws<SandboxViolationException>(() => guard.EnsureWritable(target));
        Assert.Equal("read-only", error.Reason);
    }

    [Fact]
    public void A_denied_pattern_wins_even_inside_a_granted_folder()
    {
        var guard = new PathGuard(new PermissionPolicy
        {
            Roots = [new PermissionRoot { Path = _granted, Access = FolderAccess.ReadWrite, IncludeSubfolders = true }],
            DeniedPatterns = ["**/*.pem", "**/.env"],
        });

        var key = Path.Combine(_granted, "deploy", "server.pem");

        var error = Assert.Throws<SandboxViolationException>(() => guard.EnsureReadable(key));
        Assert.Equal("denied-pattern", error.Reason);
    }

    [Fact]
    public void Deleting_is_refused_when_the_policy_forbids_it()
    {
        var guard = new PathGuard(new PermissionPolicy
        {
            Roots = [new PermissionRoot { Path = _granted, Access = FolderAccess.ReadWrite, IncludeSubfolders = true }],
            AllowDelete = false,
        });

        var target = Path.Combine(_granted, "disposable.txt");
        File.WriteAllText(target, "x");

        var error = Assert.Throws<SandboxViolationException>(() => guard.EnsureDeletable(target));
        Assert.Equal("delete-disabled", error.Reason);
    }

    [Fact]
    public void Nothing_is_reachable_before_any_folder_is_granted()
    {
        var guard = new PathGuard(new PermissionPolicy());

        var error = Assert.Throws<SandboxViolationException>(
            () => guard.EnsureReadable(Path.Combine(_granted, "notes.txt")));

        Assert.Equal("outside-roots", error.Reason);
    }

    [Fact]
    public void AutoWorks_own_data_folder_is_never_reachable_even_if_granted()
    {
        // The secret store lives here. Granting it must not expose it.
        var guard = new PathGuard(new PermissionPolicy
        {
            Roots = [new PermissionRoot { Path = AutoWork.Core.AppPaths.Root, Access = FolderAccess.ReadWrite }],
        });

        var error = Assert.Throws<SandboxViolationException>(
            () => guard.EnsureReadable(Path.Combine(AutoWork.Core.AppPaths.Root, "secrets.json")));

        Assert.Equal("protected-directory", error.Reason);
    }

    [Fact]
    public void A_symlink_pointing_out_of_the_granted_folder_is_refused()
    {
        var linkPath = Path.Combine(_granted, "shortcut");

        try
        {
            Directory.CreateSymbolicLink(linkPath, _offLimits);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            // Windows needs Developer Mode or elevation to create symlinks. Skipping keeps the
            // suite green on a stock machine; the check itself is exercised on CI/Linux.
            Assert.Skip("Creating symbolic links is not permitted in this environment.");
            return;
        }

        // Resolving the link is the whole point: without it, this path looks like it is
        // comfortably inside the granted folder.
        var error = Assert.Throws<SandboxViolationException>(
            () => _guard.EnsureReadable(Path.Combine(linkPath, "secrets.txt")));

        Assert.Equal("outside-roots", error.Reason);
    }

    [Fact]
    public void Subfolders_can_be_excluded_from_a_grant()
    {
        var guard = new PathGuard(new PermissionPolicy
        {
            Roots = [new PermissionRoot { Path = _granted, Access = FolderAccess.ReadWrite, IncludeSubfolders = false }],
        });

        Assert.Equal(Path.GetFullPath(Path.Combine(_granted, "top.txt")),
            guard.EnsureReadable(Path.Combine(_granted, "top.txt")));

        Assert.Throws<SandboxViolationException>(
            () => guard.EnsureReadable(Path.Combine(_granted, "nested", "deep.txt")));
    }

    [Theory]
    [InlineData("**/.ssh/**", "/home/user/.ssh/id_rsa", true)]
    [InlineData("**/.ssh/**", "/home/user/projects/notes.md", false)]
    [InlineData("**/*.pem", "/srv/certs/server.pem", true)]
    [InlineData("**/.env", "/app/.env", true)]
    [InlineData("**/.env", "/app/.environment", false)]
    [InlineData("*.txt", "notes.txt", true)]
    [InlineData("*.txt", "sub/notes.txt", false)]
    public void Glob_patterns_match_the_way_the_permission_UI_implies(string pattern, string path, bool expected) =>
        Assert.Equal(expected, Glob.IsMatch(path, pattern));

    /// <summary>
    /// The starter policy grants one folder and one folder only, so if the guard refuses that
    /// folder a fresh install cannot write a single file — and every refusal blames the path the
    /// model chose rather than the configuration that shipped.
    ///
    /// This is not hypothetical: the default workspace once sat inside AutoWork's own state
    /// directory, which is protected unconditionally, so the out-of-the-box grant was dead.
    /// </summary>
    [Fact]
    public void The_folder_the_starter_policy_grants_is_one_the_guard_actually_permits()
    {
        Directory.CreateDirectory(AppPaths.WorkspaceDirectory);

        var policy = PermissionPolicy.CreateStarter(AppPaths.WorkspaceDirectory);
        var guard = new PathGuard(policy);

        var target = Path.Combine(AppPaths.WorkspaceDirectory, "report.docx");

        var canonical = guard.EnsureWritable(target);

        Assert.Equal(Path.GetFullPath(target), canonical);
    }

    /// <summary>
    /// Stated as its own fact because it is the reason for the rule above: anything under the
    /// app's own root holds the secret store and must stay unreachable, so the user's working
    /// folder cannot live there.
    /// </summary>
    [Fact]
    public void The_default_workspace_is_not_inside_AutoWorks_own_state_directory()
    {
        var workspace = Path.GetFullPath(AppPaths.WorkspaceDirectory);
        var root = Path.GetFullPath(AppPaths.Root);

        Assert.False(
            workspace.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
            $"The workspace {workspace} sits inside the protected root {root}; PathGuard will refuse every write to it.");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

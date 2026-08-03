using System.Runtime.InteropServices;

namespace AutoWork.Core.Security;

/// <summary>
/// The single chokepoint every filesystem tool goes through. Nothing in AutoWork.Tools
/// touches a path it has not handed to this class first.
///
/// The checks run in this order, and all of them must pass:
///   1. Canonicalise, resolving symlinks — otherwise a link inside an allowed folder is a
///      free pass to anywhere on disk.
///   2. Reject OS and AutoWork-internal directories outright.
///   3. Require containment in an allowed root with sufficient access.
///   4. Reject denied glob patterns, even inside an allowed root.
/// </summary>
public sealed class PathGuard
{
    private const int MaxLinkDepth = 40;

    private volatile PermissionPolicy _policy;

    public PathGuard(PermissionPolicy policy) => _policy = policy;

    public PermissionPolicy Policy
    {
        get => _policy;
        set => _policy = value ?? throw new ArgumentNullException(nameof(value));
    }

    private static StringComparison PathComparison =>
        // Linux filesystems are case-sensitive; Windows and macOS are conventionally not.
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    /// <summary>Canonical path if readable; throws <see cref="SandboxViolationException"/> otherwise.</summary>
    public string EnsureReadable(string path) => Ensure(path, FolderAccess.Read);

    /// <summary>Canonical path if writable; throws otherwise.</summary>
    public string EnsureWritable(string path) => Ensure(path, FolderAccess.ReadWrite);

    /// <summary>Canonical path if deletable. Deletion is a separate switch from writing.</summary>
    public string EnsureDeletable(string path)
    {
        var canonical = Ensure(path, FolderAccess.ReadWrite);
        if (!_policy.AllowDelete)
            throw new SandboxViolationException(
                $"Deleting is turned off in permissions. Turn on \"Allow delete\" to remove {Describe(canonical)}.",
                canonical, "delete-disabled");
        return canonical;
    }

    private string Ensure(string path, FolderAccess required)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new SandboxViolationException("No path was given.", path, "empty");

        var canonical = Canonicalize(path);

        if (!IsAllowed(canonical, required, out var reason, out var detail))
            throw new SandboxViolationException(detail, canonical, reason);

        return canonical;
    }

    /// <summary>Non-throwing form, for UI affordances that want to grey things out.</summary>
    public bool IsAllowed(string canonicalPath, FolderAccess required, out string reason, out string detail)
    {
        if (_policy.ProtectSystemDirectories && IsProtected(canonicalPath))
        {
            reason = "protected-directory";
            detail = $"{Describe(canonicalPath)} is a system or AutoWork-internal location and is never accessible.";
            return false;
        }

        var root = FindContainingRoot(canonicalPath);
        if (root is null)
        {
            reason = "outside-roots";
            detail = _policy.Roots.Count == 0
                ? $"No folders have been granted yet, so {Describe(canonicalPath)} is out of reach. Add a folder in Settings › Permissions."
                : $"{Describe(canonicalPath)} is outside every granted folder. Add it in Settings › Permissions to allow access.";
            return false;
        }

        if (required == FolderAccess.ReadWrite && root.Access != FolderAccess.ReadWrite)
        {
            reason = "read-only";
            detail = $"{Describe(root.Path)} is granted as read-only. Change it to read/write to modify {Describe(canonicalPath)}.";
            return false;
        }

        if (Glob.IsMatchAny(canonicalPath, _policy.DeniedPatterns))
        {
            reason = "denied-pattern";
            detail = $"{Describe(canonicalPath)} matches a blocked pattern in Settings › Permissions.";
            return false;
        }

        reason = "";
        detail = "";
        return true;
    }

    private PermissionRoot? FindContainingRoot(string canonicalPath)
    {
        PermissionRoot? best = null;
        var bestLength = -1;

        foreach (var root in _policy.Roots)
        {
            if (string.IsNullOrWhiteSpace(root.Path)) continue;

            string canonicalRoot;
            try { canonicalRoot = Canonicalize(root.Path); }
            catch (SandboxViolationException) { continue; }

            if (!Contains(canonicalRoot, canonicalPath, root.IncludeSubfolders)) continue;

            // Most specific root wins, so a read-only sub-root can narrow a read/write parent.
            if (canonicalRoot.Length > bestLength)
            {
                best = root;
                bestLength = canonicalRoot.Length;
            }
        }

        return best;
    }

    /// <summary>
    /// Containment test with a segment boundary, so "/home/a/docs2" is not inside "/home/a/docs".
    /// </summary>
    private static bool Contains(string root, string candidate, bool includeSubfolders)
    {
        root = TrimTrailingSeparator(root);
        candidate = TrimTrailingSeparator(candidate);

        if (candidate.Equals(root, PathComparison))
            return true;

        if (candidate.Length <= root.Length + 1)
            return false;

        if (!candidate.StartsWith(root, PathComparison))
            return false;

        var boundary = candidate[root.Length];
        if (boundary != Path.DirectorySeparatorChar && boundary != Path.AltDirectorySeparatorChar)
            return false;

        if (includeSubfolders)
            return true;

        // Direct children only.
        var remainder = candidate[(root.Length + 1)..];
        return remainder.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) < 0;
    }

    private static string TrimTrailingSeparator(string path)
    {
        if (path.Length <= 1) return path;

        // Keep the separator on a filesystem root: "C:\" and "/" must stay as they are.
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return trimmed.Length == 0 || (trimmed.Length == 2 && trimmed[1] == ':') ? path : trimmed;
    }

    /// <summary>
    /// Full path with every symlink along the way resolved. Works for paths that do not exist
    /// yet — a file about to be created still has its parent chain resolved.
    /// </summary>
    public static string Canonicalize(string path)
    {
        string full;
        try
        {
            full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new SandboxViolationException($"\"{path}\" is not a usable path.", path, "malformed");
        }

        // Split into root + components, then rebuild resolving links as we descend.
        var components = new List<string>();
        var cursor = full;
        while (true)
        {
            var parent = Path.GetDirectoryName(cursor);
            if (string.IsNullOrEmpty(parent)) break;
            components.Add(Path.GetFileName(cursor));
            cursor = parent;
        }
        components.Reverse();

        var resolved = cursor;
        foreach (var component in components)
        {
            resolved = Path.Combine(resolved, component);

            var target = ResolveLink(resolved, path);
            if (target is null) continue;

            // A link target may be relative to the link's own directory.
            var containing = Path.GetDirectoryName(resolved);
            resolved = containing is null
                ? Path.GetFullPath(target)
                : Path.GetFullPath(target, containing);
        }

        return resolved;
    }

    private static string? ResolveLink(string path, string originalForErrors)
    {
        try
        {
            // Only one of these reports a target; the other returns null for a non-link.
            var info = Directory.Exists(path)
                ? Directory.ResolveLinkTarget(path, returnFinalTarget: true)
                : File.Exists(path)
                    ? File.ResolveLinkTarget(path, returnFinalTarget: true)
                    : null;

            return info?.FullName;
        }
        catch (IOException)
        {
            // Cyclic or unresolvable link chain. Refusing is the safe answer.
            throw new SandboxViolationException(
                $"\"{originalForErrors}\" points through a broken or circular link.", originalForErrors, "bad-link");
        }
        catch (UnauthorizedAccessException)
        {
            throw new SandboxViolationException(
                $"\"{originalForErrors}\" cannot be inspected — the OS denied access.", originalForErrors, "os-denied");
        }
    }

    /// <summary>
    /// Locations that stay off-limits no matter what the user granted: OS internals, and
    /// AutoWork's own state (which holds the secret store).
    /// </summary>
    private static bool IsProtected(string canonicalPath)
    {
        foreach (var dir in ProtectedRoots.Value)
        {
            if (Contains(dir, canonicalPath, includeSubfolders: true))
                return true;
        }
        return false;
    }

    private static readonly Lazy<string[]> ProtectedRoots = new(() =>
    {
        var roots = new List<string> { AppPaths.Root };

        void Add(Environment.SpecialFolder folder)
        {
            var p = Environment.GetFolderPath(folder);
            if (!string.IsNullOrWhiteSpace(p)) roots.Add(p);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Add(Environment.SpecialFolder.Windows);
            Add(Environment.SpecialFolder.System);
            Add(Environment.SpecialFolder.SystemX86);
            Add(Environment.SpecialFolder.ProgramFiles);
            Add(Environment.SpecialFolder.ProgramFilesX86);
        }
        else
        {
            roots.AddRange(["/etc", "/bin", "/sbin", "/usr/bin", "/usr/sbin", "/boot", "/dev", "/proc", "/sys", "/var/run"]);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                roots.AddRange(["/System", "/Library/Keychains", "/private/etc"]);
        }

        return roots
            .Select(r => { try { return TrimTrailingSeparator(Path.GetFullPath(r)); } catch { return r; } })
            .Distinct(OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Trims a path for display so error messages and logs stay readable.</summary>
    public static string Describe(string path)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home) && path.StartsWith(home, PathComparison))
            return "~" + path[home.Length..];
        return path;
    }
}

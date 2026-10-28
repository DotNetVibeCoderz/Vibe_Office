// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ModelContextProtocol;

namespace OfficeNet.Mcp;

/// <summary>
/// The one directory the server may read and write, and the rule that keeps every path inside it.
/// </summary>
/// <remarks>
/// <para>
/// An MCP server does what a language model asks, and a model can be talked into asking for
/// anything — by the user, or by text inside a document it was told to read. So the boundary is
/// enforced here, in code, not by trusting the model to stay polite: every path a tool receives is
/// resolved against the root and rejected if the result lands outside it.
/// </para>
/// <para>
/// Resolving means <see cref="Path.GetFullPath(string, string)"/> first, which folds <c>..</c> away,
/// then a prefix check that includes the trailing separator — <c>C:\work</c> must not admit
/// <c>C:\workshop</c>. A symbolic link inside the root that points outside it is refused too, by
/// following existing links on the way down and checking where they really go.
/// </para>
/// </remarks>
public sealed class Workspace
{
    private readonly string _rootWithSeparator;

    public Workspace(string root, bool readOnly = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        ReadOnly = readOnly;
        _rootWithSeparator = Root + Path.DirectorySeparatorChar;

        Directory.CreateDirectory(Root);
    }

    /// <summary>The workspace root, absolute and without a trailing separator.</summary>
    public string Root { get; }

    /// <summary>When true, every tool that would create or change a file refuses.</summary>
    public bool ReadOnly { get; }

    /// <summary>Resolves a path that must already exist as a file.</summary>
    public string Existing(string path)
    {
        var full = Resolve(path);

        if (!File.Exists(full))
        {
            throw new McpException($"'{path}' does not exist in the workspace.");
        }

        return full;
    }

    /// <summary>Resolves a path the tool is about to write, creating its directory.</summary>
    public string ForWriting(string path)
    {
        if (ReadOnly)
        {
            throw new McpException(
                "The server was started with --read-only; it cannot create or change files.");
        }

        var full = Resolve(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        return full;
    }

    /// <summary>Resolves a directory path, creating it when <paramref name="create"/> is set.</summary>
    public string ResolveDirectory(string path, bool create)
    {
        if (create && ReadOnly)
        {
            throw new McpException(
                "The server was started with --read-only; it cannot create or change files.");
        }

        var full = Resolve(path, allowRoot: true);

        if (create)
        {
            Directory.CreateDirectory(full);
        }
        else if (!Directory.Exists(full))
        {
            throw new McpException($"The directory '{path}' does not exist in the workspace.");
        }

        return full;
    }

    /// <summary>
    /// Reads the pictures an HTML or Markdown document refers to, from inside the workspace only.
    /// </summary>
    /// <remarks>
    /// The converters would otherwise open an <c>img</c>'s path themselves, and a <c>src</c> of
    /// <c>../../secret.png</c> or an absolute path would copy a file from outside the workspace into
    /// the document. They are run with file access off and this as their only way in: relative to
    /// <paramref name="baseDirectory"/>, through <see cref="Existing"/>, and never over the network.
    /// </remarks>
    public Func<string, byte[]?> ImageReader(string baseDirectory) => source =>
    {
        var remote = source.StartsWith("//", StringComparison.Ordinal) ||
                     (source.Contains("://", StringComparison.Ordinal) &&
                      !source.StartsWith("file:", StringComparison.OrdinalIgnoreCase));

        if (remote)
        {
            return null;
        }

        try
        {
            var path = source.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ? new Uri(source).LocalPath : source;
            return File.ReadAllBytes(Existing(Path.GetFullPath(Uri.UnescapeDataString(path), baseDirectory)));
        }
        catch (Exception ex) when (ex is McpException or IOException or UnauthorizedAccessException or
                                       ArgumentException or NotSupportedException or UriFormatException)
        {
            return null;
        }
    };

    /// <summary>A path relative to the root, with forward slashes, for showing back to the client.</summary>
    public string Relative(string fullPath) =>
        Path.GetRelativePath(Root, fullPath).Replace('\\', '/');

    /// <summary>Resolves against the root and refuses anything that escapes it.</summary>
    public string Resolve(string path, bool allowRoot = false)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new McpException("A path is required.");
        }

        if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            throw new McpException($"'{path}' contains characters that are not allowed in a path.");
        }

        var full = Path.GetFullPath(path, Root);

        if (!IsInside(full, allowRoot))
        {
            throw new McpException(
                $"'{path}' is outside the workspace. Paths are relative to the workspace root, and " +
                "the server never reads or writes anywhere else.");
        }

        // Walk down from the root: a link anywhere on the way may lead outside.
        var current = Root;

        foreach (var segment in Path.GetRelativePath(Root, full).Split(Path.DirectorySeparatorChar))
        {
            if (segment is "." or "")
            {
                continue;
            }

            current = Path.Combine(current, segment);
            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);

            if (info.Exists && info.LinkTarget is not null &&
                info.ResolveLinkTarget(returnFinalTarget: true) is { } target &&
                !IsInside(Path.GetFullPath(target.FullName), allowRoot: true))
            {
                throw new McpException($"'{path}' goes through a link that leads outside the workspace.");
            }
        }

        return full;
    }

    private bool IsInside(string full, bool allowRoot)
    {
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return full.StartsWith(_rootWithSeparator, comparison) ||
               (allowRoot && string.Equals(Path.TrimEndingDirectorySeparator(full), Root, comparison));
    }
}

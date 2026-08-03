using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using AutoWork.Core;
using AutoWork.Core.Agents;
using AutoWork.Core.Security;
using Microsoft.Extensions.AI;

namespace AutoWork.Tools;

/// <summary>
/// Direct local file access — the capability the whole product is built around, and the one
/// that most needs a leash. Every path here goes through <see cref="PathGuard"/> first, and
/// anything that modifies or removes data asks for approval.
/// </summary>
public sealed class FileTools : ToolSetBase, IToolProvider
{
    public FileTools(ToolContext context) : base(context) { }

    protected override AgentOrgan Organ => AgentOrgan.Hands;

    public string Name => "Files";

    public IEnumerable<ToolDescriptor> GetTools(ToolContext context)
    {
        var tools = new FileTools(context);

        yield return Describe(AIFunctionFactory.Create(tools.ListAsync, "files_list",
            "List files and folders in a directory. Use this before acting on files so you know what is actually there."),
            ToolRisk.Safe);

        yield return Describe(AIFunctionFactory.Create(tools.InfoAsync, "files_info",
            "Get size, timestamps and type for one file or folder."), ToolRisk.Safe);

        yield return Describe(AIFunctionFactory.Create(tools.ReadAsync, "files_read",
            "Read a text file's contents. Binary files are rejected — use the document or data tools for those."),
            ToolRisk.Safe);

        yield return Describe(AIFunctionFactory.Create(tools.SearchAsync, "files_search",
            "Find files by name and optionally by the text inside them, under a folder."), ToolRisk.Safe);

        yield return Describe(AIFunctionFactory.Create(tools.WriteAsync, "files_write",
            "Write a text file, creating parent folders as needed."),
            ToolRisk.Write, ApprovalKind.WriteFiles);

        yield return Describe(AIFunctionFactory.Create(tools.AppendAsync, "files_append",
            "Append text to the end of a file, creating it if missing."),
            ToolRisk.Write, ApprovalKind.WriteFiles);

        yield return Describe(AIFunctionFactory.Create(tools.MakeDirectoryAsync, "files_make_directory",
            "Create a folder, including any missing parents."), ToolRisk.Write, ApprovalKind.WriteFiles);

        yield return Describe(AIFunctionFactory.Create(tools.MoveAsync, "files_move",
            "Move or rename a single file or folder."), ToolRisk.Write, ApprovalKind.WriteFiles);

        yield return Describe(AIFunctionFactory.Create(tools.CopyAsync, "files_copy",
            "Copy a file or an entire folder."), ToolRisk.Write, ApprovalKind.WriteFiles);

        yield return Describe(AIFunctionFactory.Create(tools.DeleteAsync, "files_delete",
            "Delete a file or folder. Deleted items go to AutoWork's recycle folder unless soft delete is off."),
            ToolRisk.Destructive, ApprovalKind.DeleteFiles);

        yield return Describe(AIFunctionFactory.Create(tools.BatchRenameAsync, "files_batch_rename",
            "Rename many files at once using a regular expression. Always call with dryRun=true first and show the user the preview."),
            ToolRisk.Write, ApprovalKind.WriteFiles);

        yield return Describe(AIFunctionFactory.Create(tools.OrganizeAsync, "files_organize",
            "Sort a folder's files into subfolders by extension, date or type. Call with dryRun=true first."),
            ToolRisk.Write, ApprovalKind.WriteFiles);
    }

    private static ToolDescriptor Describe(AIFunction function, ToolRisk risk,
        ApprovalKind approval = ApprovalKind.Other) => new()
    {
        Function = function,
        Organ = AgentOrgan.Hands,
        Risk = risk,
        Category = "Files",
        ApprovalKind = approval,
    };

    // ── Reading ───────────────────────────────────────────────────────────────────────────

    [Description("List the contents of a folder.")]
    private Task<string> ListAsync(
        [Description("Folder to list.")] string path,
        [Description("Optional filename filter such as *.pdf. Defaults to everything.")] string pattern = "*",
        [Description("Include files in subfolders.")] bool recursive = false,
        [Description("Maximum entries to return.")] int limit = 200)
    {
        var target = Locate(path);

        return GuardedAsync("files.list", $"List {PathGuard.Describe(target)}", () =>
        {
            var canonical = Guard.EnsureReadable(target);
            if (!Directory.Exists(canonical))
                return Failed($"{PathGuard.Describe(canonical)} is not a folder.");

            var search = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var builder = new StringBuilder();
            var shown = 0;
            var skipped = 0;

            foreach (var entry in EnumerateSafely(canonical, string.IsNullOrWhiteSpace(pattern) ? "*" : pattern, search))
            {
                if (shown >= limit) { skipped++; continue; }

                // Silently omit entries the policy blocks rather than advertising them.
                if (!Guard.IsAllowed(entry, FolderAccess.Read, out _, out _)) continue;

                var info = new FileInfo(entry);
                var isDirectory = Directory.Exists(entry);
                var relative = Path.GetRelativePath(canonical, entry);

                builder.Append(isDirectory ? "DIR  " : "FILE ")
                       .Append(relative.PadRight(52))
                       .Append(isDirectory ? "" : Human(info.Length).PadLeft(10))
                       .Append("  ")
                       .AppendLine(info.LastWriteTime.ToString("yyyy-MM-dd HH:mm"));
                shown++;
            }

            if (shown == 0) return Ok($"{PathGuard.Describe(canonical)} is empty (or nothing matched \"{pattern}\").");

            if (skipped > 0)
                builder.AppendLine($"… {skipped} more entries not shown. Narrow the pattern or raise the limit.");

            return Ok($"{shown} entries in {PathGuard.Describe(canonical)}:\n{builder}");
        }, [target]);
    }

    /// <summary>Enumeration that skips folders the OS refuses instead of aborting the whole listing.</summary>
    private static IEnumerable<string> EnumerateSafely(string root, string pattern, SearchOption option)
    {
        var enumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = option == SearchOption.AllDirectories,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.System,
        };

        return Directory.EnumerateFileSystemEntries(root, pattern, enumerationOptions);
    }

    [Description("Describe one file or folder.")]
    private Task<string> InfoAsync([Description("Path to inspect.")] string path)
    {
        var target = Locate(path);

        return GuardedAsync("files.info", $"Inspect {PathGuard.Describe(target)}", () =>
        {
            var canonical = Guard.EnsureReadable(target);

            if (Directory.Exists(canonical))
            {
                var directory = new DirectoryInfo(canonical);
                var fileCount = Directory.EnumerateFiles(canonical, "*",
                    new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }).Count();

                return Ok($"Folder {PathGuard.Describe(canonical)}\n" +
                          $"Contains {fileCount:N0} files\nModified {directory.LastWriteTime:yyyy-MM-dd HH:mm}");
            }

            if (!File.Exists(canonical)) return Failed($"{PathGuard.Describe(canonical)} does not exist.");

            var file = new FileInfo(canonical);
            return Ok($"File {PathGuard.Describe(canonical)}\n" +
                      $"Size {Human(file.Length)}\nModified {file.LastWriteTime:yyyy-MM-dd HH:mm}\n" +
                      $"Created {file.CreationTime:yyyy-MM-dd HH:mm}\nRead-only {file.IsReadOnly}");
        }, [target]);
    }

    [Description("Read a text file.")]
    private Task<string> ReadAsync(
        [Description("File to read.")] string path,
        [Description("Maximum characters to return.")] int maxCharacters = 12000)
    {
        var target = Locate(path);

        return GuardedAsync("files.read", $"Read {PathGuard.Describe(target)}", async () =>
        {
            var canonical = Guard.EnsureReadable(target);
            if (!File.Exists(canonical)) return Failed($"{PathGuard.Describe(canonical)} does not exist.");

            var info = new FileInfo(canonical);
            if (info.Length > Guard.Policy.MaxReadBytes)
                return Refused($"{PathGuard.Describe(canonical)} is {Human(info.Length)}, over the {Human(Guard.Policy.MaxReadBytes)} read limit.");

            if (await LooksBinaryAsync(canonical).ConfigureAwait(false))
                return Refused($"{PathGuard.Describe(canonical)} looks like a binary file. Use data_extract_pdf, data_read_csv or the image tools instead.");

            var text = await File.ReadAllTextAsync(canonical).ConfigureAwait(false);
            return Ok(Cap(text, maxCharacters));
        }, [target]);
    }

    /// <summary>A NUL byte in the first 8 KB is the pragmatic binary test.</summary>
    private static async Task<bool> LooksBinaryAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        var buffer = new byte[Math.Min(8192, stream.Length)];
        var read = await stream.ReadAsync(buffer).ConfigureAwait(false);

        for (var i = 0; i < read; i++)
            if (buffer[i] == 0) return true;

        return false;
    }

    [Description("Search for files by name, and optionally by their contents.")]
    private Task<string> SearchAsync(
        [Description("Folder to search under.")] string root,
        [Description("Filename pattern such as *.docx.")] string pattern = "*",
        [Description("Optional text to find inside matching files.")] string? containingText = null,
        [Description("Maximum results.")] int limit = 50)
    {
        var target = Locate(root);

        return GuardedAsync("files.search", $"Search {PathGuard.Describe(target)} for \"{pattern}\"", async () =>
        {
            var canonical = Guard.EnsureReadable(target);
            if (!Directory.Exists(canonical)) return Failed($"{PathGuard.Describe(canonical)} is not a folder.");

            var results = new List<string>();

            foreach (var file in EnumerateSafely(canonical, pattern, SearchOption.AllDirectories))
            {
                if (results.Count >= limit) break;
                if (Directory.Exists(file)) continue;
                if (!Guard.IsAllowed(file, FolderAccess.Read, out _, out _)) continue;

                if (!string.IsNullOrEmpty(containingText))
                {
                    var info = new FileInfo(file);
                    if (info.Length > 8L * 1024 * 1024) continue;
                    if (await LooksBinaryAsync(file).ConfigureAwait(false)) continue;

                    var content = await File.ReadAllTextAsync(file).ConfigureAwait(false);
                    if (!content.Contains(containingText, StringComparison.OrdinalIgnoreCase)) continue;
                }

                results.Add(Path.GetRelativePath(canonical, file));
            }

            return results.Count == 0
                ? Ok($"Nothing under {PathGuard.Describe(canonical)} matched.")
                : Ok($"{results.Count} matches under {PathGuard.Describe(canonical)}:\n" + string.Join('\n', results));
        }, [target]);
    }

    // ── Writing ───────────────────────────────────────────────────────────────────────────

    [Description("Write a text file.")]
    private Task<string> WriteAsync(
        [Description("File to write.")] string path,
        [Description("Full contents to write.")] string content,
        [Description("Overwrite the file if it already exists.")] bool overwrite = false)
    {
        var target = Locate(path);

        return GuardedAsync("files.write", $"Write {PathGuard.Describe(target)}", async () =>
        {
            var canonical = Guard.EnsureWritable(target);

            if (File.Exists(canonical) && !overwrite)
                return Refused($"{PathGuard.Describe(canonical)} already exists. Call again with overwrite=true if replacing it is intended.");

            Directory.CreateDirectory(Path.GetDirectoryName(canonical)!);
            await File.WriteAllTextAsync(canonical, content).ConfigureAwait(false);

            return Ok($"Wrote {Human(new FileInfo(canonical).Length)} to {PathGuard.Describe(canonical)}.");
        },
        [target], ApprovalKind.WriteFiles,
        $"{content.Length:N0} characters → {PathGuard.Describe(target)}");
    }

    [Description("Append text to a file.")]
    private Task<string> AppendAsync(
        [Description("File to append to.")] string path,
        [Description("Text to add at the end.")] string content)
    {
        var target = Locate(path);

        return GuardedAsync("files.append", $"Append to {PathGuard.Describe(target)}", async () =>
        {
            var canonical = Guard.EnsureWritable(target);
            Directory.CreateDirectory(Path.GetDirectoryName(canonical)!);
            await File.AppendAllTextAsync(canonical, content).ConfigureAwait(false);
            return Ok($"Appended {content.Length:N0} characters to {PathGuard.Describe(canonical)}.");
        },
        [target], ApprovalKind.WriteFiles);
    }

    [Description("Create a folder.")]
    private Task<string> MakeDirectoryAsync([Description("Folder to create.")] string path)
    {
        var target = Locate(path);

        return GuardedAsync("files.mkdir", $"Create folder {PathGuard.Describe(target)}", () =>
        {
            var canonical = Guard.EnsureWritable(target);
            if (Directory.Exists(canonical)) return Ok($"{PathGuard.Describe(canonical)} already exists.");

            Directory.CreateDirectory(canonical);
            return Ok($"Created {PathGuard.Describe(canonical)}.");
        },
        [target], ApprovalKind.WriteFiles);
    }

    [Description("Move or rename a file or folder.")]
    private Task<string> MoveAsync(
        [Description("Current path.")] string source,
        [Description("New path.")] string destination,
        [Description("Replace the destination if it exists.")] bool overwrite = false)
    {
        var from = Locate(source);
        var to = Locate(destination);

        return GuardedAsync("files.move", $"Move {PathGuard.Describe(from)} → {PathGuard.Describe(to)}", () =>
        {
            var canonicalFrom = Guard.EnsureWritable(from);
            var canonicalTo = Guard.EnsureWritable(to);

            if (Directory.Exists(canonicalFrom))
            {
                if (Directory.Exists(canonicalTo) && !overwrite)
                    return Refused($"{PathGuard.Describe(canonicalTo)} already exists.");

                Directory.Move(canonicalFrom, canonicalTo);
                return Ok($"Moved folder to {PathGuard.Describe(canonicalTo)}.");
            }

            if (!File.Exists(canonicalFrom)) return Failed($"{PathGuard.Describe(canonicalFrom)} does not exist.");

            Directory.CreateDirectory(Path.GetDirectoryName(canonicalTo)!);
            File.Move(canonicalFrom, canonicalTo, overwrite);
            return Ok($"Moved to {PathGuard.Describe(canonicalTo)}.");
        },
        [from, to], ApprovalKind.WriteFiles);
    }

    [Description("Copy a file or folder.")]
    private Task<string> CopyAsync(
        [Description("Path to copy from.")] string source,
        [Description("Path to copy to.")] string destination,
        [Description("Replace the destination if it exists.")] bool overwrite = false)
    {
        var from = Locate(source);
        var to = Locate(destination);

        return GuardedAsync("files.copy", $"Copy {PathGuard.Describe(from)} → {PathGuard.Describe(to)}", () =>
        {
            var canonicalFrom = Guard.EnsureReadable(from);
            var canonicalTo = Guard.EnsureWritable(to);

            if (Directory.Exists(canonicalFrom))
            {
                var copied = CopyDirectory(canonicalFrom, canonicalTo, overwrite);
                return Ok($"Copied {copied:N0} files to {PathGuard.Describe(canonicalTo)}.");
            }

            if (!File.Exists(canonicalFrom)) return Failed($"{PathGuard.Describe(canonicalFrom)} does not exist.");

            Directory.CreateDirectory(Path.GetDirectoryName(canonicalTo)!);
            File.Copy(canonicalFrom, canonicalTo, overwrite);
            return Ok($"Copied to {PathGuard.Describe(canonicalTo)}.");
        },
        [from, to], ApprovalKind.WriteFiles);
    }

    private int CopyDirectory(string source, string destination, bool overwrite)
    {
        Directory.CreateDirectory(destination);
        var count = 0;

        foreach (var file in Directory.EnumerateFiles(source, "*",
                     new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
        {
            if (!Guard.IsAllowed(file, FolderAccess.Read, out _, out _)) continue;

            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite);
            count++;

            if (count >= Guard.Policy.MaxBatchSize) break;
        }

        return count;
    }

    [Description("Delete a file or folder.")]
    private Task<string> DeleteAsync(
        [Description("Path to delete.")] string path,
        [Description("Required for folders — confirms the whole tree goes.")] bool recursive = false)
    {
        var target = Locate(path);

        return GuardedAsync("files.delete", $"Delete {PathGuard.Describe(target)}", () =>
        {
            var canonical = Guard.EnsureDeletable(target);

            if (Directory.Exists(canonical))
            {
                if (!recursive)
                    return Refused($"{PathGuard.Describe(canonical)} is a folder. Call again with recursive=true to delete it and everything inside.");

                if (Guard.Policy.SoftDelete) return Ok(Recycle(canonical, isDirectory: true));

                Directory.Delete(canonical, recursive: true);
                return Ok($"Deleted folder {PathGuard.Describe(canonical)}.");
            }

            if (!File.Exists(canonical)) return Failed($"{PathGuard.Describe(canonical)} does not exist.");

            if (Guard.Policy.SoftDelete) return Ok(Recycle(canonical, isDirectory: false));

            File.Delete(canonical);
            return Ok($"Deleted {PathGuard.Describe(canonical)}.");
        },
        [target], ApprovalKind.DeleteFiles,
        Guard.Policy.SoftDelete ? "Moved to AutoWork's recycle folder — recoverable." : "Permanent deletion.");
    }

    /// <summary>
    /// Soft delete moves into AutoWork's own directory, which PathGuard treats as protected —
    /// so the agent cannot read recycled files back out.
    /// </summary>
    private static string Recycle(string path, bool isDirectory)
    {
        var bin = Path.Combine(AppPaths.Root, "recycle", DateTime.Now.ToString("yyyyMMdd"));
        Directory.CreateDirectory(bin);

        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var destination = Path.Combine(bin, $"{DateTime.Now:HHmmss}-{name}");

        if (isDirectory) Directory.Move(path, destination);
        else File.Move(path, destination);

        return $"Moved {PathGuard.Describe(path)} to AutoWork's recycle folder. It can be restored from there.";
    }

    // ── Batch operations ──────────────────────────────────────────────────────────────────

    [Description("Rename many files at once with a regular expression.")]
    private Task<string> BatchRenameAsync(
        [Description("Folder containing the files.")] string folder,
        [Description("Regular expression matched against each filename.")] string matchPattern,
        [Description("Replacement, which may use $1 style group references.")] string replacement,
        [Description("Filename filter applied before matching, such as *.jpg.")] string filter = "*",
        [Description("Preview without renaming anything. Do this first.")] bool dryRun = true)
    {
        var target = Locate(folder);

        return GuardedAsync("files.batch_rename",
            dryRun ? $"Preview rename in {PathGuard.Describe(target)}" : $"Rename files in {PathGuard.Describe(target)}",
            () =>
            {
                var canonical = dryRun ? Guard.EnsureReadable(target) : Guard.EnsureWritable(target);
                if (!Directory.Exists(canonical)) return Failed($"{PathGuard.Describe(canonical)} is not a folder.");

                Regex regex;
                try { regex = new Regex(matchPattern, RegexOptions.None, TimeSpan.FromSeconds(2)); }
                catch (ArgumentException ex) { return Failed($"\"{matchPattern}\" is not a valid regular expression: {ex.Message}"); }

                var planned = new List<(string From, string To)>();
                var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var file in Directory.EnumerateFiles(canonical, filter, SearchOption.TopDirectoryOnly))
                {
                    if (planned.Count >= Guard.Policy.MaxBatchSize) break;

                    var name = Path.GetFileName(file);
                    if (!regex.IsMatch(name)) continue;

                    var renamed = regex.Replace(name, replacement);
                    if (renamed == name || string.IsNullOrWhiteSpace(renamed)) continue;

                    if (renamed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                        return Failed($"The replacement would produce an invalid filename: \"{renamed}\".");

                    var destination = Path.Combine(canonical, renamed);

                    // Catch collisions before touching anything — a half-applied batch rename
                    // is far worse than a refused one.
                    if (!taken.Add(destination))
                        return Refused($"Two files would both become \"{renamed}\". Adjust the pattern and try again.");

                    if (File.Exists(destination) && !string.Equals(destination, file, StringComparison.OrdinalIgnoreCase))
                        return Refused($"\"{renamed}\" already exists in the folder. Adjust the pattern and try again.");

                    planned.Add((file, destination));
                }

                if (planned.Count == 0) return Ok($"No filenames in {PathGuard.Describe(canonical)} matched \"{matchPattern}\".");

                var preview = string.Join('\n', planned.Take(25)
                    .Select(p => $"  {Path.GetFileName(p.From)}  →  {Path.GetFileName(p.To)}"));
                var more = planned.Count > 25 ? $"\n  … and {planned.Count - 25} more" : "";

                if (dryRun)
                    return Ok($"Would rename {planned.Count} files:\n{preview}{more}\n\nCall again with dryRun=false to apply.");

                foreach (var (from, to) in planned) File.Move(from, to);

                return Ok($"Renamed {planned.Count} files in {PathGuard.Describe(canonical)}:\n{preview}{more}");
            },
            [target],
            dryRun ? null : ApprovalKind.WriteFiles);
    }

    [Description("Sort a folder's files into subfolders.")]
    private Task<string> OrganizeAsync(
        [Description("Folder to organise.")] string folder,
        [Description("How to group: extension, date, or type.")] string strategy = "extension",
        [Description("Preview without moving anything. Do this first.")] bool dryRun = true)
    {
        var target = Locate(folder);

        return GuardedAsync("files.organize",
            dryRun ? $"Preview organising {PathGuard.Describe(target)}" : $"Organise {PathGuard.Describe(target)}",
            () =>
            {
                var canonical = dryRun ? Guard.EnsureReadable(target) : Guard.EnsureWritable(target);
                if (!Directory.Exists(canonical)) return Failed($"{PathGuard.Describe(canonical)} is not a folder.");

                var normalized = strategy.Trim().ToLowerInvariant();
                if (normalized is not ("extension" or "date" or "type"))
                    return Failed($"\"{strategy}\" is not a known strategy. Use extension, date or type.");

                var planned = new List<(string From, string To, string Bucket)>();

                foreach (var file in Directory.EnumerateFiles(canonical, "*", SearchOption.TopDirectoryOnly))
                {
                    if (planned.Count >= Guard.Policy.MaxBatchSize) break;
                    if (!Guard.IsAllowed(file, FolderAccess.Read, out _, out _)) continue;

                    var bucket = normalized switch
                    {
                        "date" => new FileInfo(file).LastWriteTime.ToString("yyyy-MM"),
                        "type" => CategoryFor(Path.GetExtension(file)),
                        _ => ExtensionBucket(Path.GetExtension(file)),
                    };

                    planned.Add((file, Path.Combine(canonical, bucket, Path.GetFileName(file)), bucket));
                }

                if (planned.Count == 0) return Ok($"{PathGuard.Describe(canonical)} has no loose files to organise.");

                var summary = planned.GroupBy(p => p.Bucket)
                    .OrderByDescending(g => g.Count())
                    .Select(g => $"  {g.Key}/  {g.Count()} files")
                    .ToList();

                if (dryRun)
                    return Ok($"Would move {planned.Count} files into {summary.Count} folders:\n" +
                              string.Join('\n', summary) + "\n\nCall again with dryRun=false to apply.");

                var moved = 0;
                foreach (var (from, to, _) in planned)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                    if (File.Exists(to)) continue;
                    File.Move(from, to);
                    moved++;
                }

                return Ok($"Organised {moved} files in {PathGuard.Describe(canonical)}:\n" + string.Join('\n', summary));
            },
            [target],
            dryRun ? null : ApprovalKind.WriteFiles);
    }

    private static string ExtensionBucket(string extension) =>
        string.IsNullOrWhiteSpace(extension) ? "no-extension" : extension.TrimStart('.').ToLowerInvariant();

    private static string CategoryFor(string extension) => extension.ToLowerInvariant() switch
    {
        ".pdf" or ".doc" or ".docx" or ".odt" or ".rtf" or ".txt" or ".md" => "Documents",
        ".xls" or ".xlsx" or ".csv" or ".ods" => "Spreadsheets",
        ".ppt" or ".pptx" or ".odp" => "Presentations",
        ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".heic" or ".svg" => "Images",
        ".mp4" or ".mov" or ".avi" or ".mkv" or ".webm" => "Video",
        ".mp3" or ".wav" or ".flac" or ".m4a" or ".ogg" => "Audio",
        ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => "Archives",
        ".exe" or ".msi" or ".dmg" or ".pkg" or ".deb" or ".appimage" => "Installers",
        _ => "Other",
    };
}

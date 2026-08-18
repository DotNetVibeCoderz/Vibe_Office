using Microsoft.Extensions.Options;
using VibeDesk.Application.Abstractions;
using VibeDesk.Infrastructure.Configuration;

namespace VibeDesk.Infrastructure.Storage;

/// <summary>
/// Development backend that writes objects as files under a root directory. Keys are treated as
/// relative paths and validated so a crafted key can never escape the root.
/// </summary>
public sealed class FileSystemStorageProvider(IOptions<StorageOptions> options) : IStorageProvider
{
    private readonly StorageOptions _options = options.Value;

    public string Name => "FileSystem";

    private string Root => Path.GetFullPath(_options.RootPath);

    /// <summary>
    /// Maps a key to an absolute path, rejecting anything that would land outside the root.
    /// Without this check a key like <c>../../appsettings.json</c> would be a read/write primitive
    /// over the whole filesystem.
    /// </summary>
    private string ResolvePath(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Storage key must not be empty.", nameof(key));

        var normalised = key.Replace('\\', '/').TrimStart('/');
        var full = Path.GetFullPath(Path.Combine(Root, normalised));

        var rootWithSeparator = Root.EndsWith(Path.DirectorySeparatorChar)
            ? Root
            : Root + Path.DirectorySeparatorChar;

        if (!full.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException($"Storage key '{key}' resolves outside the storage root.");

        return full;
    }

    public async Task<StorageObject> PutAsync(
        string key, Stream content, string contentType, CancellationToken ct = default)
    {
        var path = ResolvePath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Write to a temp file then move, so a failed or cancelled upload never leaves a truncated
        // object that later reads would treat as valid.
        var tempPath = path + ".uploading";
        try
        {
            await using (var destination = new FileStream(
                tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await content.CopyToAsync(destination, ct);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            throw;
        }

        // Content type isn't recoverable from the filesystem, so it is persisted alongside.
        await File.WriteAllTextAsync(path + ".ct", contentType, ct);

        var info = new FileInfo(path);
        return new StorageObject(key, info.Length, contentType, info.LastWriteTimeUtc);
    }

    public Task<Stream?> GetAsync(string key, CancellationToken ct = default)
    {
        var path = ResolvePath(key);
        if (!File.Exists(path)) return Task.FromResult<Stream?>(null);

        Stream stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Task.FromResult<Stream?>(stream);
    }

    public Task<bool> DeleteAsync(string key, CancellationToken ct = default)
    {
        var path = ResolvePath(key);
        if (!File.Exists(path)) return Task.FromResult(false);

        File.Delete(path);
        if (File.Exists(path + ".ct")) File.Delete(path + ".ct");
        return Task.FromResult(true);
    }

    public Task<bool> ExistsAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(File.Exists(ResolvePath(key)));

    public async Task<StorageObject?> StatAsync(string key, CancellationToken ct = default)
    {
        var path = ResolvePath(key);
        if (!File.Exists(path)) return null;

        var info = new FileInfo(path);
        var contentType = File.Exists(path + ".ct")
            ? await File.ReadAllTextAsync(path + ".ct", ct)
            : "application/octet-stream";

        return new StorageObject(key, info.Length, contentType, info.LastWriteTimeUtc);
    }

    public async Task<string> CopyAsync(
        string sourceKey, string destinationKey, CancellationToken ct = default)
    {
        var source = ResolvePath(sourceKey);
        if (!File.Exists(source))
            throw new FileNotFoundException($"Storage object '{sourceKey}' not found.");

        var destination = ResolvePath(destinationKey);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
        await using (var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await input.CopyToAsync(output, ct);
        }

        if (File.Exists(source + ".ct")) File.Copy(source + ".ct", destination + ".ct", overwrite: true);

        return destinationKey;
    }

    /// <summary>
    /// The filesystem cannot sign URLs, so this returns the app's own download route. Authorisation
    /// therefore happens in that endpoint rather than in the URL itself.
    /// </summary>
    public Task<string> GetUrlAsync(string key, TimeSpan? validFor = null, CancellationToken ct = default) =>
        Task.FromResult($"/storage/{Uri.EscapeDataString(key)}");
}

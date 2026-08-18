namespace VibeDesk.Application.Abstractions;

/// <summary>
/// Blob storage seam. One implementation per configured backend (FileSystem, Azure Blob, S3, MinIO)
/// selected by <c>Storage:Provider</c>, so feature code never references a cloud SDK directly.
/// </summary>
public interface IStorageProvider
{
    /// <summary>Backend name for diagnostics and the admin health page.</summary>
    string Name { get; }

    Task<StorageObject> PutAsync(
        string key,
        Stream content,
        string contentType,
        CancellationToken ct = default);

    /// <summary>Returns null when the key does not exist rather than throwing.</summary>
    Task<Stream?> GetAsync(string key, CancellationToken ct = default);

    Task<bool> DeleteAsync(string key, CancellationToken ct = default);

    Task<bool> ExistsAsync(string key, CancellationToken ct = default);

    Task<StorageObject?> StatAsync(string key, CancellationToken ct = default);

    Task<string> CopyAsync(string sourceKey, string destinationKey, CancellationToken ct = default);

    /// <summary>
    /// A URL the browser can fetch directly. Backends that support signing return a time-limited
    /// signed URL; the filesystem backend returns a route served by the app's own download endpoint.
    /// </summary>
    Task<string> GetUrlAsync(string key, TimeSpan? validFor = null, CancellationToken ct = default);
}

public sealed record StorageObject(
    string Key,
    long SizeBytes,
    string ContentType,
    DateTimeOffset LastModified,
    string? ETag = null);

namespace VibeDesk.Application.Abstractions;

/// <summary>
/// Cache seam over MemoryCache (dev) or Redis (production). Deliberately narrow: get-or-create plus
/// tag-based invalidation, which is all the app needs and the only part both backends do well.
/// </summary>
public interface ICacheService
{
    string Name { get; }

    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);

    Task SetAsync<T>(
        string key,
        T value,
        TimeSpan? ttl = null,
        IEnumerable<string>? tags = null,
        CancellationToken ct = default);

    /// <summary>
    /// Runs <paramref name="factory"/> only on a miss. Concurrent callers for the same key are
    /// coalesced so a cold cache doesn't stampede the database.
    /// </summary>
    Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? ttl = null,
        IEnumerable<string>? tags = null,
        CancellationToken ct = default);

    Task RemoveAsync(string key, CancellationToken ct = default);

    /// <summary>Drops every entry registered under a tag, e.g. <c>drive:{userId}</c> after a move.</summary>
    Task RemoveByTagAsync(string tag, CancellationToken ct = default);
}

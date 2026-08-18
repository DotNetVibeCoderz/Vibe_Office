using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using VibeDesk.Application.Abstractions;
using VibeDesk.Infrastructure.Configuration;

namespace VibeDesk.Infrastructure.Caching;

/// <summary>
/// In-process cache for development and single-instance deployments.
/// </summary>
/// <remarks>
/// <see cref="IMemoryCache"/> has no tag support, so tags are tracked in a side index of
/// tag → keys. Entries register an eviction callback that prunes themselves from that index, which
/// keeps it from growing without bound as entries expire naturally.
/// </remarks>
public sealed class MemoryCacheService : ICacheService, IDisposable
{
    private readonly IMemoryCache _cache;
    private readonly CacheOptions _options;

    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _tagIndex = new();

    /// <summary>Per-key locks so a cold key is only computed once under concurrent access.</summary>
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _keyLocks = new();

    public MemoryCacheService(IOptions<CacheOptions> options)
    {
        _options = options.Value;
        _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = _options.MemorySizeLimit });
    }

    public string Name => "MemoryCache";

    private TimeSpan DefaultTtl => TimeSpan.FromSeconds(_options.DefaultTtlSeconds);

    public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) =>
        Task.FromResult(_cache.TryGetValue(key, out var value) ? (T?)value : default);

    public Task SetAsync<T>(
        string key,
        T value,
        TimeSpan? ttl = null,
        IEnumerable<string>? tags = null,
        CancellationToken ct = default)
    {
        var tagList = tags?.ToArray() ?? [];

        var entryOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ttl ?? DefaultTtl,
            // Every entry counts as 1 against SizeLimit; approximate but enough to bound the cache.
            Size = 1,
        };

        entryOptions.RegisterPostEvictionCallback((evictedKey, _, _, _) =>
        {
            var name = evictedKey.ToString();
            if (name is null) return;

            foreach (var tag in tagList)
            {
                if (_tagIndex.TryGetValue(tag, out var keys)) keys.TryRemove(name, out _);
            }
        });

        _cache.Set(key, value, entryOptions);

        foreach (var tag in tagList)
        {
            _tagIndex.GetOrAdd(tag, _ => new ConcurrentDictionary<string, byte>())[key] = 0;
        }

        return Task.CompletedTask;
    }

    public async Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? ttl = null,
        IEnumerable<string>? tags = null,
        CancellationToken ct = default)
    {
        if (_cache.TryGetValue(key, out var cached) && cached is T hit) return hit;

        var gate = _keyLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            // Re-check: another caller may have populated it while we waited.
            if (_cache.TryGetValue(key, out var second) && second is T secondHit) return secondHit;

            var value = await factory(ct);
            await SetAsync(key, value, ttl, tags, ct);
            return value;
        }
        finally
        {
            gate.Release();
            // Drop the lock once uncontended so the dictionary doesn't accumulate one entry per key.
            if (gate.CurrentCount == 1) _keyLocks.TryRemove(key, out _);
        }
    }

    public Task RemoveAsync(string key, CancellationToken ct = default)
    {
        _cache.Remove(key);
        return Task.CompletedTask;
    }

    public Task RemoveByTagAsync(string tag, CancellationToken ct = default)
    {
        if (_tagIndex.TryRemove(tag, out var keys))
        {
            foreach (var key in keys.Keys) _cache.Remove(key);
        }
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _cache.Dispose();
        foreach (var gate in _keyLocks.Values) gate.Dispose();
    }
}

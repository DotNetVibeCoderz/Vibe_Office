using System.Text.Json;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using VibeDesk.Application.Abstractions;
using VibeDesk.Infrastructure.Configuration;

namespace VibeDesk.Infrastructure.Caching;

/// <summary>
/// Redis-backed cache for production, where several app instances must share invalidation.
/// </summary>
/// <remarks>
/// Tags are Redis sets holding member keys, so <see cref="RemoveByTagAsync"/> is one <c>SMEMBERS</c>
/// plus a batched delete — deliberately not <c>KEYS pattern</c>, which is O(keyspace) and blocks the
/// server. Values are JSON rather than binary so cached data stays inspectable with redis-cli.
/// </remarks>
public sealed class RedisCacheService : ICacheService, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IConnectionMultiplexer _redis;
    private readonly CacheOptions _options;
    private readonly bool _ownsConnection;

    public RedisCacheService(IOptions<CacheOptions> options)
    {
        _options = options.Value;

        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException(
                "Cache:ConnectionString is required when Cache:Provider is Redis.");
        }

        var config = ConfigurationOptions.Parse(_options.ConnectionString);
        // Don't take the whole app down because the cache is briefly unreachable.
        config.AbortOnConnectFail = false;

        _redis = ConnectionMultiplexer.Connect(config);
        _ownsConnection = true;
    }

    /// <summary>Overload for hosts that already own a multiplexer (the API and Web share one).</summary>
    public RedisCacheService(IConnectionMultiplexer redis, IOptions<CacheOptions> options)
    {
        _redis = redis;
        _options = options.Value;
        _ownsConnection = false;
    }

    public string Name => "Redis";

    private IDatabase Db => _redis.GetDatabase();

    private TimeSpan DefaultTtl => TimeSpan.FromSeconds(_options.DefaultTtlSeconds);

    private string Prefixed(string key) => _options.InstanceName + key;
    private string TagKey(string tag) => $"{_options.InstanceName}tag:{tag}";

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        var value = await Db.StringGetAsync(Prefixed(key));
        if (value.IsNullOrEmpty) return default;

        try
        {
            // Cast is required: RedisValue converts implicitly to both string and byte[].
            return JsonSerializer.Deserialize<T>((string)value!, JsonOptions);
        }
        catch (JsonException)
        {
            // A shape change between deployments shouldn't surface as a 500 — treat it as a miss.
            await Db.KeyDeleteAsync(Prefixed(key));
            return default;
        }
    }

    public async Task SetAsync<T>(
        string key,
        T value,
        TimeSpan? ttl = null,
        IEnumerable<string>? tags = null,
        CancellationToken ct = default)
    {
        var expiry = ttl ?? DefaultTtl;
        var payload = JsonSerializer.Serialize(value, JsonOptions);

        var batch = Db.CreateBatch();
        var tasks = new List<Task> { batch.StringSetAsync(Prefixed(key), payload, expiry) };

        foreach (var tag in tags ?? [])
        {
            tasks.Add(batch.SetAddAsync(TagKey(tag), key));
            // Keep the tag set alive at least as long as its longest-lived member.
            tasks.Add(batch.KeyExpireAsync(TagKey(tag), expiry.Add(TimeSpan.FromMinutes(5)),
                ExpireWhen.GreaterThanCurrentExpiry));
        }

        batch.Execute();
        await Task.WhenAll(tasks);
    }

    public async Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? ttl = null,
        IEnumerable<string>? tags = null,
        CancellationToken ct = default)
    {
        var existing = await GetAsync<T>(key, ct);
        if (existing is not null) return existing;

        var value = await factory(ct);
        await SetAsync(key, value, ttl, tags, ct);
        return value;
    }

    public Task RemoveAsync(string key, CancellationToken ct = default) =>
        Db.KeyDeleteAsync(Prefixed(key));

    public async Task RemoveByTagAsync(string tag, CancellationToken ct = default)
    {
        var members = await Db.SetMembersAsync(TagKey(tag));
        if (members.Length == 0) return;

        var keys = members
            .Where(m => !m.IsNullOrEmpty)
            .Select(m => (RedisKey)Prefixed(m.ToString()))
            .ToArray();

        await Db.KeyDeleteAsync(keys);
        await Db.KeyDeleteAsync(TagKey(tag));
    }

    public async ValueTask DisposeAsync()
    {
        if (_ownsConnection) await _redis.DisposeAsync();
    }
}

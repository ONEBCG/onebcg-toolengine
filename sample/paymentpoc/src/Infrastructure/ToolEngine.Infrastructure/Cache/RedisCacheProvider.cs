using System.Text.Json;
using StackExchange.Redis;
using ToolEngine.Core.Abstractions.Cache;

namespace ToolEngine.Infrastructure.Cache;

/// <summary>
/// Redis-backed ICacheProvider using StackExchange.Redis.
/// Registered when Cache:Provider = "redis" in appsettings.
/// All values are JSON-serialised for type safety across process restarts.
/// </summary>
public sealed class RedisCacheProvider : ICacheProvider
{
    private readonly IDatabase _db;

    public RedisCacheProvider(IConnectionMultiplexer redis)
        => _db = redis.GetDatabase();

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        var raw = await _db.StringGetAsync(key);
        return raw.HasValue
            ? JsonSerializer.Deserialize<T>(raw.ToString())
            : default;
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null,
        CancellationToken ct = default)
    {
        var serialised = JsonSerializer.Serialize(value);
        // StringSetAsync in StackExchange.Redis 2.x takes TimeSpan not TimeSpan?
        if (expiry.HasValue)
            await _db.StringSetAsync(key, serialised, expiry.Value);
        else
            await _db.StringSetAsync(key, serialised);
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
        => await _db.KeyDeleteAsync(key);

    /// <summary>
    /// Atomically increments the counter. On the first increment (result == delta)
    /// the TTL is set so the key expires correctly — matching the memory provider's
    /// expiry semantics for loop detection and budget counters.
    /// </summary>
    public async Task<long> IncrementAsync(string key, long delta = 1,
        TimeSpan? expiry = null, CancellationToken ct = default)
    {
        var result = await _db.StringIncrementAsync(key, delta);
        if (result == delta && expiry.HasValue)
            await _db.KeyExpireAsync(key, expiry.Value);
        return result;
    }
}

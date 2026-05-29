using Microsoft.Extensions.Caching.Memory;
using ToolEngine.Core.Abstractions.Cache;

namespace ToolEngine.Infrastructure.Cache;

// ── MemoryCacheProvider ───────────────────────────────────────────────────────
// Used in POC single-instance deployment.
// In production: replace with DistributedCacheProvider backed by Redis.

public sealed class MemoryCacheProvider : ICacheProvider
{
    private readonly IMemoryCache _cache;
    private readonly object       _lock = new();

    public MemoryCacheProvider(IMemoryCache cache) => _cache = cache;

    public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) =>
        Task.FromResult(_cache.TryGetValue<T>(key, out var val) ? val : default);

    public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default)
    {
        if (expiry.HasValue)
            _cache.Set(key, value, expiry.Value);
        else
            _cache.Set(key, value);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken ct = default)
    {
        _cache.Remove(key);
        return Task.CompletedTask;
    }

    // Atomic increment — uses IMemoryCache as the single source of truth so that
    // TTL expiry is respected consistently.  A separate Dictionary<string,long>
    // would never expire, causing budget / loop-detection counters to accumulate
    // indefinitely across TTL boundaries.
    public Task<long> IncrementAsync(string key, long delta = 1, TimeSpan? expiry = null,
        CancellationToken ct = default)
    {
        lock (_lock)
        {
            _cache.TryGetValue<long>(key, out var current);
            var next = current + delta;

            if (expiry.HasValue)
                _cache.Set(key, next, expiry.Value);
            else
                _cache.Set(key, next);

            return Task.FromResult(next);
        }
    }
}

namespace ToolEngine.Infrastructure.Cache;

using Microsoft.Extensions.Caching.Memory;
using ToolEngine.Core.Abstractions.Common;

/// <summary>
/// In-process ICacheProvider backed by IMemoryCache.
/// Suitable for development and single-node deployments.
///
/// IncrementAsync: uses lock for in-process atomicity. TTL is applied (or refreshed)
/// on every increment. For precise TTL-on-creation-only semantics, use DistributedCacheProvider.
///
/// Switch to DistributedCacheProvider for multi-pod deployments ("Cache:Provider": "redis").
/// </summary>
internal sealed class MemoryCacheProvider : ICacheProvider
{
    private readonly IMemoryCache _cache;
    private readonly object       _lock = new();

    public MemoryCacheProvider(IMemoryCache cache) => _cache = cache;

    public Task<string?> GetStringAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(_cache.TryGetValue<string>(key, out var v) ? v : null);

    public Task SetStringAsync(string key, string value, TimeSpan ttl, CancellationToken ct = default)
    {
        _cache.Set(key, value, ttl);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken ct = default)
    {
        _cache.Remove(key);
        return Task.CompletedTask;
    }

    public Task<int> IncrementAsync(string key, TimeSpan ttl, CancellationToken ct = default)
    {
        int next;
        lock (_lock)
        {
            var current = _cache.TryGetValue<int>(key, out var v) ? v : 0;
            next = current + 1;
            // Set with sliding expiration based on ttl.
            // IMemoryCache does not expose remaining TTL — sliding is the nearest safe approximation.
            _cache.Set(key, next, new MemoryCacheEntryOptions
            {
                SlidingExpiration = ttl
            });
        }
        return Task.FromResult(next);
    }
}

namespace ToolEngine.Infrastructure.Cache;

using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Caching.Distributed;
using ToolEngine.Core.Abstractions.Common;

/// <summary>
/// ICacheProvider backed by IDistributedCache (Redis, SQL, or any compatible provider).
///
/// For Redis, register the implementation in the host:
///   services.AddStackExchangeRedisCache(opt => opt.Configuration = connStr);
///
/// IncrementAsync uses GET → SET with a CAS-style optimistic approach.
/// For true atomic Redis INCR, replace with StackExchange.Redis directly (Phase I).
/// </summary>
internal sealed class DistributedCacheProvider : ICacheProvider
{
    private readonly IDistributedCache _cache;

    // H7 — per-key semaphores provide process-level atomicity for IncrementAsync.
    // For multi-instance deployments (Redis), replace with IConnectionMultiplexer + INCR
    // + EXPIRE for true distributed atomicity (Phase I). Leaving the optimistic GET→SET
    // without a lock allows two concurrent requests on the same instance to both read
    // the same counter value and both write n+1, silently under-counting.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public DistributedCacheProvider(IDistributedCache cache) => _cache = cache;

    public async Task<string?> GetStringAsync(string key, CancellationToken ct = default)
    {
        var bytes = await _cache.GetAsync(key, ct);
        return bytes is null ? null : Encoding.UTF8.GetString(bytes);
    }

    public Task SetStringAsync(string key, string value, TimeSpan ttl, CancellationToken ct = default)
    {
        var opts = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ttl
        };
        return _cache.SetAsync(key, Encoding.UTF8.GetBytes(value), opts, ct);
    }

    public Task RemoveAsync(string key, CancellationToken ct = default) =>
        _cache.RemoveAsync(key, ct);

    public async Task<int> IncrementAsync(string key, TimeSpan ttl, CancellationToken ct = default)
    {
        var sem = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);
        try
        {
            var raw     = await GetStringAsync(key, ct);
            var current = raw is null ? 0 : int.TryParse(raw, out var n) ? n : 0;
            var next    = current + 1;
            await SetStringAsync(key, next.ToString(), ttl, ct);
            return next;
        }
        finally
        {
            sem.Release();
        }
    }
}

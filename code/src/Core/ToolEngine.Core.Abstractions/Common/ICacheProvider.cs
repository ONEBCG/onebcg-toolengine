namespace ToolEngine.Core.Abstractions.Common;

/// <summary>
/// Provider-agnostic distributed cache abstraction.
///
/// Backed by IMemoryCache (development / single-node) or IDistributedCache / Redis (production).
/// Configure via "Cache:Provider" = "memory" | "redis".
///
/// Memory implementation: in-process, no network cost, not shared across pods.
/// Redis implementation:  shared state, survives pod restarts, supports horizontal scale.
/// </summary>
public interface ICacheProvider
{
    /// <summary>Returns null if the key does not exist or has expired.</summary>
    Task<string?> GetStringAsync(string key, CancellationToken ct = default);

    /// <summary>Sets a string value with an absolute TTL.</summary>
    Task SetStringAsync(string key, string value, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>Removes the key unconditionally. No-op if the key does not exist.</summary>
    Task RemoveAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Atomically increments an integer counter stored at <paramref name="key"/>.
    /// If the key does not exist it is created with value 1 and the given TTL applied.
    /// The TTL is only applied on first creation — it is NOT refreshed on each increment.
    /// Returns the counter value after incrementing.
    /// </summary>
    Task<int> IncrementAsync(string key, TimeSpan ttl, CancellationToken ct = default);
}

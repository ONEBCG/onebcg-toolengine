namespace ToolEngine.Core.Abstractions.Cache;

public interface ICacheProvider
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);
    Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default);
    Task RemoveAsync(string key, CancellationToken ct = default);

    // Required for distributed loop detection (F4) and daily budget counter (Phase I).
    Task<long> IncrementAsync(string key, long delta = 1, TimeSpan? expiry = null, CancellationToken ct = default);
}

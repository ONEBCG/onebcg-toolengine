namespace ToolEngine.Infrastructure.Persistence;

using ToolEngine.Core.Abstractions.Persistence;
using ToolEngine.Core.Domain.Entities;

/// <summary>
/// Scoped decorator over ReadRepository&lt;Tenant, string&gt; that caches the Tenant
/// result within the lifetime of a single HTTP request (scoped DI lifetime).
///
/// Eliminates duplicate DB reads when TenantAuthorizationBehavior, TokenBudgetBehavior,
/// and DailyBudgetBehavior each call GetByIdAsync for the same TenantId in one pipeline pass.
///
/// Phase F replaces the inner IReadRepository dependency and adds tenant count caching.
/// </summary>
internal sealed class CachedTenantReadRepository : IReadRepository<Tenant, string>
{
    private readonly ReadRepository<Tenant, string>  _inner;
    private readonly Dictionary<string, Tenant?>     _cache = new(StringComparer.OrdinalIgnoreCase);

    public CachedTenantReadRepository(AppDbContext ctx)
        => _inner = new ReadRepository<Tenant, string>(ctx);

    public async Task<Tenant?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(id, out var cached))
            return cached;

        var tenant = await _inner.GetByIdAsync(id, ct);
        _cache[id] = tenant;
        return tenant;
    }

    public Task<IReadOnlyList<Tenant>> ListAllAsync(CancellationToken ct = default) =>
        _inner.ListAllAsync(ct);

    public Task<IReadOnlyList<Tenant>> ListAsync(
        ISpecification<Tenant> spec, CancellationToken ct = default) =>
        _inner.ListAsync(spec, ct);

    public Task<int> CountAsync(
        ISpecification<Tenant> spec, CancellationToken ct = default) =>
        _inner.CountAsync(spec, ct);

    public Task<PagedResult<Tenant>> PagedListAsync(
        ISpecification<Tenant> spec, int pageNumber, int pageSize,
        CancellationToken ct = default) =>
        _inner.PagedListAsync(spec, pageNumber, pageSize, ct);
}

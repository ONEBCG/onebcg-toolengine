namespace ToolEngine.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using ToolEngine.Core.Abstractions.Persistence;
using ToolEngine.Core.Domain.Common;

internal sealed class ReadRepository<TEntity, TId>
    : IReadRepository<TEntity, TId>
    where TEntity : Entity<TId>
    where TId : notnull
{
    private readonly AppDbContext _ctx;

    public ReadRepository(AppDbContext ctx) => _ctx = ctx;

    public Task<TEntity?> GetByIdAsync(TId id, CancellationToken ct = default) =>
        _ctx.Set<TEntity>().AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id.Equals(id), ct);

    public async Task<IReadOnlyList<TEntity>> ListAllAsync(
        CancellationToken ct = default) =>
        await _ctx.Set<TEntity>().AsNoTracking().ToListAsync(ct);

    public async Task<IReadOnlyList<TEntity>> ListAsync(
        ISpecification<TEntity> spec,
        CancellationToken ct = default)
    {
        var query = _ctx.Set<TEntity>().AsNoTracking()
                        .Where(spec.Criteria);

        foreach (var include in spec.Includes)
            query = query.Include(include);

        return await query.ToListAsync(ct);
    }

    public Task<int> CountAsync(
        ISpecification<TEntity> spec,
        CancellationToken ct = default) =>
        _ctx.Set<TEntity>().AsNoTracking()
            .CountAsync(spec.Criteria, ct);

    public async Task<PagedResult<TEntity>> PagedListAsync(
        ISpecification<TEntity> spec,
        int pageNumber,
        int pageSize,
        CancellationToken ct = default)
    {
        if (pageNumber < 1) pageNumber = 1;
        if (pageSize   < 1) pageSize   = 10;

        var query = _ctx.Set<TEntity>().AsNoTracking().Where(spec.Criteria);

        foreach (var include in spec.Includes)
            query = query.Include(include);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<TEntity>(items, totalCount, pageNumber, pageSize);
    }
}

using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using ToolEngine.Core.Abstractions.Persistence;

namespace ToolEngine.Infrastructure.Persistence;

// ── UnitOfWork ────────────────────────────────────────────────────────────────

// CRITICAL M2: implements IAsyncDisposable ONLY.
// Any call to sync Dispose() throws NotSupportedException to surface the violation early.
public sealed class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _db;
    private bool _disposed;

    public UnitOfWork(AppDbContext db) => _db = db;

    public Task<int> SaveChangesAsync(CancellationToken ct = default) =>
        _db.SaveChangesAsync(ct);

    // IAsyncDisposable — the ONLY dispose path
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _db.DisposeAsync();
    }
}

// ── EfRepository<TEntity,TId> ─────────────────────────────────────────────────

public sealed class EfRepository<TEntity, TId> : IRepository<TEntity, TId>
    where TEntity : class
{
    private readonly AppDbContext _db;

    public EfRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(TEntity entity, CancellationToken ct = default) =>
        await _db.Set<TEntity>().AddAsync(entity, ct);

    public Task UpdateAsync(TEntity entity, CancellationToken ct = default)
    {
        _db.Set<TEntity>().Update(entity);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(TEntity entity, CancellationToken ct = default)
    {
        _db.Set<TEntity>().Remove(entity);
        return Task.CompletedTask;
    }
}

// ── EfReadRepository<TEntity,TId> ────────────────────────────────────────────

public sealed class EfReadRepository<TEntity, TId> : IReadRepository<TEntity, TId>
    where TEntity : class
{
    private readonly AppDbContext _db;

    public EfReadRepository(AppDbContext db) => _db = db;

    public async Task<TEntity?> GetByIdAsync(TId id, CancellationToken ct = default) =>
        await _db.Set<TEntity>().FindAsync([id], ct);

    public async Task<IReadOnlyList<TEntity>> ListAsync(
        ISpecification<TEntity> spec, CancellationToken ct = default)
    {
        var q = BuildQuery(spec);
        return await q.AsNoTracking().ToListAsync(ct);
    }

    public async Task<PagedResult<TEntity>> PagedListAsync(
        ISpecification<TEntity> spec, int pageNumber, int pageSize, CancellationToken ct = default)
    {
        var q     = BuildQuery(spec);
        var total = await q.CountAsync(ct);
        var items = await q.AsNoTracking()
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<TEntity>
        {
            Items      = items,
            TotalCount = total,
            PageNumber = pageNumber,
            PageSize   = pageSize,
        };
    }

    private IQueryable<TEntity> BuildQuery(ISpecification<TEntity> spec)
    {
        var q = _db.Set<TEntity>().AsQueryable();

        if (spec.Criteria is not null)
            q = q.Where(spec.Criteria);

        foreach (var include in spec.Includes)
            q = q.Include(include);

        if (spec.OrderBy is not null)
            q = spec.IsDescending
                ? q.OrderByDescending(spec.OrderBy)
                : q.OrderBy(spec.OrderBy);

        return q;
    }
}

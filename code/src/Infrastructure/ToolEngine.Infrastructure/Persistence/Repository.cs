namespace ToolEngine.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using ToolEngine.Core.Abstractions.Persistence;
using ToolEngine.Core.Domain.Common;

internal sealed class Repository<TEntity, TId> : IRepository<TEntity, TId>
    where TEntity : Entity<TId>
    where TId : notnull
{
    private readonly AppDbContext _ctx;

    public Repository(AppDbContext ctx) => _ctx = ctx;

    public Task<TEntity?> GetByIdAsync(TId id, CancellationToken ct = default) =>
        _ctx.Set<TEntity>().FindAsync([id], ct).AsTask();

    public Task AddAsync(TEntity entity, CancellationToken ct = default) =>
        _ctx.Set<TEntity>().AddAsync(entity, ct).AsTask();

    public void Update(TEntity entity) => _ctx.Set<TEntity>().Update(entity);

    public void Remove(TEntity entity) => _ctx.Set<TEntity>().Remove(entity);
}

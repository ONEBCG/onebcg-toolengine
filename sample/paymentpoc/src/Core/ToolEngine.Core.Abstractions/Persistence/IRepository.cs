namespace ToolEngine.Core.Abstractions.Persistence;

public interface IRepository<TEntity, TId>
{
    Task AddAsync(TEntity entity, CancellationToken ct = default);
    Task UpdateAsync(TEntity entity, CancellationToken ct = default);
    Task DeleteAsync(TEntity entity, CancellationToken ct = default);
}

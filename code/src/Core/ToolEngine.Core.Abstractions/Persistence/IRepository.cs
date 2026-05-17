namespace ToolEngine.Core.Abstractions.Persistence;

public interface IRepository<TEntity, TId>
    where TEntity : class
{
    Task<TEntity?> GetByIdAsync(TId id, CancellationToken ct = default);
    Task AddAsync(TEntity entity, CancellationToken ct = default);
    void Update(TEntity entity);
    void Remove(TEntity entity);
}

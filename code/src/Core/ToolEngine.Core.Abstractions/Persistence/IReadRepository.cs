namespace ToolEngine.Core.Abstractions.Persistence;

public interface IReadRepository<TEntity, TId>
    where TEntity : class
{
    Task<TEntity?>          GetByIdAsync(TId id, CancellationToken ct = default);
    Task<IReadOnlyList<TEntity>> ListAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<TEntity>> ListAsync(
        ISpecification<TEntity> spec, CancellationToken ct = default);
    Task<int>               CountAsync(
        ISpecification<TEntity> spec, CancellationToken ct = default);
}

/// <summary>Thin specification pattern — keeps query logic in the domain layer.</summary>
public interface ISpecification<T>
{
    System.Linq.Expressions.Expression<Func<T, bool>> Criteria { get; }
    List<System.Linq.Expressions.Expression<Func<T, object>>> Includes { get; }
}

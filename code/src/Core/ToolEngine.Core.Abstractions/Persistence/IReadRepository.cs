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
    Task<PagedResult<TEntity>> PagedListAsync(
        ISpecification<TEntity> spec, int pageNumber, int pageSize,
        CancellationToken ct = default);
}

/// <summary>
/// Immutable paged result wrapper. PageNumber and PageSize are 1-based.
/// TotalCount is the unfiltered row count matching the specification.
/// </summary>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int              TotalCount,
    int              PageNumber,
    int              PageSize)
{
    public int  TotalPages  => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;
    public bool HasNext     => PageNumber < TotalPages;
    public bool HasPrevious => PageNumber > 1;
}

/// <summary>Thin specification pattern — keeps query logic in the domain layer.</summary>
public interface ISpecification<T>
{
    System.Linq.Expressions.Expression<Func<T, bool>> Criteria { get; }
    List<System.Linq.Expressions.Expression<Func<T, object>>> Includes { get; }
}

using System.Linq.Expressions;

namespace ToolEngine.Core.Abstractions.Persistence;

public interface ISpecification<T>
{
    Expression<Func<T, bool>>?           Criteria { get; }
    List<Expression<Func<T, object>>>    Includes { get; }
    Expression<Func<T, object>>?         OrderBy  { get; }
    bool                                 IsDescending { get; }
}

public sealed class ExpressionSpecification<T> : ISpecification<T>
{
    public Expression<Func<T, bool>>?        Criteria     { get; }
    public List<Expression<Func<T, object>>> Includes     { get; } = [];
    public Expression<Func<T, object>>?      OrderBy      { get; }
    public bool                               IsDescending { get; }

    public ExpressionSpecification(
        Expression<Func<T, bool>>?    criteria     = null,
        Expression<Func<T, object>>?  orderBy      = null,
        bool                          isDescending = false)
    {
        Criteria     = criteria;
        OrderBy      = orderBy;
        IsDescending = isDescending;
    }
}

public interface IReadRepository<TEntity, TId>
{
    Task<TEntity?> GetByIdAsync(TId id, CancellationToken ct = default);
    Task<IReadOnlyList<TEntity>> ListAsync(ISpecification<TEntity> spec, CancellationToken ct = default);
    Task<PagedResult<TEntity>> PagedListAsync(
        ISpecification<TEntity> spec, int pageNumber, int pageSize, CancellationToken ct = default);
}

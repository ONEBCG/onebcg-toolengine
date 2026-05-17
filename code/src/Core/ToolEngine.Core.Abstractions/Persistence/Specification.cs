namespace ToolEngine.Core.Abstractions.Persistence;

using System.Linq.Expressions;

/// <summary>
/// Convenience base for single-criteria specifications.
/// Callers can use the static factory to avoid creating a derived class per query.
/// </summary>
public abstract class Specification<T> : ISpecification<T>
{
    public abstract Expression<Func<T, bool>> Criteria { get; }
    public List<Expression<Func<T, object>>> Includes { get; } = [];
}

/// <summary>
/// Inline specification constructed from a lambda — use for simple one-off queries.
/// </summary>
public sealed class LambdaSpecification<T> : Specification<T>
{
    private readonly Expression<Func<T, bool>> _criteria;

    public LambdaSpecification(Expression<Func<T, bool>> criteria)
        => _criteria = criteria;

    public override Expression<Func<T, bool>> Criteria => _criteria;
}

namespace ToolEngine.Tools.Abstractions.Base;

using ToolEngine.Core.Abstractions.Persistence;
using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Core.Domain.Enums;
using ToolEngine.Core.Domain.Schema;
using ToolEngine.Tools.Abstractions.Interfaces;

/// <summary>
/// Base for database-backed tools. TEntity is the primary domain entity this
/// tool reads from or writes to. Injects IUnitOfWork for write operations and
/// IReadRepository for queries.
/// </summary>
public abstract class DatabaseToolBase<TInput, TOutput, TEntity, TId>
    : IToolHandler<TInput, TOutput>
    where TEntity : class
{
    protected readonly IUnitOfWork                    UnitOfWork;
    protected readonly IReadRepository<TEntity, TId> ReadRepository;

    protected DatabaseToolBase(
        IUnitOfWork                    uow,
        IReadRepository<TEntity, TId> repo)
    {
        UnitOfWork     = uow;
        ReadRepository = repo;
    }

    public abstract string    Namespace    { get; }
    public abstract string    Name         { get; }
    public          string    FullName     => $"{Namespace}.{Name}";
    public abstract string    Version      { get; }
    public abstract string    Description  { get; }
    public          ToolType  Type         => ToolType.Database;
    public abstract ToolSchema InputSchema  { get; }
    public abstract ToolSchema OutputSchema { get; }

    public abstract Task<ToolResponse<TOutput>> ExecuteAsync(
        ToolRequest<TInput> request,
        CancellationToken   ct = default);

    public async IAsyncEnumerable<ToolChunk<TOutput>> StreamAsync(
        ToolRequest<TInput> request,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken ct = default)
    {
        var response = await ExecuteAsync(request, ct);
        if (response.Success)
            yield return new ToolChunk<TOutput>(
                response.CorrelationId, response.Data!, 0, IsFinal: true, "done");
    }
}

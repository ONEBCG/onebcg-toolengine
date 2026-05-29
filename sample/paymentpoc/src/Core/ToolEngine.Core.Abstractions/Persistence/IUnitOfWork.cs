namespace ToolEngine.Core.Abstractions.Persistence;

// CRITICAL: extends IAsyncDisposable — NOT IDisposable.
// Callers MUST use: await using var uow = ...
// Using synchronous Dispose() throws InvalidOperationException at runtime (M2).
public interface IUnitOfWork : IAsyncDisposable
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

namespace ToolEngine.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore.Storage;
using ToolEngine.Core.Abstractions.Persistence;

internal sealed class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext       _ctx;
    private IDbContextTransaction?      _transaction;

    public UnitOfWork(AppDbContext ctx) => _ctx = ctx;

    public Task<int> SaveChangesAsync(CancellationToken ct = default) =>
        _ctx.SaveChangesAsync(ct);

    public async Task BeginTransactionAsync(CancellationToken ct = default) =>
        _transaction = await _ctx.Database.BeginTransactionAsync(ct);

    public async Task CommitTransactionAsync(CancellationToken ct = default)
    {
        if (_transaction is not null)
            await _transaction.CommitAsync(ct);
    }

    public async Task RollbackTransactionAsync(CancellationToken ct = default)
    {
        if (_transaction is not null)
            await _transaction.RollbackAsync(ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null)
            await _transaction.DisposeAsync();
        await _ctx.DisposeAsync();
    }
}

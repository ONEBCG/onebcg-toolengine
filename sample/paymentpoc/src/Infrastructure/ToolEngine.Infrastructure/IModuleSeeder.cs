using Microsoft.Extensions.Logging;
using ToolEngine.Infrastructure.Persistence;

namespace ToolEngine.Infrastructure;

/// <summary>
/// Implemented by each module to seed its domain data on startup.
/// Register as Transient via DI — startup code resolves IEnumerable&lt;IModuleSeeder&gt;
/// and calls SeedAsync on each in registration order.
/// </summary>
public interface IModuleSeeder
{
    Task SeedAsync(AppDbContext db, ILogger logger, CancellationToken ct = default);
}

using Microsoft.EntityFrameworkCore;

namespace ToolEngine.Infrastructure;

/// <summary>
/// Implemented by each module to contribute entity type configurations to AppDbContext.
/// Register as Singleton via DI — AppDbContext receives all implementations via
/// IEnumerable&lt;IModuleEntityConfiguration&gt; injection.
/// </summary>
public interface IModuleEntityConfiguration
{
    void Apply(ModelBuilder modelBuilder);
}

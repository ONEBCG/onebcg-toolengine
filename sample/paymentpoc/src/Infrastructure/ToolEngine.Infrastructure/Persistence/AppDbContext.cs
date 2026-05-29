using Microsoft.EntityFrameworkCore;
using ToolEngine.Core.Domain.Common;
using ToolEngine.Core.Domain.Entities;

namespace ToolEngine.Infrastructure.Persistence;

public sealed class AppDbContext : DbContext
{
    private readonly IEnumerable<ToolEngine.Infrastructure.IModuleEntityConfiguration> _moduleConfigs;

    // Runtime constructor — DI injects all registered IModuleEntityConfiguration instances
    public AppDbContext(
        DbContextOptions<AppDbContext> options,
        IEnumerable<ToolEngine.Infrastructure.IModuleEntityConfiguration> moduleConfigs)
        : base(options) => _moduleConfigs = moduleConfigs;

    // Design-time constructor — used by dotnet ef migrations
    // IDesignTimeDbContextFactory in the host project provides module configs at design time
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : this(options, []) { }

    // ── ToolEngine core tables ────────────────────────────────────────────────
    public DbSet<ToolInvocationRecord>  ToolInvocationRecords   => Set<ToolInvocationRecord>();
    public DbSet<ToolInvocationEvent>   ToolInvocationEvents    => Set<ToolInvocationEvent>();
    public DbSet<PendingApproval>       PendingApprovals        => Set<PendingApproval>();
    public DbSet<ScenarioExecution>     ScenarioExecutions      => Set<ScenarioExecution>();
    public DbSet<OutboxMessage>         OutboxMessages          => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        // DomainEvent is an abstract record used in-memory only (not persisted).
        mb.Ignore<DomainEvent>();

        // Engine entity configurations (ToolInvocationRecord, PendingApproval, etc.)
        mb.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Module entity configurations — each registered module contributes its own
        foreach (var moduleConfig in _moduleConfigs)
            moduleConfig.Apply(mb);

        base.OnModelCreating(mb);
    }
}

namespace ToolEngine.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using ToolEngine.Core.Domain.Entities;
using ToolEngine.Infrastructure.Persistence.Entities;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Tenant>               Tenants               => Set<Tenant>();
    public DbSet<ToolInvocationRecord> ToolInvocationRecords => Set<ToolInvocationRecord>();
    /// <summary>
    /// H1 — Append-only SOC 2 audit event log. Application DB user has INSERT permission only.
    /// One row per lifecycle transition (Invoked → Running → Succeeded/Failed/Suspended).
    /// </summary>
    public DbSet<ToolInvocationEvent>  ToolInvocationEvents  => Set<ToolInvocationEvent>();
    public DbSet<PendingApproval>      PendingApprovals      => Set<PendingApproval>();
    /// <summary>Outbox messages pending channel dispatch. Written atomically with PendingApproval.</summary>
    public DbSet<OutboxMessage>        OutboxMessages        => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        base.OnModelCreating(builder);
    }
}

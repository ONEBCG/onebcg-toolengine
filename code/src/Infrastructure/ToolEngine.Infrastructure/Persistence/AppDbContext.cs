namespace ToolEngine.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using ToolEngine.Core.Domain.Entities;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Tenant>               Tenants               => Set<Tenant>();
    public DbSet<ToolInvocationRecord> ToolInvocationRecords => Set<ToolInvocationRecord>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        base.OnModelCreating(builder);
    }
}

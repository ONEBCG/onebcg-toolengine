using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql.EntityFrameworkCore.PostgreSQL;
using ToolEngine.Infrastructure.Persistence;
using ToolEngine.Payment.Infrastructure;

namespace ToolEngine.Api.Persistence;

/// <summary>
/// Design-time factory used by the dotnet-ef CLI when running migrations from the API host project.
/// Overrides the base factory in ToolEngine.Infrastructure to include all module entity configurations,
/// ensuring Payment (and future module) tables are captured in migrations.
///
/// Usage (from solution root):
///   $env:Database__Provider = "sqlite"
///   dotnet ef migrations add MigrationName \
///     --project src/Infrastructure/ToolEngine.Infrastructure \
///     --startup-project src/Hosts/ToolEngine.Api
/// </summary>
public sealed class ApiDesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var provider = Environment.GetEnvironmentVariable("Database__Provider")
                    ?? Environment.GetEnvironmentVariable("Database:Provider")
                    ?? "sqlite";

        var connectionString = Environment.GetEnvironmentVariable("Database__ConnectionString")
                             ?? Environment.GetEnvironmentVariable("Database:ConnectionString")
                             ?? "Data Source=toolengine-design.db";

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();

        if (provider.Equals("postgres", StringComparison.OrdinalIgnoreCase))
            optionsBuilder.UseNpgsql(connectionString,
                pg => pg.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName));
        else if (provider.Equals("sqlserver", StringComparison.OrdinalIgnoreCase))
            optionsBuilder.UseSqlServer(connectionString,
                sql => sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName));
        else
            optionsBuilder.UseSqlite(connectionString,
                sql => sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName));

        // All module entity configurations — extend this list as new modules are added.
        return new AppDbContext(optionsBuilder.Options,
        [
            new PaymentModuleEntityConfiguration(),
        ]);
    }
}

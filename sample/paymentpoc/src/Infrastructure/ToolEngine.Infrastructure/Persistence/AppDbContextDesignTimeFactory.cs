using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql.EntityFrameworkCore.PostgreSQL;

namespace ToolEngine.Infrastructure.Persistence;

/// <summary>
/// Design-time factory used exclusively by the dotnet-ef CLI tool for migration generation.
/// Reads Database__Provider and Database__ConnectionString from environment variables so that
/// migrations can be generated for any provider (postgres, sqlite) without running the full app.
///
/// Usage (from solution root):
///   $env:Database__Provider = "postgres"
///   $env:Database__ConnectionString = "Host=...;..."
///   dotnet ef migrations add MigrationName \
///     --project src/Infrastructure/ToolEngine.Infrastructure \
///     --startup-project src/Hosts/ToolEngine.Api
/// </summary>
public sealed class AppDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
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
            optionsBuilder.UseSqlite(connectionString);

        // Module configurations are injected here at design time.
        // Subclass this factory in each host project (see ToolEngine.Api/Persistence/ApiDesignTimeDbContextFactory.cs)
        // to include module-specific entity configurations.
        return new AppDbContext(optionsBuilder.Options, []);
    }
}

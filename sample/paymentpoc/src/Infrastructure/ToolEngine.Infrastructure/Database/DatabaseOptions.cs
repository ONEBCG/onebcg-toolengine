namespace ToolEngine.Infrastructure.Database;

/// <summary>
/// Typed configuration for the database provider.
/// Reads from appsettings section "Database".
/// Provider values: "sqlite" (default) | "sqlserver" | "postgres"
/// </summary>
public sealed class DatabaseOptions
{
    public const string Section = "Database";

    public string Provider         { get; init; } = "sqlite";
    public string ConnectionString { get; init; } = "Data Source=toolengine.db";
}

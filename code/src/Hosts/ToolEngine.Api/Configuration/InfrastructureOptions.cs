namespace ToolEngine.Api.Configuration;

/// <summary>
/// Configuration section "Database".
/// Provider values: "sqlite" (default/dev), "postgresql", "sqlserver".
/// </summary>
public sealed class DatabaseOptions
{
    public string Provider { get; set; } = "sqlite";
}

/// <summary>
/// Configuration section "Cache".
/// Provider values: "memory" (default/dev), "redis".
/// When redis is selected, "ConnectionStrings:Redis" must also be configured.
/// </summary>
public sealed class CacheOptions
{
    public string Provider { get; set; } = "memory";
}

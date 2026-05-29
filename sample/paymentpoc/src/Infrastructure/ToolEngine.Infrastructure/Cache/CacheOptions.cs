namespace ToolEngine.Infrastructure.Cache;

/// <summary>
/// Typed configuration for the cache provider.
/// Reads from appsettings section "Cache".
/// Provider values: "memory" (default) | "redis"
/// </summary>
public sealed class CacheOptions
{
    public const string Section = "Cache";

    public string      Provider { get; init; } = "memory";
    public RedisOptions Redis   { get; init; } = new();
}

public sealed class RedisOptions
{
    public string ConnectionString { get; init; } = "localhost:6379";
    public string InstanceName     { get; init; } = "toolengine:";
}

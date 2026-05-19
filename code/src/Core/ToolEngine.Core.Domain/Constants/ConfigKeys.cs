namespace ToolEngine.Core.Domain.Constants;

/// <summary>
/// Configuration section and connection-string key names used in appsettings.json.
///
/// Changing a section name in appsettings without updating the code that reads it
/// produces a silent null — the app starts but behaves as if the setting is absent.
/// Centralising these makes the mismatch a compile error instead.
/// </summary>
public static class ConfigKeys
{
    // ── Connection strings ────────────────────────────────────────────────────

    /// <summary>Name of the default database connection string in ConnectionStrings.*.</summary>
    public const string DefaultConnection = "Default";

    /// <summary>Name of the Redis connection string in ConnectionStrings.*.</summary>
    public const string RedisConnection = "Redis";

    // ── Configuration sections ────────────────────────────────────────────────

    /// <summary>Section that configures the database provider and options (Database.Provider).</summary>
    public const string Database = "Database";

    /// <summary>Section that configures the cache provider (Cache.Provider).</summary>
    public const string Cache = "Cache";

    /// <summary>Section that carries the OTLP exporter endpoint (Otlp.Endpoint).</summary>
    public const string OtlpEndpoint = "Otlp:Endpoint";

    /// <summary>Section that carries JWT validation settings (Jwt.*).</summary>
    public const string Jwt = "Jwt";
}

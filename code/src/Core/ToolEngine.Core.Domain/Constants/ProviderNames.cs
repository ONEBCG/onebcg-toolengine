namespace ToolEngine.Core.Domain.Constants;

/// <summary>
/// Lowercase provider name strings used for runtime comparisons in the infrastructure layer.
///
/// These strings appear in configuration (appsettings.json) and in provider-selection logic.
/// Centralising them avoids case-sensitivity bugs when operators change a config value
/// (e.g. "Sqlite" vs "sqlite") and lets the router and tests share the same canonical form.
/// </summary>
public static class ProviderNames
{
    // ── Database providers ────────────────────────────────────────────────────

    /// <summary>SQLite provider — local development and single-node deployments.</summary>
    public const string Sqlite = "sqlite";

    /// <summary>PostgreSQL provider — recommended for production multi-tenant workloads.</summary>
    public const string PostgreSql = "postgresql";

    /// <summary>SQL Server provider — enterprise on-premises or Azure SQL environments.</summary>
    public const string SqlServer = "sqlserver";

    // ── Cache providers ───────────────────────────────────────────────────────

    /// <summary>In-process memory cache — single-pod only; not safe for horizontally-scaled deployments.</summary>
    public const string Memory = "memory";

    /// <summary>Redis distributed cache — required when running more than one pod instance.</summary>
    public const string Redis = "redis";

    // ── LLM providers ─────────────────────────────────────────────────────────

    /// <summary>Anthropic Claude provider (primary).</summary>
    public const string Anthropic = "anthropic";

    /// <summary>OpenAI GPT provider (fallback).</summary>
    public const string OpenAi = "openai";

    /// <summary>Ollama local provider — offline development and air-gapped environments.</summary>
    public const string Ollama = "ollama";

    // ── Anthropic versioning ──────────────────────────────────────────────────

    /// <summary>
    /// Pinned Anthropic API version string sent in every request.
    /// Anthropic uses a date-based version header to gate breaking changes;
    /// this pin ensures the response schema remains stable until we explicitly upgrade.
    /// </summary>
    public const string AnthropicApiVersion = "2023-06-01";
}

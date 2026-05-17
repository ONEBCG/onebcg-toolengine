namespace ToolEngine.Core.Domain.Contracts;

using ToolEngine.Core.Domain.Enums;

/// <summary>
/// The single entry contract for every tool invocation — REST, CLI, or internal.
/// CorrelationId: caller-generated trace ID propagated through logs and responses.
/// TenantId: human-readable tenant slug, e.g. "acme-corp".
/// ToolVersion: pinned version string, e.g. "v1". Use "latest" for auto-resolve.
/// Always created by the interface layer (API or CLI), never by a handler or tool.
///
/// 2026 additions: MaxResponseTokens and ResponseFormat enforce token budgets
/// per Anthropic and OpenAI guidance (max 25,000 tokens per tool response).
/// ToolNamespace enables namespace.name routing when set.
/// </summary>
public sealed record ToolRequest<TInput>(
    Guid   CorrelationId,
    string TenantId,
    string ToolName,
    string ToolVersion,
    TInput Input,
    ExecutionMode  Mode             = ExecutionMode.Sequential,
    bool           Streaming        = false,
    string?        UserId           = null,
    IDictionary<string, string>? Metadata = null,
    // Hard ceiling on response size in tokens. Default 25,000 per Anthropic guidance.
    int            MaxResponseTokens = 25_000,
    // "Concise" = compact summary ~500 tokens. "Detailed" = full response up to MaxResponseTokens.
    ResponseFormat ResponseFormat   = ResponseFormat.Concise,
    // Tool namespace, e.g. "weather" or "math". Used for namespace.name routing in Phase B+.
    string         ToolNamespace    = "")
{
    /// <summary>Fully-qualified tool name: "namespace.name" when ToolNamespace is set.</summary>
    public string FullName => string.IsNullOrEmpty(ToolNamespace)
        ? ToolName
        : $"{ToolNamespace}.{ToolName}";
}

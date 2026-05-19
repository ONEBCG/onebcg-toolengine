namespace ToolEngine.Core.Domain.Constants;

/// <summary>
/// HTTP header name strings used when reading request headers or setting response headers.
///
/// Centralising these prevents the same header string from drifting between the provider,
/// the endpoint, and integration tests. Header names are case-insensitive by spec
/// but some HTTP/2 stacks reject non-lowercase names — keep them lowercase here.
/// </summary>
public static class HttpHeaderNames
{
    // ── Inbound request headers ───────────────────────────────────────────────

    /// <summary>
    /// Carries the LLM provider override (e.g. "anthropic", "openai").
    /// Allows callers to pin a specific provider for a single request without
    /// changing tenant-level routing configuration.
    /// </summary>
    public const string LlmProviderOverride = "X-Llm-Provider";

    /// <summary>
    /// Caller-supplied correlation identifier for distributed tracing.
    /// If absent, the endpoint generates a new Guid and assigns it to the request.
    /// </summary>
    public const string CorrelationId = "X-Correlation-Id";

    // ── Outbound response headers ─────────────────────────────────────────────

    /// <summary>
    /// Returned with 429 responses to indicate when the client may retry.
    /// Value is seconds. Matches RFC 7231 §7.1.3.
    /// </summary>
    public const string RetryAfter = "Retry-After";

    // ── Anthropic provider headers ────────────────────────────────────────────

    /// <summary>
    /// Anthropic API authentication header. Carries the raw API key (no "Bearer" prefix).
    /// Value is resolved from the environment variable named in ProviderOptions.ApiKeyEnvVar.
    /// </summary>
    public const string AnthropicApiKey = "x-api-key";

    /// <summary>
    /// Anthropic API version selector. Must match a date string Anthropic publishes
    /// (currently "2023-06-01"). Pinning a version prevents unannounced breaking changes
    /// from affecting the response schema at runtime.
    /// </summary>
    public const string AnthropicVersion = "anthropic-version";
}

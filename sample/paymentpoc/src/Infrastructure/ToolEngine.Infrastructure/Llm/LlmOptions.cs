namespace ToolEngine.Infrastructure.Llm;

/// <summary>
/// Typed configuration for the LLM provider.
/// Reads from appsettings section "LLM".
/// Provider values: "claude" (default) | "openai"
/// API keys can also be set via environment variables:
///   ANTHROPIC_API_KEY  (for Claude)
///   OPENAI_API_KEY     (for OpenAI)
/// Environment variables take precedence over empty appsettings values.
/// </summary>
public sealed class LlmOptions
{
    public const string Section = "LLM";

    public string       Provider  { get; init; } = "claude";
    /// <summary>
    /// When true, the LLM provider emits ToolStartedEvent / ToolCompletedEvent
    /// during the agentic loop so the caller can stream progress to clients via SSE.
    /// </summary>
    public bool         Streaming { get; init; } = false;
    public ClaudeOptions Claude  { get; init; } = new();
    public OpenAiOptions OpenAI  { get; init; } = new();
}

public sealed record ClaudeOptions
{
    public string ApiKey  { get; init; } = string.Empty;
    public string Model   { get; init; } = "claude-sonnet-4-6";
    public string BaseUrl { get; init; } = "https://api.anthropic.com/v1/messages";
    // Note: anthropic-version header is hardcoded in ClaudeProvider (API protocol constant).
}

public sealed record OpenAiOptions
{
    public string ApiKey  { get; init; } = string.Empty;
    public string Model   { get; init; } = "gpt-4o";
    public string BaseUrl { get; init; } = "https://api.openai.com/v1/chat/completions";
}

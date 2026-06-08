namespace ToolEngine.Infrastructure.Llm;

/// <summary>
/// Typed configuration for the LLM provider.
/// Reads from appsettings section "LLM".
/// Provider values: "claude" (default) | "openai" | "gemini"
/// API keys can also be set via environment variables:
///   ANTHROPIC_API_KEY  (for Claude)
///   OPENAI_API_KEY     (for OpenAI)
///   GOOGLE_API_KEY     (for Gemini)
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
    /// <summary>
    /// When true (default): the LLM selects tools autonomously based on tool descriptions
    /// and data-flow semantics. The system prompt provides context only — no step sequencing.
    ///
    /// When false: the system prompt includes an explicit WORKFLOW section that instructs
    /// the model to call tools in a prescribed order. Use when strict sequencing is required
    /// or the model needs more guidance for complex flows.
    ///
    /// Configure in appsettings: LLM:AutonomousToolSelection
    /// </summary>
    public bool AutonomousToolSelection { get; init; } = true;
    public ClaudeOptions  Claude  { get; init; } = new();
    public OpenAiOptions  OpenAI  { get; init; } = new();
    public GeminiOptions  Gemini  { get; init; } = new();
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

public sealed record GeminiOptions
{
    public string ApiKey  { get; init; } = string.Empty;
    // Recommended stable model: gemini-2.0-flash (fast, function-calling capable).
    // For higher capability: gemini-2.5-pro or gemini-2.5-flash.
    // Verify current model list at: https://ai.google.dev/gemini-api/docs/models
    public string Model   { get; init; } = "gemini-2.5-flash";
    // Base URL — model name and :generateContent are appended at request time.
    // Auth is via ?key= query parameter (not an Authorization header) on the v1beta REST API.
    public string BaseUrl { get; init; } = "https://generativelanguage.googleapis.com/v1beta";
}

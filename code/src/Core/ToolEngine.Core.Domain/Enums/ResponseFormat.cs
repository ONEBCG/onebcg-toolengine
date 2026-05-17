namespace ToolEngine.Core.Domain.Enums;

/// <summary>
/// Controls how verbose a tool's response is.
/// Per Anthropic guidance: expose this to let agents manage context window usage.
/// </summary>
public enum ResponseFormat
{
    /// <summary>Compact summary, approximately 500 tokens. Default.</summary>
    Concise,
    /// <summary>Full detail, up to MaxResponseTokens. Use when agent needs all fields.</summary>
    Detailed
}

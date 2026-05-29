using System.Text.Json;

namespace ToolEngine.Core.Abstractions.Llm;

/// <summary>A tool the LLM can call, in provider-agnostic format.</summary>
public sealed record LlmTool(
    string      Name,
    string      Description,
    JsonElement InputSchema);

/// <summary>One tool call made by the LLM during an agentic loop turn.</summary>
public sealed record LlmToolCall(
    string      ToolName,
    string      FullName,
    JsonElement Input,
    string      OutputJson,
    bool        Success,
    bool        Suspended);

/// <summary>
/// Final response returned after the agentic loop completes for one user message.
/// Contains the model's reply text and a log of all tool calls made.
/// </summary>
public sealed class LlmChatResponse
{
    public bool                       IsSuccess    { get; private init; }
    public string                     Reply        { get; private init; } = string.Empty;
    public IReadOnlyList<LlmToolCall> ToolCalls    { get; private init; } = [];
    public string?                    Error        { get; private init; }
    public int                        InputTokens  { get; private init; }
    public int                        OutputTokens { get; private init; }

    public static LlmChatResponse Success(string reply, IReadOnlyList<LlmToolCall> calls,
                                          int inputTokens = 0, int outputTokens = 0)
        => new() { IsSuccess = true, Reply = reply, ToolCalls = calls,
                   InputTokens = inputTokens, OutputTokens = outputTokens };

    public static LlmChatResponse Failure(string error)
        => new() { IsSuccess = false, Error = error };

    /// <summary>
    /// Returned when no LLM provider is configured.
    /// Set LLM:Provider + the corresponding API key in appsettings.
    /// </summary>
    public static LlmChatResponse NotConfigured()
        => new()
        {
            IsSuccess = false,
            Error     = "LLM provider not configured. " +
                        "Set LLM:Provider (\"claude\" or \"openai\") and the corresponding " +
                        "API key in appsettings.json or environment variables.",
        };
}

// ── Streaming events ──────────────────────────────────────────────────────────
// Emitted by ILlmProvider during the agentic loop when LLM:Streaming = true.
// ChatController streams these to the browser as Server-Sent Events.

/// <summary>Base type for all events emitted during a streaming agentic loop.</summary>
public abstract record LlmStreamEvent;

/// <summary>Emitted when the LLM requests a tool call — before execution begins.</summary>
public sealed record ToolStartedEvent(
    string      ToolName,
    JsonElement Input) : LlmStreamEvent;

/// <summary>Emitted after a tool call completes (success, failure, or suspension).</summary>
public sealed record ToolCompletedEvent(
    string ToolName,
    string OutputJson,
    bool   Success,
    bool   Suspended) : LlmStreamEvent;

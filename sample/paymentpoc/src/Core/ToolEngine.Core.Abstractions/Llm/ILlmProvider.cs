using System.Text.Json;

namespace ToolEngine.Core.Abstractions.Llm;

/// <summary>
/// Provider-agnostic LLM interface.
/// Registered implementations: ClaudeProvider, OpenAiProvider, NullLlmProvider.
/// Selected at startup based on LLM:Provider configuration.
///
/// The provider owns the full agentic loop for one user message:
///   1. Send user message to the model with available tools.
///   2. If the model returns tool calls, execute them via the executeTool callback.
///   3. Return tool results to the model and repeat until a final text reply.
/// ChatService calls this once per user message and receives the complete response.
/// </summary>
public interface ILlmProvider
{
    /// <summary>Identifies this provider — "claude", "openai", or "none".</summary>
    string ProviderName { get; }

    /// <summary>
    /// Run the agentic loop for one user message.
    /// When <paramref name="onStream"/> is provided, the provider emits
    /// <see cref="ToolStartedEvent"/> and <see cref="ToolCompletedEvent"/> in real time
    /// so callers can stream progress to the browser via SSE.
    /// </summary>
    /// <param name="userMessage">The user's natural-language message.</param>
    /// <param name="tools">Tools the model can call.</param>
    /// <param name="executeTool">
    /// Callback the provider calls when the model requests a tool.
    /// Receives the tool name (in provider format) and parsed input.
    /// Returns a JSON string result to pass back to the model.
    /// </param>
    /// <param name="systemPrompt">System-level instruction for the model.</param>
    /// <param name="onStream">Optional callback invoked for each streaming event.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<LlmChatResponse> ChatAsync(
        string                                  userMessage,
        IReadOnlyList<LlmTool>                 tools,
        Func<string, JsonElement, Task<string>> executeTool,
        string                                  systemPrompt,
        Func<LlmStreamEvent, Task>?            onStream = null,
        CancellationToken                       ct       = default);
}

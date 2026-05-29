using System.Text.Json;
using ToolEngine.Core.Abstractions.Llm;

namespace ToolEngine.Infrastructure.Llm;

/// <summary>
/// No-op LLM provider registered when no API key is configured.
/// Returns a clear error rather than throwing at resolution time.
/// </summary>
public sealed class NullLlmProvider : ILlmProvider
{
    public string ProviderName => "none";

    public Task<LlmChatResponse> ChatAsync(
        string                                  userMessage,
        IReadOnlyList<LlmTool>                 tools,
        Func<string, JsonElement, Task<string>> executeTool,
        string                                  systemPrompt,
        Func<LlmStreamEvent, Task>?            onStream = null,
        CancellationToken                       ct       = default)
        => Task.FromResult(LlmChatResponse.NotConfigured());
}

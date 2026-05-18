namespace ToolEngine.Llm.Abstractions;

using ToolEngine.Llm.Models;
using ToolEngine.Llm.Options;

public interface ILlmProvider
{
    string ProviderName { get; }

    Task<LlmResponse> CompleteAsync(
        IReadOnlyList<LlmMessage>        messages,
        IReadOnlyList<LlmToolDefinition> tools,
        ProviderOptions                  options,
        CancellationToken                ct = default);
}

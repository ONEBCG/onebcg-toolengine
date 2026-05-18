namespace ToolEngine.Llm.Abstractions;

using ToolEngine.Llm.Models;
using ToolEngine.Llm.Options;
using ToolEngine.Tools.Registry;

public interface IProviderRouter
{
    /// <summary>
    /// Selects an LLM provider.
    /// Precedence: toolDescriptor[LlmProvider attribute] > tenant.LlmProviderOverride > Llm:Routing:DefaultProvider
    /// </summary>
    (ILlmProvider Provider, ProviderOptions Options) Select(
        string?         tenantProviderOverride,
        ToolDescriptor? toolDescriptor = null);
}

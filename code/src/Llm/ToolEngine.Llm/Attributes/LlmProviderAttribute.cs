namespace ToolEngine.Llm.Attributes;

/// <summary>
/// Forces a specific LLM provider for tools decorated with this attribute.
/// Overrides both tenant config and global default.
/// Usage: [LlmProvider("ollama")]
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class LlmProviderAttribute : Attribute
{
    public string ProviderName { get; }
    public LlmProviderAttribute(string providerName) => ProviderName = providerName;
}

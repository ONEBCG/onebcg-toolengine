namespace ToolEngine.Llm.Options;

public sealed class RoutingOptions
{
    public string   DefaultProvider { get; set; } = "anthropic";
    public string[] FallbackChain   { get; set; } = [];
}

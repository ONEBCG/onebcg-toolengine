namespace ToolEngine.Llm.Options;

public sealed class LlmOptions
{
    public RoutingOptions                      Routing    { get; set; } = new();
    public BudgetOptions                       Budget     { get; set; } = new();
    public ToolGuardOptions                    ToolGuard  { get; set; } = new();
    public ScopeGuardOptions                   ScopeGuard { get; set; } = new();
    public Dictionary<string, ProviderOptions> Providers  { get; set; } = new();
}

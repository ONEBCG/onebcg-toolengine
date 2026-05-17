namespace ToolEngine.Core.Abstractions.Common;

public interface ITenantContext
{
    string                TenantId              { get; }
    string                TenantName            { get; }
    /// <summary>Allowed tool names in namespace.name format, e.g. "payment.validate".</summary>
    IReadOnlyList<string> AllowedTools          { get; }
    /// <summary>Allowed namespaces. If empty, all namespaces are permitted.</summary>
    IReadOnlyList<string> AllowedNamespaces     { get; }
    string?               LlmProviderOverride   { get; }
    int                   MaxResponseTokens     { get; }
    int                   DailyToolCallBudget   { get; }
}

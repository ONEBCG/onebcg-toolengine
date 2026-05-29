using ToolEngine.Core.Domain.Enums;

namespace ToolEngine.Tools.Abstractions.Attributes;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RequiresApprovalAttribute(
    ApprovalRisk    Risk    = ApprovalRisk.Medium,
    ApprovalChannel Channel = ApprovalChannel.Dashboard,
    string          Reason  = "This action requires human approval.") : Attribute
{
    public ApprovalRisk    Risk    { get; } = Risk;
    public ApprovalChannel Channel { get; } = Channel;
    public string          Reason  { get; } = Reason;
}

// Phase L: per-tool LLM routing override for data-residency constraints.
// [LlmProvider("ollama")] on a handler class forces all LLM selections for
// that tool to route to the specified provider, regardless of tenant config.
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class LlmProviderAttribute(string Provider) : Attribute
{
    public string Provider { get; } = Provider;
}

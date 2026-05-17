namespace ToolEngine.Core.Domain.Attributes;

using ToolEngine.Core.Domain.Enums;

/// <summary>
/// Marks a tool as requiring human approval before execution.
/// The HumanApprovalGuard reads this attribute and gates the tool call
/// until an IHumanApprovalGate implementation resolves the decision.
///
/// Per NIST Cyber AI Profile (Dec 2025): irreversible or high-impact
/// actions must have human approval controls outside the agent loop.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RequiresApprovalAttribute(
    string       Reason,
    ApprovalRisk Risk = ApprovalRisk.High) : Attribute
{
    public string       Reason { get; } = Reason;
    public ApprovalRisk Risk   { get; } = Risk;
}

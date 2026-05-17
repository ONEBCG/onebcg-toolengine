namespace ToolEngine.Application.Abstractions;

using ToolEngine.Core.Domain.Enums;

/// <summary>
/// Non-generic marker interface. Implemented by ExecuteToolCommand&lt;TIn, TOut&gt;.
/// Allows pipeline behaviors to inspect command properties without generic type constraints.
/// </summary>
public interface IExecuteToolCommand
{
    Guid     CorrelationId    { get; }
    string   TenantId         { get; }
    string   UserId           { get; }
    string   ToolNamespace    { get; }
    string   ToolName         { get; }
    string   ToolVersion      { get; }
    ToolType ToolType          { get; }
    // Cap on LLM response tokens. Enforced by TokenBudgetBehavior against Tenant.MaxResponseTokens.
    int      MaxResponseTokens { get; }
    // Optional idempotency key. When set, AsyncApprovalGate returns the existing PendingApproval
    // if one already exists for the same key + tenant + tool. Prevents duplicate approvals on retry.
    string?  IdempotencyKey    { get; }
    // H4 — Caller identity: Human / AiAgent / SystemService. Sourced from JWT claim "caller_type".
    CallerType CallerType { get; }
    // H5 — ISO 42001 AI governance metadata JSON. Verbatim from X-Governance-Metadata header.
    string?  GovernanceMetadataJson { get; }
}

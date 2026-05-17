namespace ToolEngine.Tools.Abstractions.Interfaces;

using ToolEngine.Core.Domain.Enums;

/// <summary>
/// Human-in-the-loop gate for tools marked [RequiresApproval].
/// Per NIST Cyber AI Profile (Dec 2025): irreversible or high-impact actions
/// must have a deterministic control outside the agent reasoning loop.
///
/// Implementations:
/// - CLI: prompts the operator on the console
/// - API: sends a webhook and awaits callback
/// - SignalR: pushes to a live dashboard
/// </summary>
public interface IHumanApprovalGate
{
    Task<ApprovalDecision> RequestApprovalAsync(
        Guid              correlationId,
        string            toolFullName,
        string            reason,
        ApprovalRisk      risk,
        object?           inputSummary,
        CancellationToken ct = default);
}

public sealed record ApprovalDecision(
    bool   Approved,
    string ApproverUserId,
    string? Reason    = null,
    DateTimeOffset DecidedAt = default)
{
    public static ApprovalDecision Allow(string approver) =>
        new(true, approver, DecidedAt: DateTimeOffset.UtcNow);

    public static ApprovalDecision Deny(string approver, string reason) =>
        new(false, approver, reason, DateTimeOffset.UtcNow);
}

namespace ToolEngine.Tools.Abstractions.Interfaces;

using ToolEngine.Core.Domain.Enums;

/// <summary>
/// Human-in-the-loop gate for tools marked [RequiresApproval].
/// Per NIST Cyber AI Profile (Dec 2025): irreversible or high-impact actions
/// must have a deterministic control outside the agent reasoning loop.
///
/// Implementations:
/// - ConsoleApprovalGate (CLI): synchronous Spectre.Console prompt
/// - AsyncApprovalGate (API): creates PendingApproval, sends channel notification,
///   returns ApprovalDecision.Suspend() — execution resumes via callback
/// </summary>
public interface IHumanApprovalGate
{
    Task<ApprovalDecision> RequestApprovalAsync(
        ApprovalContext   context,
        string            reason,
        ApprovalRisk      risk,
        object?           inputSummary,
        CancellationToken ct = default);
}

/// <summary>
/// Caller-provided context for a human approval request.
/// ApprovalBehavior populates this from the IExecuteToolCommand fields.
/// </summary>
public sealed record ApprovalContext(
    Guid    CorrelationId,
    string  TenantId,
    string  UserId,
    string  ToolNamespace,
    string  ToolName,
    string  ToolVersion,
    string? ApproverEmail    = null,
    string? IdempotencyKey   = null)
{
    public string ToolFullName => $"{ToolNamespace}.{ToolName}";
}

public sealed record ApprovalDecision(
    bool           Approved,
    string         ApproverUserId,
    string?        Reason              = null,
    DateTimeOffset DecidedAt           = default,
    // True when decision is deferred to async channel (email, webhook, dashboard).
    // Execution is suspended; poll GET /invocations/{PendingInvocationId}/status.
    bool           Pending             = false,
    Guid?          PendingInvocationId = null)
{
    public static ApprovalDecision Allow(string approver) =>
        new(true, approver, DecidedAt: DateTimeOffset.UtcNow);

    public static ApprovalDecision Deny(string approver, string reason) =>
        new(false, approver, reason, DateTimeOffset.UtcNow);

    // Used by AsyncApprovalGate — execution suspended, awaiting out-of-band decision.
    public static ApprovalDecision Suspend(Guid invocationId) =>
        new(Approved: false, ApproverUserId: "system",
            Pending: true, PendingInvocationId: invocationId);
}

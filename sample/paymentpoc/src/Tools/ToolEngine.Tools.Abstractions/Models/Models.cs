using System.Text.Json;
using ToolEngine.Core.Domain.Enums;

namespace ToolEngine.Tools.Abstractions.Models;

// ── ToolSchema ───────────────────────────────────────────────────────────────
// WhenToUse and WhenNotToUse embedded into LLM descriptions by ToolSchemaConverter (Phase L)

public sealed record ToolSchema(
    string      Description,
    string      WhenToUse,
    string      WhenNotToUse,
    string[]    Examples,
    JsonElement InputSchema,
    JsonElement OutputSchema);

// ── ToolDescriptor ───────────────────────────────────────────────────────────

public sealed class ToolDescriptor
{
    public string    Namespace   { get; init; } = default!;
    public string    Name        { get; init; } = default!;
    public string    Version     { get; init; } = default!;
    public ToolType  Type        { get; init; }
    public bool      IsEnabled   { get; init; } = true;
    public Type      HandlerType { get; init; } = default!;
    public ToolSchema Schema     { get; init; } = default!;

    // Always "namespace.name" — registry lookup key
    public string FullName => string.IsNullOrEmpty(Namespace) ? Name : $"{Namespace}.{Name}";
}

// ── ToolSummaryResponse — FLAT DTO for GET /tools ────────────────────────────
// FLAT record — NO nested metadata sub-object. Contract drift breaks UI silently (M6).
// TypeScript interface in frontend/src/types.ts MUST mirror this exactly.

public sealed record ToolSummaryResponse(
    string      FullName,
    string      Namespace,
    string      Name,
    string      Version,
    string      Description,
    int         Type,         // int, not enum, for JSON serialisation simplicity
    bool        IsEnabled,
    JsonElement InputSchema,
    JsonElement OutputSchema);

// ── ToolPlan, ToolStep, ToolPlanResult ───────────────────────────────────────

public sealed record ToolPlan(Guid PlanId, ExecutionMode Mode, IReadOnlyList<ToolStep> Steps);

public sealed record ToolStep(
    string                      StepId,
    string                      Namespace,
    string                      ToolName,
    string                      Version,
    JsonElement                 Input,
    string[]                    DependsOn,
    Dictionary<string, string>? OutputMappings = null);

public sealed record ToolPlanResult(
    string       StepId,
    bool         Success,
    JsonElement? Data,
    ToolError?   Error);

// ── ApprovalContext ──────────────────────────────────────────────────────────

public sealed record ApprovalContext(
    string              ToolFullName,
    ApprovalRisk        Risk,
    ApprovalChannel     Channel,
    string?             UserId,
    string?             Reason,
    string?             IdempotencyKey,  // F8 — dedup in AsyncApprovalGate
    CallerType          CallerType,      // H4 — persisted on PendingApproval
    AcknowledgementStatement? AcknowledgementStatement); // H3

// ── ApprovalDecision ─────────────────────────────────────────────────────────

public sealed record ApprovalDecision
{
    public bool    IsAllowed    { get; private init; }
    public bool    IsDenied     { get; private init; }
    public bool    IsSuspended  { get; private init; }
    public string? DenialReason { get; private init; }
    public Guid?   InvocationId { get; private init; }

    private ApprovalDecision() { }

    public static ApprovalDecision Allowed()                           => new() { IsAllowed   = true };
    public static ApprovalDecision Denied(string reason)               => new() { IsDenied    = true, DenialReason = reason };
    public static ApprovalDecision Suspended(Guid invocationId)        => new() { IsSuspended = true, InvocationId = invocationId };
}

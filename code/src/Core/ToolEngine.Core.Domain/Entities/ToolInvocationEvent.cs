namespace ToolEngine.Core.Domain.Entities;

using ToolEngine.Core.Domain.Common;
using ToolEngine.Core.Domain.Enums;

/// <summary>
/// Append-only audit event. One row per lifecycle transition of a tool invocation.
///
/// Design principles:
/// - No mutation methods — all properties are init-only once created.
/// - The application DB user should be granted INSERT only on this table (no UPDATE/DELETE).
///   Enforced at the database level; see deployment runbook §4 "Database Hardening".
/// - Multiple events per invocation are expected (Invoked → Running → Succeeded/Failed/Suspended).
/// - CallerType persists the Human / AiAgent distinction required by EU AI Act Article 14.
/// - GovernanceMetadataJson carries ISO 42001 context supplied by the caller.
///
/// SOC 2 CC6.2 — access control evidence.
/// NIST Cyber AI Profile — traceability of AI agent actions.
/// </summary>
public sealed class ToolInvocationEvent : Entity<Guid>
{
    private ToolInvocationEvent(
        Guid                id,
        Guid                correlationId,
        Guid                invocationRecordId,
        string              tenantId,
        string              userId,
        CallerType          callerType,
        string              toolNamespace,
        string              toolName,
        string              toolVersion,
        InvocationEventType eventType,
        DateTimeOffset      occurredAt,
        double?             durationMs,
        string?             errorCode,
        string?             errorMessage,
        string?             governanceMetadataJson)
        : base(id)
    {
        CorrelationId          = correlationId;
        InvocationRecordId     = invocationRecordId;
        TenantId               = tenantId;
        UserId                 = userId;
        CallerType             = callerType;
        ToolNamespace          = toolNamespace;
        ToolName               = toolName;
        ToolVersion            = toolVersion;
        EventType              = eventType;
        OccurredAt             = occurredAt;
        DurationMs             = durationMs;
        ErrorCode              = errorCode;
        ErrorMessage           = errorMessage;
        GovernanceMetadataJson = governanceMetadataJson;
    }

    private ToolInvocationEvent() { } // EF Core

    // ── Identity ─────────────────────────────────────────────────────────────
    public Guid   CorrelationId      { get; private set; }
    /// <summary>FK to ToolInvocationRecord.Id — links events to the mutable audit record.</summary>
    public Guid   InvocationRecordId { get; private set; }

    // ── Principal ────────────────────────────────────────────────────────────
    public string      TenantId   { get; private set; } = default!;
    public string      UserId     { get; private set; } = default!;
    /// <summary>H4 — human vs AI agent identity, sourced from JWT claim "caller_type".</summary>
    public CallerType  CallerType { get; private set; }

    // ── Tool ──────────────────────────────────────────────────────────────────
    public string ToolNamespace { get; private set; } = default!;
    public string ToolName      { get; private set; } = default!;
    public string ToolVersion   { get; private set; } = default!;
    public string ToolFullName  => string.IsNullOrEmpty(ToolNamespace)
                                       ? ToolName
                                       : $"{ToolNamespace}.{ToolName}";

    // ── Event ─────────────────────────────────────────────────────────────────
    public InvocationEventType EventType   { get; private set; }
    public DateTimeOffset      OccurredAt  { get; private set; }
    public double?             DurationMs  { get; private set; }

    // ── Outcome ───────────────────────────────────────────────────────────────
    public string? ErrorCode    { get; private set; }
    public string? ErrorMessage { get; private set; }

    // ── Governance ────────────────────────────────────────────────────────────
    /// <summary>H5 — ISO 42001 AI governance metadata JSON, verbatim from X-Governance-Metadata header.</summary>
    public string? GovernanceMetadataJson { get; private set; }

    // ── Factory ───────────────────────────────────────────────────────────────

    public static ToolInvocationEvent Create(
        Guid                invocationRecordId,
        Guid                correlationId,
        string              tenantId,
        string              userId,
        CallerType          callerType,
        string              toolNamespace,
        string              toolName,
        string              toolVersion,
        InvocationEventType eventType,
        double?             durationMs             = null,
        string?             errorCode              = null,
        string?             errorMessage           = null,
        string?             governanceMetadataJson = null) =>
        new(
            id:                    Guid.NewGuid(),
            correlationId:         correlationId,
            invocationRecordId:    invocationRecordId,
            tenantId:              tenantId,
            userId:                userId,
            callerType:            callerType,
            toolNamespace:         toolNamespace,
            toolName:              toolName,
            toolVersion:           toolVersion,
            eventType:             eventType,
            occurredAt:            DateTimeOffset.UtcNow,
            durationMs:            durationMs,
            errorCode:             errorCode,
            errorMessage:          errorMessage,
            governanceMetadataJson: governanceMetadataJson);
}

namespace ToolEngine.Core.Domain.Entities;

using ToolEngine.Core.Abstractions.Common;
using ToolEngine.Core.Domain.Common;
using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Core.Domain.Enums;

// H2: GDPR retention  H4: Agent identity  H5: ISO 42001 governance metadata

/// <summary>
/// Audit aggregate. One record per tool invocation.
/// Written by the AuditBehavior in the MediatR pipeline.
/// </summary>
public sealed class ToolInvocationRecord : AggregateRoot<Guid>
{
    private ToolInvocationRecord(
        Guid           id,
        Guid           correlationId,
        string         tenantId,
        string         userId,
        string         toolNamespace,
        string         toolName,
        string         toolVersion,
        ToolType       toolType,
        CallerType     callerType,
        DateTimeOffset invokedAt,
        string?        governanceMetadataJson,
        DateTimeOffset retainUntil)
        : base(id)
    {
        CorrelationId          = correlationId;
        TenantId               = tenantId;
        UserId                 = userId;
        ToolNamespace          = toolNamespace;
        ToolName               = toolName;
        ToolVersion            = toolVersion;
        ToolType               = toolType;
        CallerType             = callerType;
        Status                 = ToolStatus.Pending;
        InvokedAt              = invokedAt;
        GovernanceMetadataJson = governanceMetadataJson;
        RetainUntil            = retainUntil;
    }

    public Guid            CorrelationId { get; private set; }
    public string          TenantId      { get; private set; }
    public string          UserId        { get; private set; }
    public string          ToolNamespace { get; private set; }
    public string          ToolName      { get; private set; }
    // "namespace.name" — canonical identity for log queries and dashboards.
    public string          ToolFullName  => string.IsNullOrEmpty(ToolNamespace)
                                               ? ToolName
                                               : $"{ToolNamespace}.{ToolName}";
    public string          ToolVersion   { get; private set; }
    public ToolType        ToolType      { get; private set; }
    /// <summary>H4 — Human vs AiAgent identity sourced from JWT claim "caller_type".</summary>
    public CallerType      CallerType    { get; private set; }
    public ToolStatus      Status        { get; private set; }
    public DateTimeOffset  InvokedAt     { get; private set; }
    public DateTimeOffset? CompletedAt   { get; private set; }
    public TimeSpan?       Duration      { get; private set; }
    public int?            TokensIn      { get; private set; }
    public int?            TokensOut     { get; private set; }
    public int             RetryCount    { get; private set; }
    public string?         ErrorCode     { get; private set; }
    public string?         ErrorMessage  { get; private set; }
    // ── H2 GDPR retention ────────────────────────────────────────────────────
    /// <summary>H2 — Earliest UTC date this record may be deleted or anonymised.</summary>
    public DateTimeOffset  RetainUntil   { get; private set; }
    /// <summary>H2 — Set to true after Anonymize() has been called; PII fields are then nulled.</summary>
    public bool            IsAnonymized  { get; private set; }
    // ── H5 ISO 42001 governance metadata ─────────────────────────────────────
    /// <summary>H5 — JSON blob from X-Governance-Metadata header. Nullable; not all callers supply it.</summary>
    public string?         GovernanceMetadataJson { get; private set; }

    public static ToolInvocationRecord Create(
        Guid              correlationId,
        string            tenantId,
        string            userId,
        string            toolNamespace,
        string            toolName,
        string            toolVersion,
        ToolType          toolType,
        IDateTimeProvider clock,
        CallerType        callerType             = CallerType.Human,
        string?           governanceMetadataJson = null,
        int               retentionDays          = 90) =>
        new(Guid.NewGuid(), correlationId, tenantId, userId,
            toolNamespace, toolName, toolVersion, toolType, callerType,
            clock.UtcNow, governanceMetadataJson,
            clock.UtcNow.AddDays(retentionDays));

    public void MarkRunning() => Status = ToolStatus.Running;

    public void MarkSucceeded(ToolUsageMetrics metrics, IDateTimeProvider clock)
    {
        Status      = ToolStatus.Succeeded;
        CompletedAt = clock.UtcNow;
        Duration    = metrics.Duration;
        TokensIn    = metrics.TokensIn;
        TokensOut   = metrics.TokensOut;
        RetryCount  = metrics.RetryCount;
    }

    public void MarkFailed(ToolError error, IDateTimeProvider clock)
    {
        Status       = ToolStatus.Failed;
        CompletedAt  = clock.UtcNow;
        ErrorCode    = error.Code;
        ErrorMessage = error.Description;
    }

    /// <summary>
    /// C3 — Marks the record as suspended pending human approval.
    /// The record remains open; CompletedAt is not set.
    /// Status will be updated to Succeeded or Failed when the approval is decided
    /// and the tool re-executes (or is denied).
    /// </summary>
    public void MarkSuspended()
    {
        Status = ToolStatus.Suspended;
        // CompletedAt intentionally NOT set — execution has not completed.
    }

    public void MarkCancelled(IDateTimeProvider clock)
    {
        Status      = ToolStatus.Cancelled;
        CompletedAt = clock.UtcNow;
    }

    /// <summary>
    /// H2 — GDPR erasure / anonymisation sweep.
    /// Nulls all fields that may contain personal data. Idempotent.
    /// The record structure is retained for SOC 2 completeness counts;
    /// only the PII is removed. Per GDPR Article 17 "right to erasure".
    ///
    /// Call only after RetainUntil has passed, verified by the retention sweep job.
    /// </summary>
    public void Anonymize()
    {
        if (IsAnonymized) return;

        UserId                 = "[anonymized]";
        ErrorMessage           = null;
        GovernanceMetadataJson = null;
        IsAnonymized           = true;
    }
}

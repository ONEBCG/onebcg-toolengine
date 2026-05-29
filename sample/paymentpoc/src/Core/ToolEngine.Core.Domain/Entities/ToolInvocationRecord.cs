using ToolEngine.Core.Abstractions.Common;
using ToolEngine.Core.Domain.Common;
using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Core.Domain.Enums;

namespace ToolEngine.Core.Domain.Entities;

public sealed class ToolInvocationRecord : AggregateRoot<Guid>
{
    public string?        UserId                 { get; private set; }
    public string         ToolFullName           { get; private set; } = default!;
    public string         ToolVersion            { get; private set; } = default!;
    public ToolStatus     Status                 { get; private set; }
    public DateTimeOffset InvokedAt              { get; private set; }
    public DateTimeOffset? CompletedAt           { get; private set; }
    public long?          DurationMs             { get; private set; }
    public int?           TokensUsed             { get; private set; }
    public string?        ErrorCode              { get; private set; }
    public string?        ErrorMessage           { get; private set; }

    // H4 — NIST AI Agent Identity
    public CallerType     CallerType             { get; private set; }

    // H5 — ISO 42001 AI governance traceability
    public string?        GovernanceMetadataJson { get; private set; }

    // H2 — GDPR retention
    public DateTimeOffset RetainUntil            { get; private set; }
    public bool           IsAnonymized           { get; private set; }

    private ToolInvocationRecord() { }

    public static ToolInvocationRecord Create(
        Guid correlationId, string? userId,
        string toolFullName, string toolVersion,
        CallerType callerType, string? governanceMetadataJson,
        IDateTimeProvider clock)
    {
        var now = clock.UtcNow;
        return new ToolInvocationRecord
        {
            Id                    = correlationId,
            UserId                = userId,
            ToolFullName          = toolFullName,
            ToolVersion           = toolVersion,
            Status                = ToolStatus.Running,
            InvokedAt             = now,
            CallerType            = callerType,
            GovernanceMetadataJson = governanceMetadataJson,
            RetainUntil           = now.AddDays(90),   // H2: 90-day default
            IsAnonymized          = false,
            CreatedAt             = now,
            UpdatedAt             = now,
        };
    }

    public void MarkSucceeded(ToolUsageMetrics metrics)
    {
        Status      = ToolStatus.Succeeded;
        CompletedAt = DateTimeOffset.UtcNow;
        DurationMs  = metrics.DurationMs;
        TokensUsed  = metrics.TokensUsed;
        UpdatedAt   = DateTimeOffset.UtcNow;
    }

    public void MarkFailed(ToolError error)
    {
        Status       = ToolStatus.Failed;
        CompletedAt  = DateTimeOffset.UtcNow;
        ErrorCode    = error.ErrorCode;
        ErrorMessage = error.Description;
        UpdatedAt    = DateTimeOffset.UtcNow;
    }

    public void MarkSuspended()
    {
        Status    = ToolStatus.Suspended;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    // H2 — GDPR right-to-erasure — nulls PII, retains structural fields for SOC 2
    public void Anonymize()
    {
        if (IsAnonymized) return;   // idempotent
        UserId                = "[anonymized]";
        ErrorMessage          = null;
        GovernanceMetadataJson = null;   // may contain session/user PII — H2+H5
        IsAnonymized          = true;
        UpdatedAt             = DateTimeOffset.UtcNow;
    }
}

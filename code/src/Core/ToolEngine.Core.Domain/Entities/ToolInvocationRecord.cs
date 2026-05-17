namespace ToolEngine.Core.Domain.Entities;

using ToolEngine.Core.Abstractions.Common;
using ToolEngine.Core.Domain.Common;
using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Core.Domain.Enums;

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
        DateTimeOffset invokedAt)
        : base(id)
    {
        CorrelationId = correlationId;
        TenantId      = tenantId;
        UserId        = userId;
        ToolNamespace = toolNamespace;
        ToolName      = toolName;
        ToolVersion   = toolVersion;
        ToolType      = toolType;
        Status        = ToolStatus.Pending;
        InvokedAt     = invokedAt;
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
    public ToolStatus      Status        { get; private set; }
    public DateTimeOffset  InvokedAt     { get; private set; }
    public DateTimeOffset? CompletedAt   { get; private set; }
    public TimeSpan?       Duration      { get; private set; }
    public int?            TokensIn      { get; private set; }
    public int?            TokensOut     { get; private set; }
    public int             RetryCount    { get; private set; }
    public string?         ErrorCode     { get; private set; }
    public string?         ErrorMessage  { get; private set; }

    public static ToolInvocationRecord Create(
        Guid              correlationId,
        string            tenantId,
        string            userId,
        string            toolNamespace,
        string            toolName,
        string            toolVersion,
        ToolType          toolType,
        IDateTimeProvider clock) =>
        new(Guid.NewGuid(), correlationId, tenantId, userId,
            toolNamespace, toolName, toolVersion, toolType, clock.UtcNow);

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

    public void MarkCancelled(IDateTimeProvider clock)
    {
        Status      = ToolStatus.Cancelled;
        CompletedAt = clock.UtcNow;
    }
}

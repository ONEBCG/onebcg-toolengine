namespace ToolEngine.Infrastructure.Persistence.Entities;

using ToolEngine.Core.Domain.Common;

/// <summary>
/// Outbox pattern — approval channel notification intent.
///
/// Written atomically with PendingApproval in the same SaveChangesAsync call.
/// Processed asynchronously by NotificationDispatchService, which calls the
/// IApprovalChannel.SendAsync and marks SentAt on success.
///
/// This ensures at-least-once delivery: if the API process dies between
/// SaveChangesAsync and channel.SendAsync, the unsent OutboxMessage is picked up
/// on the next dispatch cycle. Channels must be idempotent on duplicate delivery.
/// </summary>
public sealed class OutboxMessage : Entity<Guid>
{
    // EF Core
    private OutboxMessage() { }

    private OutboxMessage(
        Guid              pendingApprovalId,
        string            tenantId,
        string            toolFullName,
        string            channelType)
        : base(Guid.NewGuid())
    {
        PendingApprovalId = pendingApprovalId;
        TenantId          = tenantId;
        ToolFullName      = toolFullName;
        ChannelType       = channelType;
        CreatedAt         = DateTimeOffset.UtcNow;
    }

    public Guid              PendingApprovalId { get; private set; }
    public string            TenantId          { get; private set; } = default!;
    public string            ToolFullName      { get; private set; } = default!;
    /// <summary>Serialised ApprovalChannelType name — used to re-select the channel on dispatch.</summary>
    public string            ChannelType       { get; private set; } = default!;
    public int               RetryCount        { get; private set; }
    public DateTimeOffset    CreatedAt         { get; private set; }
    public DateTimeOffset?   SentAt            { get; private set; }
    public DateTimeOffset?   NextRetryAt       { get; private set; }
    public string?           LastError         { get; private set; }

    public static OutboxMessage Create(
        Guid   pendingApprovalId,
        string tenantId,
        string toolFullName,
        string channelType) =>
        new(pendingApprovalId, tenantId, toolFullName, channelType);

    /// <summary>Marks the message as successfully sent.</summary>
    public void MarkSent() => SentAt = DateTimeOffset.UtcNow;

    /// <summary>
    /// Increments the retry counter and schedules the next attempt
    /// using exponential back-off: 30s → 2m → 8m → 32m → 128m (capped at 2 hours).
    /// </summary>
    public void RecordFailure(string error)
    {
        RetryCount++;
        LastError    = error;
        var backoff  = TimeSpan.FromSeconds(Math.Min(30 * Math.Pow(4, RetryCount - 1), 7200));
        NextRetryAt  = DateTimeOffset.UtcNow + backoff;
    }

    /// <summary>
    /// H11 — Terminally abandons the message so it is never retried again.
    /// Called for unrecoverable failures (e.g. unknown channel type) where retrying
    /// will always produce the same error. Sets RetryCount to MaxRetries so the
    /// dispatch query filter (RetryCount &lt; MaxRetries) permanently excludes this row.
    /// </summary>
    public void Abandon(string reason, int maxRetries)
    {
        LastError   = $"[ABANDONED] {reason}";
        RetryCount  = maxRetries; // query filter: RetryCount < MaxRetries → row excluded
        NextRetryAt = null;
        // SentAt intentionally NOT set — the message was never delivered.
    }

    /// <summary>True when the message is eligible for dispatch.</summary>
    public bool IsReady =>
        SentAt is null &&
        (NextRetryAt is null || NextRetryAt <= DateTimeOffset.UtcNow);
}

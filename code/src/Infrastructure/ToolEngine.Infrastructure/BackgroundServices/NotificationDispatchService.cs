namespace ToolEngine.Infrastructure.BackgroundServices;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ToolEngine.Core.Abstractions.Persistence;
using ToolEngine.Core.Domain.Entities;
using ToolEngine.Infrastructure.Approval;
using ToolEngine.Infrastructure.Persistence;
using ToolEngine.Infrastructure.Persistence.Entities;

/// <summary>
/// Background service that processes unsent OutboxMessages and delivers them via the
/// configured IApprovalChannel. Implements the Outbox pattern for at-least-once delivery.
///
/// Dispatch loop runs every <see cref="PollingIntervalSeconds"/> seconds.
/// Failed deliveries are retried with exponential back-off (30s → 2m → 8m → 32m → 2h).
/// After <see cref="MaxRetries"/> consecutive failures the message is abandoned and logged.
///
/// Channels must be idempotent — a message may be delivered more than once if the process
/// crashes between SendAsync and SaveChangesAsync.
/// </summary>
internal sealed class NotificationDispatchService : BackgroundService
{
    private const int PollingIntervalSeconds = 15;
    private const int MaxRetries             = 5;

    private readonly IServiceScopeFactory           _scopeFactory;
    private readonly ILogger<NotificationDispatchService> _log;

    public NotificationDispatchService(
        IServiceScopeFactory scopeFactory,
        ILogger<NotificationDispatchService> log)
    {
        _scopeFactory = scopeFactory;
        _log          = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("NotificationDispatchService started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Unexpected error in NotificationDispatchService loop.");
            }

            await Task.Delay(TimeSpan.FromSeconds(PollingIntervalSeconds), stoppingToken);
        }

        _log.LogInformation("NotificationDispatchService stopped.");
    }

    private async Task DispatchBatchAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();

        var ctx      = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var selector = scope.ServiceProvider.GetRequiredService<ApprovalChannelSelector>();
        var approvalReadRepo = scope.ServiceProvider
                                   .GetRequiredService<IReadRepository<PendingApproval, Guid>>();

        var now   = DateTimeOffset.UtcNow;
        var batch = await ctx.OutboxMessages
            .Where(m => m.SentAt == null &&
                        (m.NextRetryAt == null || m.NextRetryAt <= now) &&
                        m.RetryCount < MaxRetries)
            .OrderBy(m => m.CreatedAt)
            .Take(50)
            .ToListAsync(ct);

        if (batch.Count == 0)
            return;

        _log.LogDebug("NotificationDispatchService: processing {Count} outbox message(s).", batch.Count);

        foreach (var message in batch)
        {
            await DispatchSingleAsync(message, ctx, selector, approvalReadRepo, ct);
            // M5 — Save after each message rather than batching all 50 into one commit.
            // A single DbUpdateConcurrencyException in the batch would roll back mutations
            // for messages already successfully delivered, causing duplicate re-deliveries
            // on the next poll cycle. Per-message saves bound the blast radius to one row.
            await ctx.SaveChangesAsync(ct);
        }
    }

    private async Task DispatchSingleAsync(
        OutboxMessage                          message,
        AppDbContext                           ctx,
        ApprovalChannelSelector                selector,
        IReadRepository<PendingApproval, Guid> approvalRepo,
        CancellationToken                      ct)
    {
        var pending = await approvalRepo.GetByIdAsync(message.PendingApprovalId, ct);
        if (pending is null)
        {
            // Orphaned outbox message — approval was deleted. Mark as sent to stop retrying.
            _log.LogWarning(
                "OutboxMessage {Id}: PendingApproval {ApprovalId} not found — marking sent.",
                message.Id, message.PendingApprovalId);
            message.MarkSent();
            return;
        }

        if (!Enum.TryParse<ToolEngine.Core.Domain.Enums.ApprovalChannelType>(
            message.ChannelType, out var channelType))
        {
            _log.LogError(
                "OutboxMessage {Id}: unknown channel type '{Type}' — abandoning permanently.",
                message.Id, message.ChannelType);
            // H11 — Abandon terminally: an unknown channel type will never succeed.
            // RecordFailure would schedule exponential retries for days; Abandon
            // sets RetryCount = MaxRetries so the dispatch query excludes this row forever.
            message.Abandon($"Unknown channel type: {message.ChannelType}", MaxRetries);
            return;
        }

        var channel = selector.SelectByType(channelType);

        try
        {
            await channel.SendAsync(pending, ct);
            message.MarkSent();
            _log.LogInformation(
                "OutboxMessage {Id}: dispatched via {Channel} for approval {ApprovalId}.",
                message.Id, channelType, message.PendingApprovalId);
        }
        catch (Exception ex)
        {
            message.RecordFailure(ex.Message);
            _log.LogWarning(ex,
                "OutboxMessage {Id}: delivery failed (attempt {Retry}/{Max}).",
                message.Id, message.RetryCount, MaxRetries);
        }
    }
}

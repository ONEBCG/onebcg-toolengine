using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ToolEngine.Core.Abstractions.Common;
using ToolEngine.Core.Domain.Entities;
using ToolEngine.Core.Domain.Enums;
using ToolEngine.Tools.Abstractions.Interfaces;
using ToolEngine.Tools.Abstractions.Models;

namespace ToolEngine.Infrastructure.Approval;

/// <summary>
/// AsyncApprovalGate — used by the API host.
/// F8: IdempotencyKey checked before creating a new PendingApproval record.
///     If an existing Pending approval with the same key exists, returns Suspended
///     with the existing InvocationId without creating a duplicate.
/// F7: Notification dispatched via OutboxMessage in the same SaveChangesAsync call
///     to guarantee at-least-once delivery (no separate network call here).
/// </summary>
public sealed class AsyncApprovalGate : IHumanApprovalGate
{
    private readonly Persistence.AppDbContext _db;
    private readonly IDateTimeProvider        _clock;
    private readonly ILogger<AsyncApprovalGate> _log;

    public AsyncApprovalGate(
        Persistence.AppDbContext db,
        IDateTimeProvider clock,
        ILogger<AsyncApprovalGate> log)
    {
        _db    = db;
        _clock = clock;
        _log   = log;
    }

    public async Task<ApprovalDecision> RequestApprovalAsync(
        ApprovalContext context, CancellationToken ct = default)
    {
        // F8: idempotency — reuse existing pending approval for same idempotency key
        if (!string.IsNullOrWhiteSpace(context.IdempotencyKey))
        {
            var existing = await _db.Set<PendingApproval>()
                .FirstOrDefaultAsync(a =>
                    a.IdempotencyKey == context.IdempotencyKey
                 && a.Status        == ApprovalStatus.Pending, ct);

            if (existing is not null)
            {
                _log.LogInformation(
                    "F8: Reusing existing PendingApproval {Id} for idempotency key '{Key}'.",
                    existing.Id, context.IdempotencyKey);
                return ApprovalDecision.Suspended(existing.Id);
            }
        }

        // Create new PendingApproval (E1: CSPRNG token inside Create())
        var approval = PendingApproval.Create(
            context.ToolFullName,
            context.Risk, context.Channel,
            context.IdempotencyKey, _clock);

        // H3: Acknowledgement for High/Critical risk
        if (context.AcknowledgementStatement is not null)
            approval.SetAcknowledgement(
                JsonSerializer.Serialize(context.AcknowledgementStatement));

        _db.Set<PendingApproval>().Add(approval);

        // F7: Outbox notification (same transaction as approval record — atomic)
        var payload = JsonSerializer.Serialize(new
        {
            ApprovalId   = approval.Id,
            ToolFullName = context.ToolFullName,
            Risk         = context.Risk.ToString(),
            Channel      = context.Channel.ToString(),
            ExpiresAt    = approval.ExpiresAt,
            Token        = approval.ApprovalToken,
            UserId       = context.UserId,
            CallerType   = context.CallerType.ToString(),
        });

        _db.Set<OutboxMessage>().Add(
            OutboxMessage.Create("ApprovalRequested", payload, _clock.UtcNow));

        await _db.SaveChangesAsync(ct);

        _log.LogInformation(
            "PendingApproval {Id} created for tool '{Tool}' (Risk: {Risk}, Channel: {Channel}).",
            approval.Id, context.ToolFullName, context.Risk, context.Channel);

        return ApprovalDecision.Suspended(approval.Id);
    }
}

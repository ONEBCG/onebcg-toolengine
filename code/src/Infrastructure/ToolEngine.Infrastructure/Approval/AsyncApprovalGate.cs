namespace ToolEngine.Infrastructure.Approval;

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ToolEngine.Core.Abstractions.Persistence;
using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Core.Domain.Entities;
using ToolEngine.Core.Domain.Enums;
using ToolEngine.Infrastructure.Persistence;
using ToolEngine.Infrastructure.Persistence.Entities;
using ToolEngine.Tools.Abstractions.Interfaces;

/// <summary>
/// API-side IHumanApprovalGate implementation.
///
/// For Low risk:    auto-approves immediately (audit log only).
/// For Medium/High: creates PendingApproval + OutboxMessage in a single SaveChangesAsync,
///                  returns ApprovalDecision.Suspend(). NotificationDispatchService delivers
///                  the notification asynchronously with retry (outbox pattern).
/// For Critical:    same as Medium/High but channel is forced to EmailOtp.
///
/// Idempotency: if an IdempotencyKey is supplied, an existing PendingApproval matching
/// the key + tenantId + toolFullName is returned without creating a duplicate. This makes
/// retried requests safe under at-least-once delivery from the agent orchestrator.
/// </summary>
public sealed class AsyncApprovalGate : IHumanApprovalGate
{
    private readonly IRepository<PendingApproval, Guid>     _repo;
    private readonly IReadRepository<PendingApproval, Guid> _readRepo;
    private readonly IUnitOfWork                            _uow;
    private readonly AppDbContext                           _ctx;
    private readonly ApprovalChannelSelector                _selector;
    private readonly ApprovalOptions                        _options;
    private readonly ILogger<AsyncApprovalGate>             _log;

    private static readonly string EmptySerializedInput =
        JsonSerializer.Serialize(new object());

    public AsyncApprovalGate(
        IRepository<PendingApproval, Guid>     repo,
        IReadRepository<PendingApproval, Guid> readRepo,
        IUnitOfWork                            uow,
        AppDbContext                           ctx,
        ApprovalChannelSelector                selector,
        IOptions<ApprovalOptions>              options,
        ILogger<AsyncApprovalGate>             log)
    {
        _repo     = repo;
        _readRepo = readRepo;
        _uow      = uow;
        _ctx      = ctx;
        _selector = selector;
        _options  = options.Value;
        _log      = log;
    }

    public async Task<ApprovalDecision> RequestApprovalAsync(
        ApprovalContext   context,
        string            reason,
        ApprovalRisk      risk,
        object?           inputSummary,
        CancellationToken ct = default)
    {
        // Low risk: auto-approve with audit log — no friction.
        if (risk == ApprovalRisk.Low)
        {
            _log.LogInformation(
                "AsyncApprovalGate: auto-approving {Tool} (Low risk, correlationId={Id})",
                context.ToolFullName, context.CorrelationId);
            return ApprovalDecision.Allow("system-auto");
        }

        // Return an existing pending approval if the idempotency key matches,
        // preventing duplicate notifications when the caller retries the same request.
        if (context.IdempotencyKey is not null)
        {
            var existing = await FindByIdempotencyKeyAsync(
                context.IdempotencyKey, context.TenantId, context.ToolFullName, ct);
            if (existing is not null)
            {
                _log.LogInformation(
                    "AsyncApprovalGate: returning existing approval {Id} for idempotency key {Key}",
                    existing.Id, context.IdempotencyKey);
                return ApprovalDecision.Suspend(existing.Id);
            }
        }

        var serializedInput = inputSummary is null
            ? EmptySerializedInput
            : JsonSerializer.Serialize(inputSummary);

        var channel = _selector.Select(context.TenantId, risk);

        var pending = PendingApproval.Create(
            correlationId:    context.CorrelationId,
            tenantId:         context.TenantId,
            userId:           context.UserId,
            toolNamespace:    context.ToolNamespace,
            toolName:         context.ToolName,
            toolVersion:      context.ToolVersion,
            serializedInput:  serializedInput,
            channel:          channel.ChannelType,
            risk:             risk,
            approvalReason:   reason,
            approverEmail:    context.ApproverEmail,
            timeoutMinutes:   _options.ApprovalTimeoutMinutes,
            idempotencyKey:   context.IdempotencyKey);

        // EU AI Act Article 14 §4 — High and Critical tools require an explicit
        // acknowledgement statement recorded against the approval record.
        // This provides the regulatory audit trail for human oversight.
        if (risk >= ApprovalRisk.High)
        {
            var ack = new AcknowledgementStatement(
                RegulatoryBasis:   "EU AI Act Article 14 §4 — Human Oversight",
                RiskLevel:         risk,
                ToolFullName:      context.ToolFullName,
                OperatorStatement: $"The approving operator acknowledges that '{context.ToolFullName}' " +
                                   $"is classified {risk} risk and that explicit human oversight is required " +
                                   $"before execution. This acknowledgement is recorded for regulatory traceability.",
                IssuedAt:          DateTimeOffset.UtcNow);
            pending.SetAcknowledgement(JsonSerializer.Serialize(ack));
        }

        // Write PendingApproval + OutboxMessage atomically in a single SaveChangesAsync.
        // NotificationDispatchService reads the outbox and calls channel.SendAsync asynchronously.
        // If the process crashes after commit, the outbox ensures delivery on next startup.
        var outbox = OutboxMessage.Create(
            pendingApprovalId: pending.Id,
            tenantId:          context.TenantId,
            toolFullName:      context.ToolFullName,
            channelType:       channel.ChannelType.ToString());

        await _repo.AddAsync(pending, ct);
        await _ctx.OutboxMessages.AddAsync(outbox, ct);
        await _uow.SaveChangesAsync(ct);

        _log.LogInformation(
            "AsyncApprovalGate: suspended {Tool} (risk={Risk}) — invocationId={Id}, channel={Channel}",
            context.ToolFullName, risk, pending.Id, channel.ChannelType);

        return ApprovalDecision.Suspend(pending.Id);
    }

    private async Task<PendingApproval?> FindByIdempotencyKeyAsync(
        string key, string tenantId, string toolFullName, CancellationToken ct)
    {
        var spec = new LambdaSpecification<PendingApproval>(
            a => a.IdempotencyKey == key &&
                 a.TenantId       == tenantId &&
                 a.ToolFullName   == toolFullName &&
                 a.Status         == ApprovalStatus.Pending);
        var results = await _readRepo.ListAsync(spec, ct);
        return results.FirstOrDefault(a => !a.IsExpired);
    }
}

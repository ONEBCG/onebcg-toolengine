namespace ToolEngine.Infrastructure.Approval;

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ToolEngine.Core.Abstractions.Persistence;
using ToolEngine.Core.Domain.Entities;
using ToolEngine.Core.Domain.Enums;
using ToolEngine.Tools.Abstractions.Interfaces;

/// <summary>
/// API-side IHumanApprovalGate implementation.
///
/// For Low risk:    auto-approves immediately (audit log only).
/// For Medium/High: creates PendingApproval in DB, sends notification via the
///                  configured channel, returns ApprovalDecision.Suspend().
///                  Execution resumes when POST /approvals/{token}/decide arrives.
/// For Critical:    same as Medium/High but channel is forced to EmailOtp.
///
/// The ApprovalBehavior (MediatR pipeline) maps Suspend() → HTTP 202 + poll URL.
/// </summary>
public sealed class AsyncApprovalGate : IHumanApprovalGate
{
    private readonly IRepository<PendingApproval, Guid> _repo;
    private readonly IUnitOfWork                         _uow;
    private readonly ApprovalChannelSelector             _selector;
    private readonly ApprovalOptions                     _options;
    private readonly ILogger<AsyncApprovalGate>          _log;

    public AsyncApprovalGate(
        IRepository<PendingApproval, Guid> repo,
        IUnitOfWork                         uow,
        ApprovalChannelSelector             selector,
        IOptions<ApprovalOptions>           options,
        ILogger<AsyncApprovalGate>          log)
    {
        _repo     = repo;
        _uow      = uow;
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
                "AsyncApprovalGate: auto-approving {ToolFullName} (Low risk, correlationId={Id})",
                context.ToolFullName, context.CorrelationId);
            return ApprovalDecision.Allow("system-auto");
        }

        var serializedInput = inputSummary is null
            ? "{}"
            : JsonSerializer.Serialize(inputSummary);

        var channel = _selector.Select(context.TenantId, risk);

        var pending = PendingApproval.Create(
            correlationId:   context.CorrelationId,
            tenantId:        context.TenantId,
            userId:          context.UserId,
            toolNamespace:   context.ToolNamespace,
            toolName:        context.ToolName,
            toolVersion:     context.ToolVersion,
            serializedInput: serializedInput,
            channel:         channel.ChannelType,
            risk:            risk,
            approvalReason:  reason,
            approverEmail:   context.ApproverEmail,
            timeoutMinutes:  _options.ApprovalTimeoutMinutes);

        await _repo.AddAsync(pending, ct);
        await _uow.SaveChangesAsync(ct);

        await channel.SendAsync(pending, ct);
        // EmailOtpChannel.SendAsync sets OtpHash on the entity — persist any channel mutations.
        await _uow.SaveChangesAsync(ct);

        _log.LogInformation(
            "AsyncApprovalGate: suspended {ToolFullName} (risk={Risk}) — " +
            "invocationId={Id}, channel={Channel}",
            context.ToolFullName, risk, pending.Id, channel.ChannelType);

        return ApprovalDecision.Suspend(pending.Id);
    }
}

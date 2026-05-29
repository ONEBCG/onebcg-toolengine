using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using ToolEngine.Application.Abstractions;
using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Core.Domain.Enums;
using ToolEngine.Tools.Abstractions.Attributes;
using ToolEngine.Tools.Abstractions.Interfaces;
using ToolEngine.Tools.Abstractions.Models;

namespace ToolEngine.Application.Behaviors;

/// <summary>
/// Behavior 3 of 4 — Human Approval Gate.
/// Checks for [RequiresApproval] on the tool handler type resolved from IToolRegistry.
/// If present: delegates to IHumanApprovalGate.RequestApprovalAsync.
///
/// Outcomes:
///   Allowed   → passes to next behavior (synchronous / auto-approve paths)
///   Suspended → returns ToolResponse.Suspended(invocationId) — API returns HTTP 202
///   Denied    → returns ToolResponse.Fail(APPROVAL_DENIED)
///
/// F8: IdempotencyKey forwarded to ApprovalContext → AsyncApprovalGate deduplicates.
/// H3: AcknowledgementStatement required for High/Critical risk tools.
/// H4: CallerType persisted on PendingApproval record.
/// </summary>
public sealed class ApprovalBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest  : notnull
    where TResponse : IToolResponse
{
    private readonly IToolRegistry      _registry;
    private readonly IHumanApprovalGate _approvalGate;
    private readonly ILogger<ApprovalBehavior<TRequest, TResponse>> _log;

    public ApprovalBehavior(
        IToolRegistry registry,
        IHumanApprovalGate approvalGate,
        ILogger<ApprovalBehavior<TRequest, TResponse>> log)
    {
        _registry     = registry;
        _approvalGate = approvalGate;
        _log          = log;
    }

    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (request is not IExecuteToolCommand cmd)
            return await next();

        var resolveResult = _registry.Resolve(cmd.FullName, cmd.ToolVersion);
        if (resolveResult.IsFailure)
            return await next(); // Let handler return the error

        var descriptor = resolveResult.Value;
        var attr       = descriptor.HandlerType
            .GetCustomAttributes(typeof(RequiresApprovalAttribute), false)
            .FirstOrDefault() as RequiresApprovalAttribute;

        if (attr is null)
            return await next(); // No approval required

        _log.LogInformation(
            "Approval required for tool '{FullName}' (Risk: {Risk}, Channel: {Channel}).",
            cmd.FullName, attr.Risk, attr.Channel);

        // H3: AcknowledgementStatement for High/Critical risk tools.
        // ApprovalRisk enum: Low=0, Medium=1, High=2, Critical=3.
        // ">= High" correctly captures High and Critical only — Medium is intentionally excluded.
        AcknowledgementStatement? ack = null;
        if (attr.Risk >= ApprovalRisk.High)
        {
            ack = new AcknowledgementStatement(
                RegBasis:          "EU AI Act Article 14 §4",
                RiskLevel:         attr.Risk.ToString(),
                ToolFullName:      cmd.FullName,
                OperatorStatement: $"Operator confirms human oversight for {attr.Risk} risk tool '{cmd.FullName}'.",
                IssuedAt:          DateTimeOffset.UtcNow);
        }

        var context = new ApprovalContext(
            ToolFullName:              cmd.FullName,
            Risk:                      attr.Risk,
            Channel:                   attr.Channel,
            UserId:                    cmd.UserId,
            Reason:                    attr.Reason,
            IdempotencyKey:            cmd.IdempotencyKey, // F8
            CallerType:                cmd.CallerType,     // H4
            AcknowledgementStatement:  ack);               // H3

        var decision = await _approvalGate.RequestApprovalAsync(context, ct);

        if (decision.IsAllowed)
            return await next();

        if (decision.IsDenied)
            return Fail(cmd, new ToolError(403, "APPROVAL_DENIED",
                decision.DenialReason ?? "Approval was denied."));

        // Suspended — HTTP 202
        return Suspend(cmd, decision.InvocationId ?? Guid.NewGuid());
    }

    // Avoid reflection on TResponse (which is IToolResponse — an interface with no static members).
    // Construct ToolResponse<JsonElement> directly and box to TResponse via object.
    // Safe for any TResponse : IToolResponse; correct at runtime because the pipeline
    // always runs with TResponse = IToolResponse (ExecuteToolCommand : IRequest<IToolResponse>).
    private static TResponse Fail(IExecuteToolCommand cmd, ToolError error) =>
        (TResponse)(object)ToolResponse<JsonElement>.Fail(cmd.CorrelationId, error);

    private static TResponse Suspend(IExecuteToolCommand cmd, Guid invocationId) =>
        (TResponse)(object)ToolResponse<JsonElement>.Suspended(cmd.CorrelationId, invocationId);
}

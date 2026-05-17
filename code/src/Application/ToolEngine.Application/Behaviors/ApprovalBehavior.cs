namespace ToolEngine.Application.Behaviors;

using MediatR;
using ToolEngine.Application.Abstractions;
using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Tools.Abstractions.Interfaces;

/// <summary>
/// MediatR pipeline behavior that enforces the human-in-the-loop approval gate.
///
/// Flow:
/// 1. Resolve tool descriptor via IToolDiscovery — surfaced approval metadata.
/// 2. If NeedsApproval is false, pass through immediately.
/// 3. Build ApprovalContext from the command — provides tenantId, userId, toolVersion
///    so the gate can create a correctly-attributed PendingApproval entity.
/// 4a. Suspend path (API/AsyncApprovalGate): gate returns Pending=true.
///     Return ToolResponse.Suspended(correlationId, pendingInvocationId) →
///     mapped to HTTP 202 by the endpoint handler.
/// 4b. Deny path: gate returns Approved=false.
///     Return ToolResponse.Fail with UNAUTHORIZED.
/// 4c. Allow path: proceed to next behavior in the pipeline.
/// </summary>
public sealed class ApprovalBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IToolDiscovery     _discovery;
    private readonly IHumanApprovalGate _gate;

    public ApprovalBehavior(IToolDiscovery discovery, IHumanApprovalGate gate)
    {
        _discovery = discovery;
        _gate      = gate;
    }

    public async Task<TResponse> Handle(
        TRequest                          request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken                 ct)
    {
        if (request is not IExecuteToolCommand cmd)
            return await next();

        var descriptor = _discovery.Resolve(
            cmd.ToolNamespace, cmd.ToolName, cmd.ToolVersion, cmd.TenantId);

        // Unknown tool or approval not required — pass through.
        if (descriptor.IsFailure || !descriptor.Value.NeedsApproval)
            return await next();

        var context = new ApprovalContext(
            CorrelationId: cmd.CorrelationId,
            TenantId:      cmd.TenantId,
            UserId:        cmd.UserId,
            ToolNamespace: cmd.ToolNamespace,
            ToolName:      cmd.ToolName,
            ToolVersion:   cmd.ToolVersion);

        var decision = await _gate.RequestApprovalAsync(
            context,
            descriptor.Value.ApprovalReason
                ?? $"Tool '{descriptor.Value.FullName}' requires approval before execution.",
            descriptor.Value.ApprovalRisk,
            inputSummary: null,
            ct);

        // Async path: suspended, awaiting out-of-band decision (email, webhook, dashboard).
        if (decision.Pending && decision.PendingInvocationId.HasValue)
            return Suspended(cmd, decision.PendingInvocationId.Value);

        // Synchronous deny (CLI prompt declined, or gate configuration denied).
        if (!decision.Approved)
            return Fail(cmd, new ToolError(
                "APPROVAL_DENIED",
                decision.Reason ?? $"Human approval was denied for tool '{context.ToolFullName}'.",
                403));

        return await next();
    }

    private static TResponse Suspended(IExecuteToolCommand cmd, Guid pendingInvocationId)
    {
        if (typeof(TResponse).IsGenericType &&
            typeof(TResponse).GetGenericTypeDefinition() == typeof(ToolResponse<>))
        {
            var outputType = typeof(TResponse).GetGenericArguments()[0];
            var method = typeof(ToolResponse<>)
                .MakeGenericType(outputType)
                .GetMethod(nameof(ToolResponse<object>.Suspended),
                    [typeof(Guid), typeof(Guid)])!;
            return (TResponse)method.Invoke(null, [cmd.CorrelationId, pendingInvocationId])!;
        }

        throw new InvalidOperationException(
            $"Execution suspended pending approval {pendingInvocationId}.");
    }

    private static TResponse Fail(IExecuteToolCommand cmd, ToolError error)
    {
        if (typeof(TResponse).IsGenericType &&
            typeof(TResponse).GetGenericTypeDefinition() == typeof(ToolResponse<>))
        {
            var outputType = typeof(TResponse).GetGenericArguments()[0];
            var method = typeof(ToolResponse<>)
                .MakeGenericType(outputType)
                .GetMethod(nameof(ToolResponse<object>.Fail),
                    [typeof(Guid), typeof(ToolError)])!;
            return (TResponse)method.Invoke(null, [cmd.CorrelationId, error])!;
        }

        throw new UnauthorizedAccessException(error.Description);
    }
}

namespace ToolEngine.Application.Behaviors;

using MediatR;
using ToolEngine.Application.Abstractions;
using ToolEngine.Core.Abstractions.Persistence;
using ToolEngine.Core.Domain.Constants;
using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Core.Domain.Entities;

/// <summary>
/// Enforces the per-tenant MaxResponseTokens cap before the tool executes.
/// Rejects the request immediately when the command's requested token count would exceed
/// the tenant's configured cap, preventing cost overruns before approval is triggered.
///
/// TenantAuthorizationBehavior runs before this and guarantees the tenant exists.
/// MaxResponseTokens == 0 means no cap is configured for this tenant.
/// </summary>
public sealed class TokenBudgetBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IReadRepository<Tenant, string> _tenants;

    public TokenBudgetBehavior(IReadRepository<Tenant, string> tenants)
        => _tenants = tenants;

    public async Task<TResponse> Handle(
        TRequest                          request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken                 ct)
    {
        if (request is not IExecuteToolCommand cmd)
            return await next();

        var tenant = await _tenants.GetByIdAsync(cmd.TenantId, ct);
        // TenantAuthorizationBehavior (which runs before this) will reject unknown tenants.
        // If tenant is null here a non-tool command slipped through — pass it on safely.
        if (tenant is null)
            return await next();

        // MaxResponseTokens == 0 means no cap configured for this tenant.
        if (tenant.MaxResponseTokens > 0 &&
            cmd.MaxResponseTokens > tenant.MaxResponseTokens)
        {
            return Fail(cmd, new ToolError(
                ErrorCodes.TokenBudgetExceeded,
                $"Requested {cmd.MaxResponseTokens} tokens exceeds tenant cap " +
                $"of {tenant.MaxResponseTokens}.",
                400));
        }

        return await next();
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
                    [typeof(Guid), typeof(ToolError), typeof(ToolUsageMetrics)])!;
            return (TResponse)method.Invoke(null, [cmd.CorrelationId, error, null])!;
        }

        throw new InvalidOperationException(error.Description);
    }
}

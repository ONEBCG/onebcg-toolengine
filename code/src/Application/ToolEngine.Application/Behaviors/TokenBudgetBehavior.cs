namespace ToolEngine.Application.Behaviors;

using MediatR;
using ToolEngine.Application.Abstractions;
using ToolEngine.Core.Abstractions.Persistence;
using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Core.Domain.Entities;

/// <summary>
/// Enforces the per-tenant MaxResponseTokens cap before the tool executes.
/// If the command's MaxResponseTokens exceeds the tenant's configured cap the
/// request is rejected immediately with a clear budget error.
///
/// Runs after TenantAuthorizationBehavior (tenant is guaranteed to exist here)
/// and before ApprovalBehavior so we don't trigger approval for a request that
/// would be rejected on budget anyway.
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
        if (tenant is null)
            return await next(); // TenantAuthorizationBehavior will reject; pass through

        // MaxResponseTokens == 0 means no cap configured.
        if (tenant.MaxResponseTokens > 0 &&
            cmd.MaxResponseTokens > tenant.MaxResponseTokens)
        {
            return Fail(cmd, new ToolError(
                "TOKEN_BUDGET_EXCEEDED",
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

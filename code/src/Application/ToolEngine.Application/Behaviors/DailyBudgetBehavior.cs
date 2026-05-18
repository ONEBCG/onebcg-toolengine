namespace ToolEngine.Application.Behaviors;

using System.Diagnostics;
using MediatR;
using ToolEngine.Application.Abstractions;
using ToolEngine.Application.Telemetry;
using ToolEngine.Core.Abstractions.Persistence;
using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Core.Domain.Entities;

/// <summary>
/// Enforces the per-tenant daily tool-call budget defined by Tenant.DailyToolCallBudget.
/// Counts all ToolInvocationRecords created today (UTC) for the tenant and rejects
/// if the configured cap is reached.
///
/// DailyToolCallBudget == 0 means no cap — the tenant is unrestricted.
///
/// Runs after TenantAuthorizationBehavior and TokenBudgetBehavior (per-request cap)
/// but before LoopDetectionBehavior and ApprovalBehavior.
///
/// Performance note: this issues a COUNT(*) query per request. Phase F replaces this
/// with a Redis INCR counter keyed on tenantId + date for O(1) distributed enforcement.
/// </summary>
public sealed class DailyBudgetBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IReadRepository<Tenant, string>               _tenants;
    private readonly IReadRepository<ToolInvocationRecord, Guid>   _invocations;

    public DailyBudgetBehavior(
        IReadRepository<Tenant, string>             tenants,
        IReadRepository<ToolInvocationRecord, Guid> invocations)
    {
        _tenants     = tenants;
        _invocations = invocations;
    }

    public async Task<TResponse> Handle(
        TRequest                          request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken                 ct)
    {
        if (request is not IExecuteToolCommand cmd)
            return await next();

        var tenant = await _tenants.GetByIdAsync(cmd.TenantId, ct);
        if (tenant is null || tenant.DailyToolCallBudget <= 0)
            return await next(); // no cap configured — TenantAuth will reject unknown tenants

        // M2 — Use DateTime.UtcNow.Date (Kind=Utc) rather than DateTimeOffset.UtcNow.Date
        // (Kind=Unspecified) to make the UTC-midnight intent explicit and safe if the
        // clock source is ever changed from UtcNow.
        var startOfDayUtc = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);

        var spec = new LambdaSpecification<ToolInvocationRecord>(
            r => r.TenantId == cmd.TenantId && r.InvokedAt >= startOfDayUtc);

        var todayCount = await _invocations.CountAsync(spec, ct);

        if (todayCount >= tenant.DailyToolCallBudget)
        {
            // G2 — daily budget metric
            ToolEngineTelemetry.DailyBudgetExceeded.Add(1,
                new TagList { { "tenant.id", cmd.TenantId } });
            return Fail(cmd, new ToolError(
                "DAILY_BUDGET_EXCEEDED",
                $"Tenant '{cmd.TenantId}' has reached its daily tool-call budget " +
                $"of {tenant.DailyToolCallBudget}. Budget resets at midnight UTC.",
                429));
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

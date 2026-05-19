namespace ToolEngine.Application.Behaviors;

using MediatR;
using ToolEngine.Application.Abstractions;
using ToolEngine.Core.Abstractions.Persistence;
using ToolEngine.Core.Domain.Constants;
using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Core.Domain.Entities;

/// <summary>
/// Validates that the requesting tenant is active and is allowed to call the requested namespace.
/// Runs outermost (before ValidationBehavior) — auth precedes all business logic (OWASP A01:2025).
///
/// Namespace allowlist semantics (deny-by-default):
///   Empty list           → tenant cannot call any namespace.
///   Contains "*"         → tenant is unrestricted (all namespaces permitted).
///   Contains "math"      → only the "math" namespace is permitted.
///
/// Dev / seed tenants should have AllowedNamespaces = ["*"].
/// Newly created tenants start with an empty list and must be explicitly granted access.
/// </summary>
public sealed class TenantAuthorizationBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IReadRepository<Tenant, string> _tenants;

    public TenantAuthorizationBehavior(IReadRepository<Tenant, string> tenants)
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
            return Fail(cmd, new ToolError(ErrorCodes.Unauthorized,
                $"Tenant '{cmd.TenantId}' not found.", 401));

        if (!tenant.IsActive)
            return Fail(cmd, new ToolError(ErrorCodes.Unauthorized,
                $"Tenant '{cmd.TenantId}' is inactive.", 403));

        // Namespace deny-by-default: a tenant with no allowlist entries cannot invoke
        // any tool. The wildcard "*" is the explicit opt-in for unrestricted access.
        if (!string.IsNullOrWhiteSpace(cmd.ToolNamespace))
        {
            var allowed = tenant.AllowedNamespaces;
            var isUnrestricted = allowed.Contains("*", StringComparer.OrdinalIgnoreCase);

            if (!isUnrestricted &&
                !allowed.Contains(cmd.ToolNamespace, StringComparer.OrdinalIgnoreCase))
            {
                return Fail(cmd, new ToolError(ErrorCodes.Unauthorized,
                    $"Tenant '{cmd.TenantId}' is not permitted to use namespace '{cmd.ToolNamespace}'. " +
                    "Grant access via Tenant.AllowNamespace(\"namespace\") or AllowNamespace(\"*\").",
                    403));
            }
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

        throw new UnauthorizedAccessException(error.Description);
    }
}

namespace ToolEngine.Application.Behaviors;

using MediatR;
using ToolEngine.Application.Abstractions;
using ToolEngine.Core.Abstractions.Persistence;
using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Core.Domain.Entities;

/// <summary>
/// Validates that the requesting tenant is active and is allowed to call the requested
/// namespace. Runs outermost after ValidationBehavior so auth precedes all business logic.
///
/// Namespace check: if Tenant.AllowedNamespaces is empty the tenant is unrestricted
/// (default for dev). If it has entries only those namespaces are permitted.
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
            return Fail(cmd, new ToolError("UNAUTHORIZED",
                $"Tenant '{cmd.TenantId}' not found.", 401));

        if (!tenant.IsActive)
            return Fail(cmd, new ToolError("UNAUTHORIZED",
                $"Tenant '{cmd.TenantId}' is inactive.", 403));

        // Namespace restriction — empty list means all namespaces permitted.
        if (!string.IsNullOrWhiteSpace(cmd.ToolNamespace) &&
            tenant.AllowedNamespaces.Count > 0 &&
            !tenant.AllowedNamespaces.Contains(
                cmd.ToolNamespace, StringComparer.OrdinalIgnoreCase))
        {
            return Fail(cmd, new ToolError("UNAUTHORIZED",
                $"Tenant '{cmd.TenantId}' is not permitted to use namespace '{cmd.ToolNamespace}'.",
                403));
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
                    [typeof(Guid), typeof(ToolError)])!;
            return (TResponse)method.Invoke(null, [cmd.CorrelationId, error])!;
        }

        throw new UnauthorizedAccessException(error.Description);
    }
}

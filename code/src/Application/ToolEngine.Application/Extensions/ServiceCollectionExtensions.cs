namespace ToolEngine.Application.Extensions;

using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using ToolEngine.Application.Behaviors;
using ToolEngine.Application.Commands;
using ToolEngine.Application.Handlers;
using ToolEngine.Core.Domain.Contracts;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers MediatR, pipeline behaviors, and validators from this assembly.
    /// Call after AddToolRegistry() and AddToolExecutor() in the composition root.
    ///
    /// Behavior execution order (outermost → innermost → handler):
    ///   TenantAuthorizationBehavior   — auth before validation (OWASP A01:2025)
    ///   → ValidationBehavior          — only reached by authorised callers
    ///   → TokenBudgetBehavior         — per-request token cap
    ///   → DailyBudgetBehavior         — per-tenant daily call cap
    ///   → LoopDetectionBehavior       — agent loop circuit-breaker
    ///   → ApprovalBehavior            — human-in-the-loop gate
    ///   → AuditBehavior               — persist invocation record
    ///   → Handler
    /// </summary>
    public static IServiceCollection AddToolApplication(
        this IServiceCollection services)
    {
        var assembly = typeof(ServiceCollectionExtensions).Assembly;

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(assembly));

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        // Pipeline — register outermost first.
        // Auth precedes Validation: unauthorized callers must not receive
        // detailed field-level validation errors (OWASP A01:2025).
        services.AddTransient(
            typeof(IPipelineBehavior<,>),
            typeof(TenantAuthorizationBehavior<,>));

        services.AddTransient(
            typeof(IPipelineBehavior<,>),
            typeof(ValidationBehavior<,>));

        services.AddTransient(
            typeof(IPipelineBehavior<,>),
            typeof(TokenBudgetBehavior<,>));

        services.AddTransient(
            typeof(IPipelineBehavior<,>),
            typeof(DailyBudgetBehavior<,>));

        services.AddTransient(
            typeof(IPipelineBehavior<,>),
            typeof(LoopDetectionBehavior<,>));

        services.AddTransient(
            typeof(IPipelineBehavior<,>),
            typeof(ApprovalBehavior<,>));

        services.AddTransient(
            typeof(IPipelineBehavior<,>),
            typeof(AuditBehavior<,>));

        // Default options for loop detection — override from config section "LoopDetection".
        services.AddOptions<LoopDetectionOptions>()
                .Configure(o => o.MaxCallsPerCorrelation = 10)
                .BindConfiguration("LoopDetection");

        // MediatR does not auto-register open generic handlers via assembly scan.
        // Register the JsonElement boundary type explicitly — used by all host entry points.
        services.AddTransient<
            IRequestHandler<
                ExecuteToolCommand<JsonElement, JsonElement>,
                ToolResponse<JsonElement>>,
            ExecuteToolCommandHandler<JsonElement, JsonElement>>();

        return services;
    }
}

using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using ToolEngine.Application.Behaviors;
using ToolEngine.Application.Orchestration;
using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Tools.Abstractions.Interfaces;

namespace ToolEngine.Application.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers MediatR, all pipeline behaviors in execution order,
    /// and FluentValidation validators.
    ///
    /// PIPELINE ORDER (enforced by registration sequence):
    ///   1. ValidationBehavior    — FluentValidation input gates
    ///   2. LoopDetectionBehavior — distributed loop guard (F4)
    ///   3. ApprovalBehavior      — human approval gate (E1/H3/H4)
    ///   4. AuditBehavior         — SOC 2 append-only audit (H1/H2/H4/H5)
    /// </summary>
    public static IServiceCollection AddApplicationLayer(
        this IServiceCollection services,
        params System.Reflection.Assembly[] assemblies)
    {
        // MediatR — scan all provided assemblies for IRequestHandler implementations
        services.AddMediatR(cfg =>
        {
            foreach (var asm in assemblies)
                cfg.RegisterServicesFromAssembly(asm);
        });

        // FluentValidation validators
        foreach (var asm in assemblies)
            services.AddValidatorsFromAssembly(asm, includeInternalTypes: true);

        // Pipeline behaviors — registration order = execution order in MediatR
        services.AddTransient(
            typeof(IPipelineBehavior<,>),
            typeof(ValidationBehavior<,>));

        services.AddTransient(
            typeof(IPipelineBehavior<,>),
            typeof(LoopDetectionBehavior<,>));

        services.AddTransient(
            typeof(IPipelineBehavior<,>),
            typeof(ApprovalBehavior<,>));

        services.AddTransient(
            typeof(IPipelineBehavior<,>),
            typeof(AuditBehavior<,>));

        return services;
    }

    /// <summary>
    /// Registers the Scenario Orchestration Layer:
    ///   - IScenarioRegistry (Singleton — shared across requests)
    ///   - IToolPlanOrchestrator → ToolPlanOrchestrator (Scoped — uses ISender)
    ///   - ScenarioRunner (Scoped — uses AppDbContext + IServiceProvider)
    /// Call RegisterScenarios(app.Services, registry) after build to populate the registry.
    /// </summary>
    public static IServiceCollection AddScenarioOrchestration(
        this IServiceCollection services)
    {
        services.AddSingleton<IScenarioRegistry, ScenarioRegistry>();
        services.AddScoped<IToolPlanOrchestrator, ToolPlanOrchestrator>();
        services.AddScoped<ScenarioRunner>();
        return services;
    }
}

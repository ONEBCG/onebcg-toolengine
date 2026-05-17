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
    /// Behavior order: ValidationBehavior (outer) → AuditBehavior (inner) → Handler.
    /// </summary>
    public static IServiceCollection AddToolApplication(
        this IServiceCollection services)
    {
        var assembly = typeof(ServiceCollectionExtensions).Assembly;

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(assembly));

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        // Outermost first
        services.AddTransient(
            typeof(IPipelineBehavior<,>),
            typeof(ValidationBehavior<,>));

        services.AddTransient(
            typeof(IPipelineBehavior<,>),
            typeof(AuditBehavior<,>));

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

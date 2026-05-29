using Microsoft.Extensions.DependencyInjection;
using ToolEngine.Tools.Abstractions.Interfaces;

namespace ToolEngine.Tools.Executor.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddToolExecutor(this IServiceCollection services)
    {
        services.AddSingleton<IToolExecutor,     ToolExecutor>();
        services.AddSingleton<IToolPlanExecutor, ToolPlanExecutor>();
        return services;
    }
}

namespace ToolEngine.Tools.Executor.Extensions;

using Microsoft.Extensions.DependencyInjection;
using ToolEngine.Tools.Abstractions.Base;
using ToolEngine.Tools.Abstractions.Interfaces;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddToolExecutor(
        this IServiceCollection services)
    {
        services.AddSingleton<IToolExecutor, ToolExecutor>();
        services.AddSingleton<IToolPlanExecutor, ToolPlanExecutor>();
        return services;
    }
}

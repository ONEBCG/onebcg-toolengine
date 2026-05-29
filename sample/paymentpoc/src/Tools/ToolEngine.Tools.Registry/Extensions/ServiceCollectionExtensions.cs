using Microsoft.Extensions.DependencyInjection;
using ToolEngine.Tools.Abstractions.Interfaces;

namespace ToolEngine.Tools.Registry.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddToolRegistry(this IServiceCollection services)
    {
        services.AddSingleton<IToolRegistry, ToolRegistry>();
        services.AddSingleton<IToolDiscovery, ToolDiscovery>();
        return services;
    }
}

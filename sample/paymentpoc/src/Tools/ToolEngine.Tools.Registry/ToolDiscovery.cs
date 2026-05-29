using Microsoft.Extensions.DependencyInjection;
using ToolEngine.Core.Domain.Enums;
using ToolEngine.Tools.Abstractions.Base;
using ToolEngine.Tools.Abstractions.Interfaces;
using ToolEngine.Tools.Abstractions.Models;

namespace ToolEngine.Tools.Registry;

public sealed class ToolDiscovery : IToolDiscovery
{
    private readonly IServiceProvider _services;

    public ToolDiscovery(IServiceProvider services) => _services = services;

    public Task<IReadOnlyList<ToolDescriptor>> DiscoverAsync(CancellationToken ct = default)
    {
        var descriptors = new List<ToolDescriptor>();

        // Walk all registered services looking for concrete ToolHandlerBase subtypes
        foreach (var service in _services.GetServices<object>())
        {
            if (service is not ToolHandlerBase handler) continue;

            var handlerType = handler.GetType();
            var toolType    = ResolveToolType(handlerType);
            var descriptor  = handler.ToDescriptor(toolType, handlerType);
            descriptors.Add(descriptor);
        }

        return Task.FromResult<IReadOnlyList<ToolDescriptor>>(descriptors.DistinctBy(d => d.FullName).ToList());
    }

    private static ToolType ResolveToolType(Type handlerType)
    {
        var baseType = handlerType.BaseType;
        while (baseType is not null)
        {
            if (baseType.IsGenericType)
            {
                var def = baseType.GetGenericTypeDefinition().Name;
                return def switch
                {
                    "LogicToolBase`2"     => ToolType.Logic,
                    "ApiToolBase`2"       => ToolType.Api,
                    "DatabaseToolBase`2"  => ToolType.Database,
                    "CompositeToolBase`2" => ToolType.Composite,
                    _                     => ToolType.Logic,
                };
            }
            baseType = baseType.BaseType;
        }
        return ToolType.Logic;
    }
}

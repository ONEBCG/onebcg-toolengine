using System.Collections.Concurrent;
using ToolEngine.Core.Domain.Common;
using ToolEngine.Tools.Abstractions.Interfaces;
using ToolEngine.Tools.Abstractions.Models;

namespace ToolEngine.Tools.Registry;

public sealed class ToolRegistry : IToolRegistry
{
    // Key format: "namespace.name@version" (all lowercase)
    private readonly ConcurrentDictionary<string, ToolDescriptor> _tools = new();

    public void Register(ToolDescriptor descriptor)
    {
        var key = MakeKey(descriptor.FullName, descriptor.Version);
        _tools[key] = descriptor;
    }

    public Result<ToolDescriptor> Resolve(string fullName, string version)
    {
        var key = MakeKey(fullName, version);
        if (!_tools.TryGetValue(key, out var descriptor))
            return Result.Failure<ToolDescriptor>(Error.NotFound("Tool", fullName));

        if (!descriptor.IsEnabled)
            return Result.Failure<ToolDescriptor>(
                Error.Validation($"Tool '{fullName}' is disabled."));

        return Result.Success(descriptor);
    }

    public IReadOnlyList<ToolDescriptor> ListAll() =>
        _tools.Values.ToList();

    public IReadOnlyList<ToolDescriptor> ListTools() =>
        _tools.Values
            .Where(d => d.IsEnabled)
            .OrderBy(d => d.FullName)
            .ToList();

    private static string MakeKey(string fullName, string version) =>
        $"{fullName.ToLowerInvariant()}@{version.ToLowerInvariant()}";
}

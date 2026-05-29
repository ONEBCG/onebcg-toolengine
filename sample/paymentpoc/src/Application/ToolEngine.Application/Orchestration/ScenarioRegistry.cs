using ToolEngine.Tools.Abstractions.Interfaces;
using ToolEngine.Tools.Abstractions.Models;

namespace ToolEngine.Application.Orchestration;

/// <summary>
/// In-memory scenario registry. Scenarios are registered at startup via
/// RegisterScenarios() in the payment tools extension and discovered the
/// same way tool descriptors are — using the IServiceProvider after build.
/// </summary>
public sealed class ScenarioRegistry : IScenarioRegistry
{
    private readonly Dictionary<string, IScenarioDefinition> _scenarios =
        new(StringComparer.OrdinalIgnoreCase);

    public void Register(IScenarioDefinition scenario)
        => _scenarios[$"{scenario.Name}@{scenario.Version}"] = scenario;

    public IScenarioDefinition? Resolve(string name, string version = "v1")
        => _scenarios.TryGetValue($"{name}@{version}", out var s) ? s : null;

    public IReadOnlyList<IScenarioDefinition> ListAll()
        => [.. _scenarios.Values.OrderBy(s => s.Name).ThenBy(s => s.Version)];
}

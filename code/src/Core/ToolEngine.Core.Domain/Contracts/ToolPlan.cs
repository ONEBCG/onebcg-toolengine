namespace ToolEngine.Core.Domain.Contracts;

using System.Text.Json;
using ToolEngine.Core.Domain.Enums;

/// <summary>
/// A multi-step execution plan. Steps are run according to ExecutionMode:
///   Sequential — one after another; halts on first failure.
///   Parallel   — all steps concurrently; collects all results.
///   Dag        — wave-based topological order driven by ToolStep.DependsOn edges.
/// </summary>
public sealed record ToolPlan(
    Guid                    CorrelationId,
    string                  TenantId,
    string                  UserId,
    ExecutionMode           Mode,
    IReadOnlyList<ToolStep> Steps);

/// <summary>
/// One step inside a ToolPlan.
/// DependsOn is only meaningful for Dag plans — list of StepIds that must
/// complete successfully before this step is eligible to run.
/// </summary>
public sealed record ToolStep(
    string       StepId,
    string       ToolNamespace,
    string       ToolName,
    string       ToolVersion,
    JsonElement  Input,
    string[]?    DependsOn = null)
{
    // "namespace.name" e.g. "weather.current"
    public string FullName => $"{ToolNamespace}.{ToolName}";
}

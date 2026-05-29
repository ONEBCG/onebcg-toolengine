using System.Text.Json;
using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Core.Domain.Enums;
using ToolEngine.Tools.Abstractions.Interfaces;
using ToolEngine.Tools.Abstractions.Models;

namespace ToolEngine.Tools.Executor;

public sealed class ToolPlanExecutor : IToolPlanExecutor
{
    private readonly IToolExecutor _executor;

    public ToolPlanExecutor(IToolExecutor executor) => _executor = executor;

    public async Task<IReadOnlyList<ToolPlanResult>> ExecuteAsync(
        ToolPlan plan, CancellationToken ct = default)
    {
        return plan.Mode switch
        {
            ExecutionMode.Sequential => await ExecuteSequentialAsync(plan, ct),
            ExecutionMode.Parallel   => await ExecuteParallelAsync(plan, ct),
            ExecutionMode.Dag        => await ExecuteDagAsync(plan, ct),
            _                        => throw new ArgumentOutOfRangeException(nameof(plan.Mode))
        };
    }

    private async Task<IReadOnlyList<ToolPlanResult>> ExecuteSequentialAsync(
        ToolPlan plan, CancellationToken ct)
    {
        var results = new List<ToolPlanResult>();

        foreach (var step in plan.Steps)
        {
            ct.ThrowIfCancellationRequested();
            var result = await ExecuteStepAsync(step, ct);
            results.Add(result);

            // Abort on first failure in sequential mode
            if (!result.Success) break;
        }

        return results;
    }

    private async Task<IReadOnlyList<ToolPlanResult>> ExecuteParallelAsync(
        ToolPlan plan, CancellationToken ct)
    {
        var tasks = plan.Steps.Select(step => ExecuteStepAsync(step, ct));
        var results = await Task.WhenAll(tasks);
        return results;
    }

    private async Task<IReadOnlyList<ToolPlanResult>> ExecuteDagAsync(
        ToolPlan plan, CancellationToken ct)
    {
        var completed = new Dictionary<string, ToolPlanResult>();

        while (completed.Count < plan.Steps.Count)
        {
            var ready = plan.Steps
                .Where(s => !completed.ContainsKey(s.StepId)
                         && s.DependsOn.All(dep => completed.ContainsKey(dep)
                                                 && completed[dep].Success))
                .ToList();

            if (ready.Count == 0 && completed.Count < plan.Steps.Count)
                break; // unresolvable DAG

            var tasks = ready.Select(step => ExecuteStepAsync(step, ct));
            var batch = await Task.WhenAll(tasks);
            foreach (var r in batch) completed[r.StepId] = r;
        }

        return plan.Steps
            .Select(s => completed.TryGetValue(s.StepId, out var r)
                ? r
                : new ToolPlanResult(s.StepId, false, null,
                    ToolError.Validation($"Step '{s.StepId}' not executed — dependency failed.")))
            .ToList();
    }

    private async Task<ToolPlanResult> ExecuteStepAsync(
        ToolStep step, CancellationToken ct)
    {
        var request = new ToolRequest<JsonElement>(
            CorrelationId:  Guid.NewGuid(),
            ToolName:       step.ToolName,
            ToolVersion:    step.Version,
            Input:          step.Input,
            ToolNamespace:  step.Namespace);

        var response = await _executor.ExecuteAsync<JsonElement, JsonElement>(request, ct);

        return new ToolPlanResult(
            StepId:  step.StepId,
            Success: response.Success,
            Data:    response.Data,
            Error:   response.Error);
    }
}

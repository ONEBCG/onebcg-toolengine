namespace ToolEngine.Tools.Executor;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Core.Domain.Enums;
using ToolEngine.Tools.Abstractions.Base;
using ToolEngine.Tools.Abstractions.Interfaces;

/// <summary>
/// Executes ToolPlans in Sequential, Parallel, or DAG mode.
///
/// Sequential — steps run one after another in list order. First failure marks all
///              remaining steps as Skipped and returns immediately.
///
/// Parallel   — all steps dispatched concurrently via Task.WhenAll. All results are
///              collected regardless of individual failures.
///
/// Dag        — wave-based topological execution. Each wave is the set of steps whose
///              DependsOn edges are fully satisfied by previously completed steps.
///              Within a wave, steps run in parallel. A step whose dependency failed
///              is marked Skipped rather than executed.
/// </summary>
public sealed class ToolPlanExecutor : IToolPlanExecutor
{
    private readonly IToolExecutor _executor;

    public ToolPlanExecutor(IToolExecutor executor) =>
        _executor = executor;

    public Task<ToolPlanResult> ExecuteAsync(ToolPlan plan, CancellationToken ct = default) =>
        plan.Mode switch
        {
            ExecutionMode.Sequential => ExecuteSequentialAsync(plan, ct),
            ExecutionMode.Parallel   => ExecuteParallelAsync(plan, ct),
            ExecutionMode.Dag        => ExecuteDagAsync(plan, ct),
            _                        => ExecuteSequentialAsync(plan, ct),
        };

    // -------------------------------------------------------------------------
    // Sequential
    // -------------------------------------------------------------------------

    private async Task<ToolPlanResult> ExecuteSequentialAsync(
        ToolPlan plan, CancellationToken ct)
    {
        var wall    = Stopwatch.StartNew();
        var results = new Dictionary<string, ToolStepResult>(plan.Steps.Count);
        var success = true;
        var failIdx = -1;

        for (var i = 0; i < plan.Steps.Count; i++)
        {
            var step = plan.Steps[i];

            if (failIdx >= 0)
            {
                // Earlier step failed — skip everything remaining.
                results[step.StepId] = Skipped(step);
                continue;
            }

            var stepResult = await RunStepAsync(plan, step, ct);
            results[step.StepId] = stepResult;

            if (!stepResult.Success)
            {
                success  = false;
                failIdx  = i;
            }
        }

        return BuildResult(plan, success, results, wall.Elapsed);
    }

    // -------------------------------------------------------------------------
    // Parallel
    // -------------------------------------------------------------------------

    private async Task<ToolPlanResult> ExecuteParallelAsync(
        ToolPlan plan, CancellationToken ct)
    {
        var wall  = Stopwatch.StartNew();
        var tasks = plan.Steps.Select(step => RunStepAsync(plan, step, ct));
        var all   = await Task.WhenAll(tasks);

        var results = all.ToDictionary(r => r.StepId);
        var success = all.All(r => r.Success);

        return BuildResult(plan, success, results, wall.Elapsed);
    }

    // -------------------------------------------------------------------------
    // DAG (wave-based topological execution)
    // -------------------------------------------------------------------------

    private async Task<ToolPlanResult> ExecuteDagAsync(
        ToolPlan plan, CancellationToken ct)
    {
        var wall      = Stopwatch.StartNew();
        var results   = new ConcurrentDictionary<string, ToolStepResult>();
        var completed = new HashSet<string>();   // StepIds that have finished (pass or fail)
        var failed    = new HashSet<string>();   // StepIds that failed

        while (completed.Count < plan.Steps.Count)
        {
            // Steps that are ready: deps all in completed, not yet started.
            var wave = plan.Steps
                .Where(s => !completed.Contains(s.StepId))
                .Where(s => (s.DependsOn ?? []).All(dep => completed.Contains(dep)))
                .ToList();

            // No progress — circular dependency or all remaining blocked by failures.
            if (wave.Count == 0) break;

            var waveTasks = wave.Select(async step =>
            {
                // If any declared dependency failed, skip this step.
                var blockedBy = (step.DependsOn ?? []).FirstOrDefault(dep => failed.Contains(dep));
                if (blockedBy is not null)
                {
                    results[step.StepId] = Skipped(step);
                    return;
                }

                var stepResult = await RunStepAsync(plan, step, ct);
                results[step.StepId] = stepResult;
            });

            await Task.WhenAll(waveTasks);

            // Advance completed + failed sets after the whole wave settles.
            foreach (var step in wave)
            {
                completed.Add(step.StepId);
                if (results.TryGetValue(step.StepId, out var r) && !r.Success && !r.Skipped)
                    failed.Add(step.StepId);
            }
        }

        // Any steps never reached (e.g. unreachable subgraph) — mark skipped.
        foreach (var step in plan.Steps.Where(s => !results.ContainsKey(s.StepId)))
            results[step.StepId] = Skipped(step);

        var success = results.Values.All(r => r.Success || r.Skipped == false && r.Skipped);
        // Overall success: true only when every executed (non-skipped) step succeeded.
        success = results.Values
            .Where(r => !r.Skipped)
            .All(r => r.Success);

        return BuildResult(plan, success, results, wall.Elapsed);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<ToolStepResult> RunStepAsync(
        ToolPlan plan, ToolStep step, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            var request = new ToolRequest<JsonElement>(
                plan.CorrelationId,
                plan.TenantId,
                ToolName:      step.ToolName,
                ToolVersion:   step.ToolVersion,
                Input:         step.Input,
                ToolNamespace: step.ToolNamespace,
                UserId:        plan.UserId);

            var response = await _executor
                .ExecuteAsync<JsonElement, JsonElement>(request, ct);

            sw.Stop();

            return new ToolStepResult(
                step.StepId,
                step.FullName,
                response.Success,
                response.Data,
                response.Error,
                sw.Elapsed);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ToolStepResult(
                step.StepId,
                step.FullName,
                Success: false,
                Output:  null,
                Error:   ToolError.Internal(ex.Message),
                Duration: sw.Elapsed);
        }
    }

    private static ToolStepResult Skipped(ToolStep step) =>
        new(step.StepId, step.FullName,
            Success:  false,
            Output:   null,
            Error:    null,
            Duration: TimeSpan.Zero,
            Skipped:  true);

    private static ToolPlanResult BuildResult(
        ToolPlan                              plan,
        bool                                  success,
        IDictionary<string, ToolStepResult>   results,
        TimeSpan                              wallClock) =>
        new(plan.CorrelationId,
            success,
            results as IReadOnlyDictionary<string, ToolStepResult>
                ?? new Dictionary<string, ToolStepResult>(results),
            new ToolUsageMetrics(wallClock),
            DateTimeOffset.UtcNow);
}

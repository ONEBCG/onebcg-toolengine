using System.Text.Json;
using MediatR;
using ToolEngine.Application.Commands;
using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Core.Domain.Enums;
using ToolEngine.Tools.Abstractions.Interfaces;
using ToolEngine.Tools.Abstractions.Models;

namespace ToolEngine.Application.Orchestration;

/// <summary>
/// Governed plan executor. Routes every step through the MediatR behavior
/// pipeline (Validation → LoopDetection → Approval → Audit) via ISender.
///
/// Replaces ToolPlanExecutor on all paths that require governance.
/// ToolPlanExecutor remains for internal/trusted use cases where behaviors
/// are intentionally bypassed.
///
/// Resume behaviour: on ResumeAsync, starts at the step AFTER the one that
/// was suspended (the suspended step already completed its work — e.g.,
/// compile-dossier already submitted the approval request).
/// </summary>
public sealed class ToolPlanOrchestrator : IToolPlanOrchestrator
{
    private readonly ISender _mediator;

    public ToolPlanOrchestrator(ISender mediator) => _mediator = mediator;

    public async Task<OrchestratorResult> ExecuteAsync(
        ToolPlan      plan,
        string?       userId,
        CallerType    callerType,
        StepContext?  resumeContext     = null,
        string?       resumeFromStepId = null,
        CancellationToken ct           = default)
    {
        var context = resumeContext ?? new StepContext();
        var results = new List<StepResult>();
        var steps   = OrderedSteps(plan);

        // On resume: start from the step AFTER the suspended one.
        // The suspended step already completed its action (e.g., submitted the
        // approval request); rerunning it would create a duplicate approval.
        var startIndex = 0;
        if (resumeFromStepId is not null)
        {
            var suspendedIndex = steps.FindIndex(
                s => s.StepId.Equals(resumeFromStepId, StringComparison.Ordinal));
            if (suspendedIndex >= 0)
                startIndex = suspendedIndex + 1;
        }

        for (var i = startIndex; i < steps.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var step = steps[i];

            // Merge static input with any mapped values from prior step outputs
            var resolvedInput = context.ResolveInput(step.Input, step.OutputMappings);

            var response = await _mediator.Send(new ExecuteToolCommand(
                CorrelationId:  Guid.NewGuid(),
                ToolNamespace:  step.Namespace,
                ToolName:       step.ToolName,
                ToolVersion:    step.Version,
                Input:          resolvedInput,
                UserId:         userId,
                CallerType:     callerType,
                IdempotencyKey: $"{plan.PlanId}:{step.StepId}"), ct);

            // Approval gate triggered — persist state and return to caller
            if (response.IsSuspended)
                return OrchestratorResult.Suspended(
                    response.PendingInvocationId!.Value,
                    step.StepId,
                    context,
                    results);

            // Extract data from successful response and store in context
            JsonElement? data = null;
            if (response is ToolResponse<JsonElement> typed
                && typed.Success
                && typed.Data.ValueKind != JsonValueKind.Undefined)
            {
                data = typed.Data;
                context.SetStepOutput(step.StepId, typed.Data);
            }

            results.Add(new StepResult(step.StepId, response.Success, data, response.Error));

            if (!response.Success)
                return OrchestratorResult.Failed(step.StepId, response.Error, results);
        }

        return OrchestratorResult.Completed(context, results);
    }

    // DAG mode: topological sort respecting DependsOn. Sequential/Parallel: preserve declaration order.
    private static List<ToolStep> OrderedSteps(ToolPlan plan)
    {
        if (plan.Mode != ExecutionMode.Dag)
            return plan.Steps.ToList();

        var remaining = plan.Steps.ToList();
        var sorted    = new List<ToolStep>();
        var completed = new HashSet<string>(StringComparer.Ordinal);

        while (remaining.Count > 0)
        {
            var ready = remaining
                .Where(s => s.DependsOn.All(dep => completed.Contains(dep)))
                .ToList();

            if (ready.Count == 0)
                break; // Cycle or unresolvable dependency — return what we have

            foreach (var step in ready)
            {
                sorted.Add(step);
                completed.Add(step.StepId);
            }
            remaining.RemoveAll(s => completed.Contains(s.StepId));
        }

        return sorted;
    }
}

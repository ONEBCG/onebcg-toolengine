using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ToolEngine.Core.Domain.Entities;
using ToolEngine.Core.Domain.Enums;
using ToolEngine.Infrastructure.Persistence;
using ToolEngine.Tools.Abstractions.Interfaces;
using ToolEngine.Tools.Abstractions.Models;

namespace ToolEngine.Application.Orchestration;

/// <summary>
/// Application entry point for running and resuming named scenarios.
/// Responsibilities:
///   1. Resolve scenario definition from IScenarioRegistry.
///   2. Run optional IRequiresSetup hook (e.g., create PaymentInstruction).
///   3. Build the ToolPlan via IScenarioDefinition.Build(input).
///   4. Persist ScenarioExecution to DB for durability / resume support.
///   5. Delegate plan execution to IToolPlanOrchestrator.
///   6. Update ScenarioExecution with final status + serialised StepContext.
/// </summary>
public sealed class ScenarioRunner
{
    private readonly IScenarioRegistry     _registry;
    private readonly IToolPlanOrchestrator _orchestrator;
    private readonly AppDbContext          _db;
    private readonly IServiceProvider      _services;

    public ScenarioRunner(
        IScenarioRegistry     registry,
        IToolPlanOrchestrator orchestrator,
        AppDbContext          db,
        IServiceProvider      services)
    {
        _registry     = registry;
        _orchestrator = orchestrator;
        _db           = db;
        _services     = services;
    }

    public async Task<ScenarioRunResult> RunAsync(
        string     scenarioName,
        string     scenarioVersion,
        JsonElement input,
        string?    userId,
        CallerType callerType,
        CancellationToken ct)
    {
        var definition = _registry.Resolve(scenarioName, scenarioVersion);
        if (definition is null)
            return ScenarioRunResult.NotFound(scenarioName, scenarioVersion);

        // Optional setup (e.g., create PaymentInstruction, inject PRID into input)
        if (definition is IRequiresSetup setup)
        {
            try
            {
                input = await setup.SetupAsync(input, _services, ct);
            }
            catch (Exception ex)
            {
                return ScenarioRunResult.SetupFailed(ex.Message);
            }
        }

        ToolPlan plan;
        try
        {
            plan = definition.Build(input);
        }
        catch (Exception ex)
        {
            return ScenarioRunResult.BuildFailed(ex.Message);
        }

        var execution = ScenarioExecution.Start(scenarioName, scenarioVersion, input, userId);
        _db.Set<ScenarioExecution>().Add(execution);
        await _db.SaveChangesAsync(ct);

        var result = await _orchestrator.ExecuteAsync(plan, userId, callerType, ct: ct);

        ApplyOrchestratorResult(execution, result);
        await _db.SaveChangesAsync(ct);

        return ScenarioRunResult.FromOrchestration(execution.Id, result);
    }

    public async Task<ScenarioRunResult> ResumeAsync(
        Guid       executionId,
        string?    userId,
        CallerType callerType,
        CancellationToken ct)
    {
        var execution = await _db.Set<ScenarioExecution>()
            .FirstOrDefaultAsync(e => e.Id == executionId, ct);

        if (execution is null)
            return ScenarioRunResult.NotFound(executionId.ToString(), "execution");

        if (execution.Status != ToolEngine.Core.Domain.Enums.ScenarioStatus.Suspended)
            return ScenarioRunResult.InvalidState(
                $"Execution '{executionId}' is {execution.Status} — only Suspended executions can be resumed.");

        var definition = _registry.Resolve(execution.ScenarioName, execution.ScenarioVersion);
        if (definition is null)
            return ScenarioRunResult.NotFound(execution.ScenarioName, execution.ScenarioVersion);

        var originalInput = JsonSerializer.Deserialize<JsonElement>(execution.InputJson);
        var plan          = definition.Build(originalInput);
        var context       = StepContext.Deserialise(execution.StepContextJson);

        execution.Resume();
        await _db.SaveChangesAsync(ct);

        var result = await _orchestrator.ExecuteAsync(
            plan, userId, callerType,
            resumeContext:     context,
            resumeFromStepId:  execution.SuspendedAtStepId,
            ct: ct);

        ApplyOrchestratorResult(execution, result);
        await _db.SaveChangesAsync(ct);

        return ScenarioRunResult.FromOrchestration(execution.Id, result);
    }

    private static void ApplyOrchestratorResult(ScenarioExecution execution, OrchestratorResult result)
    {
        if (result.IsCompleted)
            execution.Complete(result.Context.Serialise());
        else if (result.IsSuspended)
            execution.Suspend(result.SuspendedAtStepId!, result.PendingApprovalId!.Value,
                result.Context.Serialise());
        else
            execution.Fail(result.FailedAtStepId!, result.FailureError?.Description);
    }
}

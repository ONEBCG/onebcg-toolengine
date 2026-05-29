using ToolEngine.Tools.Abstractions.Models;

namespace ToolEngine.Application.Orchestration;

/// <summary>Result returned by ScenarioRunner.RunAsync / ResumeAsync.</summary>
public sealed class ScenarioRunResult
{
    public bool                      IsSuccess   { get; private init; }
    public bool                      IsNotFound  { get; private init; }
    public bool                      IsSuspended { get; private init; }
    public bool                      IsFailed    { get; private init; }
    public Guid?                     ExecutionId { get; private init; }
    public string?                   Error       { get; private init; }
    public Guid?                     PendingApprovalId   { get; private init; }
    public string?                   SuspendedAtStepId   { get; private init; }
    public IReadOnlyList<StepResult> CompletedSteps      { get; private init; } = [];

    public static ScenarioRunResult FromOrchestration(Guid executionId, OrchestratorResult result)
    {
        if (result.IsCompleted)
            return new() { IsSuccess = true, ExecutionId = executionId,
                           CompletedSteps = result.CompletedSteps };

        if (result.IsSuspended)
            return new() { IsSuspended = true, ExecutionId = executionId,
                           PendingApprovalId = result.PendingApprovalId,
                           SuspendedAtStepId = result.SuspendedAtStepId,
                           CompletedSteps    = result.CompletedSteps };

        return new() { IsFailed = true, ExecutionId = executionId,
                       Error          = result.FailureError?.Description,
                       CompletedSteps = result.CompletedSteps };
    }

    public static ScenarioRunResult NotFound(string name, string version)
        => new() { IsNotFound = true,
                   Error      = $"Scenario '{name}' version '{version}' is not registered." };

    public static ScenarioRunResult SetupFailed(string message)
        => new() { IsFailed = true, Error = $"Scenario setup failed: {message}" };

    public static ScenarioRunResult BuildFailed(string message)
        => new() { IsFailed = true, Error = $"Plan build failed: {message}" };

    public static ScenarioRunResult InvalidState(string message)
        => new() { IsFailed = true, Error = message };
}

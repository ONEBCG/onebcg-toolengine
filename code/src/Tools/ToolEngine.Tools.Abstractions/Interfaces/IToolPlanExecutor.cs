namespace ToolEngine.Tools.Abstractions.Interfaces;

using ToolEngine.Core.Domain.Contracts;

/// <summary>
/// Executes a ToolPlan — a multi-step sequence, parallel batch, or DAG of tool calls.
/// Implemented in ToolEngine.Tools.Executor; declared here to avoid a circular dependency.
/// </summary>
public interface IToolPlanExecutor
{
    /// <summary>
    /// Execute all steps in the plan according to plan.Mode.
    /// Always returns a result — never throws. Individual step failures are captured
    /// in ToolPlanResult.StepResults; the overall Success flag is false if any step failed.
    /// </summary>
    Task<ToolPlanResult> ExecuteAsync(ToolPlan plan, CancellationToken ct = default);
}

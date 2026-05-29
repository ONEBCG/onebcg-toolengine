using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ToolEngine.Application.Orchestration;
using ToolEngine.Core.Domain.Enums;
using ToolEngine.Tools.Abstractions.Interfaces;
using ToolEngine.Tools.Abstractions.Models;

namespace ToolEngine.Api.Controllers;

[ApiController]
[Route("api/v1/plans")]
[Authorize]
[Tags("Plans")]
public sealed class PlansController : ControllerBase
{
    private readonly IToolPlanOrchestrator _orchestrator;

    public PlansController(IToolPlanOrchestrator orchestrator) => _orchestrator = orchestrator;

    /// <summary>
    /// Execute a declarative ToolPlan. mode: 0=Sequential, 1=Parallel, 2=DAG.
    /// Runs through the full orchestration pipeline (approval gates, audit, suspension).
    /// Returns 200 (completed), 202 (suspended at approval gate), or 422 (failed at a step).
    /// </summary>
    [HttpPost("execute")]
    [ProducesResponseType<ExecutePlanResponse>(200)]
    [ProducesResponseType(202)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> ExecutePlan(
        [FromBody] ToolPlan plan,
        CancellationToken ct)
    {
        var result = await _orchestrator.ExecuteAsync(
            plan,
            userId:     User.FindFirst("sub")?.Value,
            callerType: CallerType.Human,
            ct:         ct);

        var response = new ExecutePlanResponse(
            PlanId:            plan.PlanId,
            Mode:              plan.Mode,
            Status:            result.IsCompleted ? "Completed"
                             : result.IsSuspended ? "Suspended"
                             : "Failed",
            CompletedSteps:    result.CompletedSteps,
            SuspendedAtStepId: result.SuspendedAtStepId,
            PendingApprovalId: result.PendingApprovalId,
            FailedAtStepId:    result.FailedAtStepId,
            Error:             result.FailureError?.Description);

        if (result.IsSuspended)
        {
            Response.Headers.Append("Location",
                $"/api/v1/approvals/{result.PendingApprovalId}");
            return Accepted(response);
        }

        return result.IsFailed ? UnprocessableEntity(response) : Ok(response);
    }
}

public sealed record ExecutePlanResponse(
    Guid                             PlanId,
    ExecutionMode                    Mode,
    string                           Status,
    IReadOnlyList<StepResult>        CompletedSteps,
    string?                          SuspendedAtStepId  = null,
    Guid?                            PendingApprovalId  = null,
    string?                          FailedAtStepId     = null,
    string?                          Error              = null);

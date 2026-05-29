using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ToolEngine.Application.Orchestration;
using ToolEngine.Core.Domain.Enums;
using ToolEngine.Tools.Abstractions.Interfaces;

namespace ToolEngine.Api.Controllers;

[ApiController]
[Route("api/v1/scenarios")]
[Authorize]
[Tags("Scenarios")]
public sealed class ScenariosController : ControllerBase
{
    private readonly IScenarioRegistry _registry;
    private readonly ScenarioRunner    _runner;

    public ScenariosController(IScenarioRegistry registry, ScenarioRunner runner)
    {
        _registry = registry;
        _runner   = runner;
    }

    /// <summary>List all registered scenario definitions with name, version, description, and input schema.</summary>
    [HttpGet]
    [ProducesResponseType(200)]
    public IActionResult ListScenarios()
    {
        var scenarios = _registry.ListAll()
            .Select(s => new
            {
                name        = s.Name,
                version     = s.Version,
                description = s.Description,
                inputSchema = s.InputSchema,
            });
        return Ok(scenarios);
    }

    /// <summary>
    /// Run a named scenario. Returns 200 (completed), 202 (suspended at approval gate),
    /// or 422 (failed at a step). The executionId in the response can be used to resume
    /// after approval is granted.
    /// </summary>
    [HttpPost("{name}/run")]
    [ProducesResponseType(200)]
    [ProducesResponseType(202)]
    [ProducesResponseType(404)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> RunScenario(
        string name,
        [FromBody] RunScenarioRequest req,
        CancellationToken ct)
    {
        var result = await _runner.RunAsync(
            name,
            req.Version ?? "v1",
            req.Input,
            User.FindFirst("sub")?.Value,
            CallerType.Human,
            ct);

        if (result.IsNotFound)
            return NotFound(new { error = result.Error });

        if (result.IsFailed)
            return UnprocessableEntity(new
            {
                executionId    = result.ExecutionId,
                status         = "Failed",
                error          = result.Error,
                completedSteps = result.CompletedSteps,
            });

        if (result.IsSuspended)
        {
            Response.Headers.Append("Location",
                $"/api/v1/approvals/{result.PendingApprovalId}");
            return Accepted(new
            {
                executionId       = result.ExecutionId,
                status            = "Suspended",
                suspendedAtStep   = result.SuspendedAtStepId,
                pendingApprovalId = result.PendingApprovalId,
                completedSteps    = result.CompletedSteps,
                resumeUrl         = $"/api/v1/scenarios/{result.ExecutionId}/resume",
            });
        }

        return Ok(new
        {
            executionId    = result.ExecutionId,
            status         = "Completed",
            completedSteps = result.CompletedSteps,
        });
    }

    /// <summary>
    /// Resume a suspended scenario execution after approval has been granted.
    /// Use the executionId returned from the run endpoint.
    /// </summary>
    [HttpPost("{executionId:guid}/resume")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> ResumeScenario(Guid executionId, CancellationToken ct)
    {
        var result = await _runner.ResumeAsync(
            executionId,
            User.FindFirst("sub")?.Value,
            CallerType.Human,
            ct);

        if (result.IsNotFound)
            return NotFound(new { error = result.Error });

        if (result.IsFailed)
            return UnprocessableEntity(new
            {
                executionId = result.ExecutionId,
                status      = "Failed",
                error       = result.Error,
            });

        if (result.IsSuspended)
        {
            Response.Headers.Append("Location",
                $"/api/v1/approvals/{result.PendingApprovalId}");
            return Accepted(new
            {
                executionId       = result.ExecutionId,
                status            = "Suspended",
                pendingApprovalId = result.PendingApprovalId,
                resumeUrl         = $"/api/v1/scenarios/{result.ExecutionId}/resume",
            });
        }

        return Ok(new
        {
            executionId    = result.ExecutionId,
            status         = "Completed",
            completedSteps = result.CompletedSteps,
        });
    }
}

public sealed record RunScenarioRequest(JsonElement Input, string? Version = "v1");

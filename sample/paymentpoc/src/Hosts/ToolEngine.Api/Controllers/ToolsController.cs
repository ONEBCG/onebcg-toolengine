using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ToolEngine.Application.Commands;
using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Core.Domain.Enums;
using ToolEngine.Tools.Abstractions.Interfaces;
using ToolEngine.Tools.Abstractions.Models;

namespace ToolEngine.Api.Controllers;

[ApiController]
[Route("api/v1/tools")]
[Authorize]
[Tags("Tools")]
public sealed class ToolsController : ControllerBase
{
    private readonly IToolRegistry _registry;
    private readonly ISender       _mediator;

    public ToolsController(IToolRegistry registry, ISender mediator)
    {
        _registry = registry;
        _mediator = mediator;
    }

    /// <summary>List all registered tools. Returns flat ToolSummaryResponse (no metadata sub-object).</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ToolSummaryResponse>>(200)]
    public IActionResult ListTools()
    {
        var tools = _registry.ListTools()
            .Select(d => new ToolSummaryResponse(
                FullName:     d.FullName,
                Namespace:    d.Namespace,
                Name:         d.Name,
                Version:      d.Version,
                Description:  d.Schema.Description,
                Type:         (int)d.Type,
                IsEnabled:    d.IsEnabled,
                InputSchema:  d.Schema.InputSchema,
                OutputSchema: d.Schema.OutputSchema))
            .ToList();

        return Ok(tools);
    }

    /// <summary>Get a single tool by namespace, name, and version.</summary>
    [HttpGet("{ns}/{name}/{version}")]
    [ProducesResponseType<ToolSummaryResponse>(200)]
    [ProducesResponseType(404)]
    public IActionResult GetTool(string ns, string name, string version)
    {
        var result = _registry.Resolve($"{ns}.{name}", version);

        if (result.IsFailure)
            return NotFound(new { error = result.Error.Description });

        var d = result.Value;
        return Ok(new ToolSummaryResponse(
            FullName:     d.FullName,
            Namespace:    d.Namespace,
            Name:         d.Name,
            Version:      d.Version,
            Description:  d.Schema.Description,
            Type:         (int)d.Type,
            IsEnabled:    d.IsEnabled,
            InputSchema:  d.Schema.InputSchema,
            OutputSchema: d.Schema.OutputSchema));
    }

    /// <summary>
    /// Invoke any registered tool by namespace/name/version.
    /// Runs through the full MediatR pipeline (Validation → Loop Detection → Approval → Audit).
    /// </summary>
    [HttpPost("invoke")]
    [ProducesResponseType<IToolResponse>(200)]
    [ProducesResponseType(202)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> InvokeTool(
        [FromBody] InvokeToolRequest req,
        CancellationToken ct)
    {
        var command = new ExecuteToolCommand(
            CorrelationId:          req.CorrelationId ?? Guid.NewGuid(),
            ToolNamespace:          req.Namespace,
            ToolName:               req.Name,
            ToolVersion:            req.Version,
            Input:                  req.Input,
            UserId:                 User.FindFirst("sub")?.Value,
            CallerType:             req.CallerType,
            GovernanceMetadataJson: req.GovernanceMetadataJson,
            IdempotencyKey:         req.IdempotencyKey,
            MaxResponseTokens:      req.MaxResponseTokens ?? 4096);

        var response = await _mediator.Send(command, ct);

        if (response.IsSuspended)
        {
            Response.Headers.Append("Location",    $"/api/v1/approvals/{response.PendingInvocationId}");
            Response.Headers.Append("Retry-After", "3600");
            return Accepted(response);
        }

        return response.Success ? Ok(response) : UnprocessableEntity(response);
    }
}

// ── Request DTOs ──────────────────────────────────────────────────────────────

public sealed record InvokeToolRequest(
    string      Namespace,
    string      Name,
    string      Version,
    JsonElement Input,
    Guid?       CorrelationId          = null,
    CallerType  CallerType             = CallerType.Human,
    string?     GovernanceMetadataJson = null,
    string?     IdempotencyKey         = null,
    int?        MaxResponseTokens      = null);

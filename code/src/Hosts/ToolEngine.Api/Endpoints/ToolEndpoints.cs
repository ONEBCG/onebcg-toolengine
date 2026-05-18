namespace ToolEngine.Api.Endpoints;

using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using ToolEngine.Application.Commands;
using ToolEngine.Api.Streaming;
using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Core.Domain.Enums;
using ToolEngine.Core.Domain.Schema;
using ToolEngine.Tools.Abstractions.Metadata;
using ToolEngine.Tools.Registry;

public static class ToolEndpoints
{
    public static WebApplication MapToolEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/tools")
                       .WithTags("Tools");

        // GET /tools — list all registered tools
        group.MapGet("/", ListTools)
             .WithName("ListTools")
             .WithSummary("List all registered tools.");

        // GET /tools/{ns}/{name}/versions — list versions for a specific tool
        group.MapGet("/{ns}/{name}/versions", GetVersions)
             .WithName("GetToolVersions")
             .WithSummary("List all registered versions for a tool.");

        // POST /tools/{ns}/{name}/{version}/invoke — synchronous invocation
        group.MapPost("/{ns}/{name}/{version}/invoke", InvokeTool)
             .WithName("InvokeTool")
             .WithSummary("Invoke a tool and return a complete response.")
             .RequireAuthorization();

        // POST /tools/{ns}/{name}/{version}/stream — SSE streaming invocation
        group.MapPost("/{ns}/{name}/{version}/stream", StreamTool)
             .WithName("StreamTool")
             .WithSummary("Invoke a tool and stream the response as SSE.")
             .RequireAuthorization();

        return app;
    }

    private static IResult ListTools(IToolRegistry registry) =>
        Results.Ok(registry.ListAll().Select(ToolSummaryResponse.From).ToList());

    private static IResult GetVersions(string ns, string name, IToolRegistry registry)
    {
        var versions = registry.GetVersions(ns, name);
        return versions.Count == 0
            ? Results.NotFound(new { error = $"No tool '{ns}.{name}' is registered." })
            : Results.Ok(versions);
    }

    private static async Task<IResult> InvokeTool(
        string                 ns,
        string                 name,
        string                 version,
        [FromBody] JsonElement  body,
        HttpContext             ctx,
        IMediator              mediator,
        CancellationToken      ct)
    {
        var (correlationId, tenantId, userId) = ExtractContext(ctx);

        // Idempotency-Key header — prevents duplicate PendingApproval creation on retry.
        // Clients should send a stable, client-generated key (e.g. UUID v4) for retryable calls.
        var idempotencyKey = ctx.Request.Headers.TryGetValue("Idempotency-Key", out var ik)
            ? ik.ToString()
            : null;

        // H4 — caller_type JWT claim: distinguishes AI agent invocations from human users.
        var callerType = ctx.User.FindFirst("caller_type")?.Value switch
        {
            "ai_agent"       => ToolEngine.Core.Domain.Enums.CallerType.AiAgent,
            "system_service" => ToolEngine.Core.Domain.Enums.CallerType.SystemService,
            _                => ToolEngine.Core.Domain.Enums.CallerType.Human
        };

        // H5 — ISO 42001 governance metadata: verbatim JSON from X-Governance-Metadata header.
        var governanceMetadata = ctx.Request.Headers.TryGetValue("X-Governance-Metadata", out var gm)
            ? gm.ToString()
            : null;

        var command = new ExecuteToolCommand<JsonElement, JsonElement>(
            correlationId, tenantId, userId,
            ToolName:               name,
            ToolVersion:            version,
            Input:                  body,
            ToolType:               ToolType.Logic,
            ToolNamespace:          ns,
            IdempotencyKey:         idempotencyKey,
            CallerType:             callerType,
            GovernanceMetadataJson: governanceMetadata);

        var response = await mediator.Send(command, ct);

        // Approval suspended — return 202 Accepted.
        // RFC 7231 §6.3.3: Location header points to the status resource.
        // Retry-After guides client polling interval (seconds).
        if (response.PendingInvocationId.HasValue)
        {
            var pollUrl = $"/invocations/{response.PendingInvocationId}/status";
            ctx.Response.Headers["Retry-After"] = "10";
            return Results.Accepted(
                pollUrl,
                new
                {
                    status       = "pending_approval",
                    invocationId = response.PendingInvocationId,
                    pollUrl,
                    message      = response.Error?.Description
                });
        }

        return response.Success
            ? Results.Ok(response)
            : Results.Problem(
                detail:     response.Error!.Description,
                statusCode: response.Error.HttpStatusCode,
                title:      response.Error.Code);
    }

    private static async Task StreamTool(
        string                 ns,
        string                 name,
        string                 version,
        [FromBody] JsonElement  body,
        HttpContext             ctx,
        IToolRegistry          registry,
        ToolEngine.Tools.Abstractions.Base.IToolExecutor executor,
        CancellationToken      ct)
    {
        var (correlationId, tenantId, _) = ExtractContext(ctx);

        ctx.Response.ContentType          = "text/event-stream";
        ctx.Response.Headers.CacheControl = "no-cache";
        ctx.Response.Headers.Connection   = "keep-alive";

        var request = new ToolRequest<JsonElement>(
            correlationId, tenantId,
            ToolName:      name,
            ToolVersion:   version,
            Input:         body,
            ToolNamespace: ns);

        var resolve = registry.Resolve(ns, name, version, tenantId);
        if (resolve.IsFailure)
        {
            await SseWriter.WriteErrorAsync(ctx.Response, resolve.Error.Description, ct);
            return;
        }

        var handler = ctx.RequestServices.GetService(resolve.Value.HandlerType)
                          as ToolEngine.Tools.Abstractions.Interfaces.IToolHandler<JsonElement, JsonElement>;

        if (handler is null)
        {
            await SseWriter.WriteErrorAsync(ctx.Response, "Handler not found in DI container.", ct);
            return;
        }

        await foreach (var chunk in handler.StreamAsync(request, ct))
            await SseWriter.WriteChunkAsync(ctx.Response, chunk, ct);
    }

    private static (Guid correlationId, string tenantId, string userId) ExtractContext(
        HttpContext ctx)
    {
        var correlationId = ctx.Request.Headers.TryGetValue("X-Correlation-Id", out var hdr)
                            && Guid.TryParse(hdr, out var parsed)
            ? parsed
            : Guid.NewGuid();

        var tenantId = ctx.User.FindFirst("tenant_id")?.Value ?? "anonymous";
        var userId   = ctx.User.FindFirst(
                           System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                       ?? "anonymous";

        return (correlationId, tenantId, userId);
    }
}

/// <summary>
/// Serializable projection of <see cref="ToolDescriptor"/> for the GET /tools listing.
/// Excludes <c>HandlerType</c> (<see cref="System.Type"/>) which STJ cannot serialize.
/// </summary>
internal sealed record ToolSummaryResponse(
    string     FullName,
    string     Namespace,
    string     Name,
    string     Version,
    string     Description,
    ToolType   Type,
    bool       IsEnabled,
    string?    TenantId,
    ToolSchema InputSchema,
    ToolSchema OutputSchema)
{
    internal static ToolSummaryResponse From(ToolDescriptor d) => new(
        FullName:     d.FullName,
        Namespace:    d.Metadata.Namespace,
        Name:         d.Metadata.Name,
        Version:      d.Metadata.Version,
        Description:  d.Metadata.Description,
        Type:         d.Metadata.Type,
        IsEnabled:    d.Metadata.IsEnabled,
        TenantId:     d.TenantId,
        InputSchema:  d.Metadata.InputSchema,
        OutputSchema: d.Metadata.OutputSchema);
}

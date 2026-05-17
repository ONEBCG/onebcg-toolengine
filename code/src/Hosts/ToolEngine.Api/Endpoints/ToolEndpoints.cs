namespace ToolEngine.Api.Endpoints;

using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using ToolEngine.Application.Commands;
using ToolEngine.Api.Streaming;
using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Core.Domain.Enums;
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
        Results.Ok(registry.ListAll());

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

        var command = new ExecuteToolCommand<JsonElement, JsonElement>(
            correlationId, tenantId, userId,
            ToolName:      name,
            ToolVersion:   version,
            Input:         body,
            ToolType:      ToolType.Logic,
            ToolNamespace: ns);

        var response = await mediator.Send(command, ct);

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

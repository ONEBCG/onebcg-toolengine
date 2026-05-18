namespace ToolEngine.Api.Endpoints;

using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using ToolEngine.Api.Streaming;
using ToolEngine.Llm.Commands;

public static class AgentEndpoints
{
    public static WebApplication MapAgentEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/agent")
                       .WithTags("Agent");

        // POST /agent/chat — single-turn or multi-turn text-based agent invocation
        group.MapPost("/chat", AgentChat)
             .WithName("AgentChat")
             .WithSummary("Submit free-text input. LLM selects and invokes the appropriate tool.")
             .RequireAuthorization();

        // POST /agent/chat/stream — SSE streaming version
        group.MapPost("/chat/stream", AgentChatStream)
             .WithName("AgentChatStream")
             .WithSummary("Submit free-text input with SSE streaming response.")
             .RequireAuthorization();

        return app;
    }

    private static async Task<IResult> AgentChat(
        [FromBody] AgentChatRequest body,
        HttpContext                 ctx,
        IMediator                  mediator,
        CancellationToken          ct)
    {
        var (correlationId, tenantId, userId) = ExtractContext(ctx);

        // Optional provider override from header
        var providerOverride = ctx.Request.Headers.TryGetValue("X-Llm-Provider", out var hdr)
            ? hdr.ToString() : null;

        var command = new AgentChatCommand(
            correlationId,
            tenantId,
            userId,
            body.Text,
            body.SessionId,
            LlmProviderOverride: providerOverride);

        var response = await mediator.Send(command, ct);

        if (response.PendingInvocationId.HasValue)
        {
            var pollUrl = $"/invocations/{response.PendingInvocationId}/status";
            ctx.Response.Headers["Retry-After"] = "10";
            return Results.Accepted(pollUrl, new
            {
                status       = "pending_approval",
                invocationId = response.PendingInvocationId,
                pollUrl,
                sessionId    = response.SessionId
            });
        }

        if (!response.Success)
        {
            // Distinguish budget (429) from other errors (500)
            var statusCode = response.ErrorMessage?.Contains("budget") == true ||
                             response.ErrorMessage?.Contains("iterations") == true
                             ? 429 : 500;
            return Results.Problem(
                detail:     response.ErrorMessage,
                statusCode: statusCode,
                title:      "AgentError");
        }

        return Results.Ok(new
        {
            reply       = response.Reply,
            toolInvoked = response.ToolInvoked,
            toolResult  = response.ToolResult,
            sessionId   = response.SessionId,
            usage       = new
            {
                inputTokens      = response.Usage.InputTokens,
                outputTokens     = response.Usage.OutputTokens,
                totalTokens      = response.Usage.TotalTokens,
                estimatedCostUsd = response.Usage.EstimatedCostUsd
            }
        });
    }

    private static async Task AgentChatStream(
        [FromBody] AgentChatRequest body,
        HttpContext                 ctx,
        IMediator                  mediator,
        CancellationToken          ct)
    {
        ctx.Response.ContentType          = "text/event-stream";
        ctx.Response.Headers.CacheControl = "no-cache";
        ctx.Response.Headers.Connection   = "keep-alive";

        var (correlationId, tenantId, userId) = ExtractContext(ctx);
        var providerOverride = ctx.Request.Headers.TryGetValue("X-Llm-Provider", out var hdr)
            ? hdr.ToString() : null;

        var command = new AgentChatCommand(
            correlationId, tenantId, userId, body.Text, body.SessionId, providerOverride);

        // Run orchestration and emit SSE events
        await SseWriter.WriteEventAsync(ctx.Response, "status", "{ \"status\": \"processing\" }", ct);

        var response = await mediator.Send(command, ct);

        if (response.ToolInvoked is not null)
            await SseWriter.WriteEventAsync(ctx.Response, "tool_selected",
                JsonSerializer.Serialize(new { tool = response.ToolInvoked }), ct);

        if (response.ToolResult.HasValue)
            await SseWriter.WriteEventAsync(ctx.Response, "tool_result",
                JsonSerializer.Serialize(response.ToolResult.Value), ct);

        if (response.Success)
            await SseWriter.WriteEventAsync(ctx.Response, "reply",
                JsonSerializer.Serialize(new { text = response.Reply, sessionId = response.SessionId }), ct);
        else
            await SseWriter.WriteErrorAsync(ctx.Response,
                response.ErrorMessage ?? "Agent error", ct);
    }

    private static (Guid correlationId, string tenantId, string userId) ExtractContext(HttpContext ctx)
    {
        var correlationId = ctx.Request.Headers.TryGetValue("X-Correlation-Id", out var hdr)
                            && Guid.TryParse(hdr, out var parsed)
            ? parsed : Guid.NewGuid();
        var tenantId = ctx.User.FindFirst("tenant_id")?.Value ?? "anonymous";
        var userId   = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
        return (correlationId, tenantId, userId);
    }
}

/// <summary>Request body for /agent/chat endpoints.</summary>
public sealed record AgentChatRequest(string Text, string? SessionId = null);

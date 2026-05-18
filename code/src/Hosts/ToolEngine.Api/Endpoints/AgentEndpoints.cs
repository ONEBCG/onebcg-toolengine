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

    // Maximum characters accepted in a single agent request.
    // Limits prompt-injection surface and prevents runaway token consumption.
    private const int MaxTextLength = 4_000;

    // Provider names that may appear in X-Llm-Provider.
    // Only alphanumeric + hyphens accepted; unrecognised values rejected before routing.
    private static readonly System.Text.RegularExpressions.Regex _providerNameRx =
        new(@"^[a-zA-Z0-9\-]{1,50}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static async Task<IResult> AgentChat(
        [FromBody] AgentChatRequest body,
        HttpContext                 ctx,
        IMediator                  mediator,
        CancellationToken          ct)
    {
        // ── Input validation ──────────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(body.Text))
            return Results.BadRequest(new { error = "Text must not be empty." });

        if (body.Text.Length > MaxTextLength)
            return Results.BadRequest(new
            {
                error = $"Text exceeds maximum length of {MaxTextLength} characters."
            });

        // ── Provider override header — sanitise before routing ────────────────
        var providerOverride = ctx.Request.Headers.TryGetValue("X-Llm-Provider", out var hdr)
            ? hdr.ToString() : null;

        if (providerOverride is not null && !_providerNameRx.IsMatch(providerOverride))
            return Results.BadRequest(new
            {
                error = "X-Llm-Provider contains invalid characters. Use alphanumeric and hyphens only."
            });

        var (correlationId, tenantId, userId) = ExtractContext(ctx);

        var command = new AgentChatCommand(
            correlationId,
            tenantId,
            userId,
            body.Text,
            body.SessionId,
            LlmProviderOverride: providerOverride);

        var response = await mediator.Send(command, ct);

        // ── Pending approval (202) ────────────────────────────────────────────
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

        // ── Out-of-scope (200 — conversational boundary, not a system error) ──
        // IsOutOfScope = true means the pre-flight classifier determined the request
        // was outside the domain of available tools. Reply contains the refusal message.
        // Return 200 so clients can display it as a normal assistant turn, not an error.
        if (response.IsOutOfScope)
            return Results.Ok(new
            {
                reply        = response.Reply,
                isOutOfScope = true,
                sessionId    = response.SessionId,
                usage        = BuildUsage(response)
            });

        // ── System errors (429 / 500) ─────────────────────────────────────────
        if (!response.Success)
        {
            var statusCode = response.ErrorMessage?.Contains("budget",    StringComparison.OrdinalIgnoreCase) == true ||
                             response.ErrorMessage?.Contains("iterations", StringComparison.OrdinalIgnoreCase) == true
                             ? 429 : 500;
            return Results.Problem(
                detail:     response.ErrorMessage,
                statusCode: statusCode,
                title:      "AgentError");
        }

        // ── Success (200) ─────────────────────────────────────────────────────
        return Results.Ok(new
        {
            reply        = response.Reply,
            isOutOfScope = false,
            toolInvoked  = response.ToolInvoked,
            toolResult   = response.ToolResult,
            sessionId    = response.SessionId,
            usage        = BuildUsage(response)
        });
    }

    private static object BuildUsage(AgentChatResponse r) => new
    {
        inputTokens      = r.Usage.InputTokens,
        outputTokens     = r.Usage.OutputTokens,
        totalTokens      = r.Usage.TotalTokens,
        estimatedCostUsd = r.Usage.EstimatedCostUsd
    };

    private static async Task AgentChatStream(
        [FromBody] AgentChatRequest body,
        HttpContext                 ctx,
        IMediator                  mediator,
        CancellationToken          ct)
    {
        // ── Input validation (same rules as /chat) ────────────────────────────
        if (string.IsNullOrWhiteSpace(body.Text))
        {
            ctx.Response.StatusCode = 400;
            return;
        }

        if (body.Text.Length > MaxTextLength)
        {
            ctx.Response.StatusCode = 400;
            return;
        }

        var providerOverride = ctx.Request.Headers.TryGetValue("X-Llm-Provider", out var hdr)
            ? hdr.ToString() : null;

        if (providerOverride is not null && !_providerNameRx.IsMatch(providerOverride))
        {
            ctx.Response.StatusCode = 400;
            return;
        }

        // ── SSE headers ───────────────────────────────────────────────────────
        ctx.Response.ContentType          = "text/event-stream";
        ctx.Response.Headers.CacheControl = "no-cache";
        ctx.Response.Headers.Connection   = "keep-alive";

        var (correlationId, tenantId, userId) = ExtractContext(ctx);

        var command = new AgentChatCommand(
            correlationId, tenantId, userId, body.Text, body.SessionId, providerOverride);

        await SseWriter.WriteEventAsync(ctx.Response, "status",
            JsonSerializer.Serialize(new { status = "processing" }), ct);

        var response = await mediator.Send(command, ct);

        // Emit tool events regardless of final outcome — client may display progress
        if (response.ToolInvoked is not null)
            await SseWriter.WriteEventAsync(ctx.Response, "tool_selected",
                JsonSerializer.Serialize(new { tool = response.ToolInvoked }), ct);

        if (response.ToolResult.HasValue)
            await SseWriter.WriteEventAsync(ctx.Response, "tool_result",
                JsonSerializer.Serialize(response.ToolResult.Value), ct);

        // ── Terminal events — exactly one of the four below fires ─────────────

        if (response.IsOutOfScope)
        {
            // Scope boundary — not an error; client should display as assistant message
            await SseWriter.WriteEventAsync(ctx.Response, "out_of_scope",
                JsonSerializer.Serialize(new
                {
                    message   = response.Reply,
                    sessionId = response.SessionId,
                    usage     = BuildUsage(response)
                }), ct);
        }
        else if (response.PendingInvocationId.HasValue)
        {
            var pollUrl = $"/invocations/{response.PendingInvocationId}/status";
            await SseWriter.WriteEventAsync(ctx.Response, "pending_approval",
                JsonSerializer.Serialize(new
                {
                    invocationId = response.PendingInvocationId,
                    pollUrl,
                    sessionId    = response.SessionId
                }), ct);
        }
        else if (response.Success)
        {
            await SseWriter.WriteEventAsync(ctx.Response, "reply",
                JsonSerializer.Serialize(new
                {
                    text      = response.Reply,
                    sessionId = response.SessionId,
                    usage     = BuildUsage(response)
                }), ct);
        }
        else
        {
            await SseWriter.WriteErrorAsync(ctx.Response,
                response.ErrorMessage ?? "Agent error", ct);
        }
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

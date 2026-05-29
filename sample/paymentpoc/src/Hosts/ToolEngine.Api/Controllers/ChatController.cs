using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ToolEngine.Api.Services;
using ToolEngine.Core.Abstractions.Llm;

namespace ToolEngine.Api.Controllers;

[ApiController]
[Route("api/v1/chat")]
[Authorize]
[Tags("Chat")]
public sealed class ChatController : ControllerBase
{
    private readonly ChatService _chatService;

    public ChatController(ChatService chatService) => _chatService = chatService;

    /// <summary>
    /// Send a natural language message. Claude selects and executes payment tools autonomously.
    /// Requires ANTHROPIC_API_KEY environment variable or Claude:ApiKey in appsettings.json.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<ChatResponse>(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(502)]
    public async Task<IActionResult> Chat(
        [FromBody] ChatRequest req,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Message))
            return BadRequest(new { error = "Message is required." });

        var response = await _chatService.SendAsync(req.Message, ct);

        return response.IsSuccess
            ? Ok(response)
            : Problem(detail: response.Error, title: "Chat failed", statusCode: 502);
    }

    /// <summary>
    /// Streaming chat endpoint — returns Server-Sent Events (text/event-stream).
    /// Emits tool_started and tool_completed events in real time as the LLM agent
    /// executes tools, then emits a final "done" event with the full reply.
    ///
    /// Use the Fetch streaming API on the client (not EventSource — that requires GET).
    /// LLM:Streaming must be true in appsettings for intermediate events to be emitted;
    /// the "done" event is always sent regardless.
    /// </summary>
    [HttpPost("stream")]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    public async Task StreamChat(
        [FromBody] ChatRequest req,
        CancellationToken      ct)
    {
        if (string.IsNullOrWhiteSpace(req.Message))
        {
            Response.StatusCode = 400;
            return;
        }

        Response.ContentType              = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";   // prevent Nginx/proxy buffering

        // Helper that writes one SSE event and flushes immediately
        async Task SendEvent(string eventType, object data)
        {
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Converters           = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
            });
            await Response.WriteAsync($"event: {eventType}\ndata: {json}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }

        // Callback passed to ChatService.StreamAsync — forwards provider events as SSE
        async Task OnStream(LlmStreamEvent evt)
        {
            switch (evt)
            {
                case ToolStartedEvent e:
                    await SendEvent("tool_started", new
                    {
                        toolName = e.ToolName,
                        input    = e.Input,
                    });
                    break;

                case ToolCompletedEvent e:
                    await SendEvent("tool_completed", new
                    {
                        toolName   = e.ToolName,
                        outputJson = e.OutputJson,
                        success    = e.Success,
                        suspended  = e.Suspended,
                    });
                    break;
            }
        }

        // Run the agentic loop — provider calls OnStream for each tool event
        var result = await _chatService.StreamAsync(req.Message, OnStream, ct);

        // Final event — always emitted, carries the complete reply + tool log + token usage
        await SendEvent("done", new
        {
            reply        = result.Reply,
            toolCalls    = result.ToolCalls,
            isSuccess    = result.IsSuccess,
            error        = result.Error,
            inputTokens  = result.InputTokens,
            outputTokens = result.OutputTokens,
        });
    }
}

public sealed record ChatRequest(string Message);

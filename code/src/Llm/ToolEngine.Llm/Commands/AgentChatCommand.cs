namespace ToolEngine.Llm.Commands;

using MediatR;
using ToolEngine.Llm.Models;

public sealed record AgentChatCommand(
    Guid    CorrelationId,
    string  TenantId,
    string  UserId,
    string  Text,
    string? SessionId,
    string? LlmProviderOverride = null
) : IRequest<AgentChatResponse>;

public sealed record AgentChatResponse(
    bool                              Success,
    string?                           Reply,
    string?                           ToolInvoked,
    System.Text.Json.JsonElement?     ToolResult,
    string                            SessionId,
    LlmUsage                          Usage,
    string?                           ErrorMessage        = null,
    Guid?                             PendingInvocationId = null,
    /// <summary>
    /// <c>true</c> when the request was outside the scope of available tools.
    /// <see cref="Reply"/> contains the human-readable refusal.
    /// This is a conversational boundary, not a system error — callers should
    /// display <see cref="Reply"/> to the user and not treat this as a failure.
    /// </summary>
    bool                              IsOutOfScope        = false);

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
    Guid?                             PendingInvocationId = null);

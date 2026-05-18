namespace ToolEngine.Llm.Models;

using System.Text.Json;

public sealed class AgentResult
{
    public bool          Success             { get; private init; }
    public string?       Reply               { get; private init; }
    public string?       ToolInvoked         { get; private init; }   // original full name e.g. "math.calculate"
    public JsonElement?  ToolResult          { get; private init; }
    public string        SessionId           { get; private init; } = string.Empty;
    public LlmUsage      Usage               { get; private init; } = LlmUsage.Zero;
    public string?       ErrorMessage        { get; private init; }
    public Guid?         PendingInvocationId { get; private init; }

    public static AgentResult Ok(string reply, string sessionId, LlmUsage usage, string? toolInvoked = null, JsonElement? toolResult = null) =>
        new() { Success = true, Reply = reply, SessionId = sessionId, Usage = usage, ToolInvoked = toolInvoked, ToolResult = toolResult };

    public static AgentResult ToolPending(Guid pendingId, string sessionId, LlmUsage usage) =>
        new() { Success = false, PendingInvocationId = pendingId, SessionId = sessionId, Usage = usage, ErrorMessage = $"Tool execution pending approval. Poll /invocations/{pendingId}/status." };

    public static AgentResult BudgetExceeded(string sessionId, LlmUsage usage) =>
        new() { Success = false, ErrorMessage = "Session token budget exceeded.", SessionId = sessionId, Usage = usage };

    public static AgentResult MaxIterations(string sessionId, LlmUsage usage) =>
        new() { Success = false, ErrorMessage = "Maximum orchestration iterations reached.", SessionId = sessionId, Usage = usage };

    public static AgentResult Failure(string message, string sessionId, LlmUsage usage) =>
        new() { Success = false, ErrorMessage = message, SessionId = sessionId, Usage = usage };
}

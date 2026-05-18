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

    /// <summary>
    /// <c>true</c> when the LLM determined the user's request was entirely outside
    /// the domain of available tools. <see cref="Reply"/> contains the human-readable
    /// refusal message; <see cref="ErrorMessage"/> is <c>null</c> (this is not a
    /// system error — it is an expected conversational boundary).
    /// </summary>
    public bool          IsOutOfScope        { get; private init; }

    public static AgentResult Ok(string reply, string sessionId, LlmUsage usage, string? toolInvoked = null, JsonElement? toolResult = null) =>
        new() { Success = true, Reply = reply, SessionId = sessionId, Usage = usage, ToolInvoked = toolInvoked, ToolResult = toolResult };

    /// <summary>
    /// The request was fully outside the scope of available tools.
    /// <paramref name="refusalMessage"/> is the LLM-generated explanation of what
    /// was asked and what the agent can help with instead.
    /// No tool was invoked; no MediatR pipeline was touched.
    /// </summary>
    public static AgentResult OutOfScope(string refusalMessage, string sessionId, LlmUsage usage) =>
        new() { Success = false, IsOutOfScope = true, Reply = refusalMessage, SessionId = sessionId, Usage = usage };

    public static AgentResult ToolPending(Guid pendingId, string sessionId, LlmUsage usage) =>
        new() { Success = false, PendingInvocationId = pendingId, SessionId = sessionId, Usage = usage, ErrorMessage = $"Tool execution pending approval. Poll /invocations/{pendingId}/status." };

    public static AgentResult BudgetExceeded(string sessionId, LlmUsage usage) =>
        new() { Success = false, ErrorMessage = "Session token budget exceeded.", SessionId = sessionId, Usage = usage };

    public static AgentResult MaxIterations(string sessionId, LlmUsage usage) =>
        new() { Success = false, ErrorMessage = "Maximum orchestration iterations reached.", SessionId = sessionId, Usage = usage };

    public static AgentResult Failure(string message, string sessionId, LlmUsage usage) =>
        new() { Success = false, ErrorMessage = message, SessionId = sessionId, Usage = usage };
}

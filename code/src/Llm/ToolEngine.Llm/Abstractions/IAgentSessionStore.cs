namespace ToolEngine.Llm.Abstractions;

using ToolEngine.Llm.Session;

public interface IAgentSessionStore
{
    Task<AgentSession> GetOrCreateAsync(string? sessionId, bool isSingleTurn, CancellationToken ct);
    Task SaveAsync(AgentSession session, CancellationToken ct);
    Task DeleteAsync(string sessionId, CancellationToken ct);
}

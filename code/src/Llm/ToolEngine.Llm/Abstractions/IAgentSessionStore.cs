namespace ToolEngine.Llm.Abstractions;

using ToolEngine.Llm.Session;

public interface IAgentSessionStore
{
    Task<AgentSession> GetOrCreateAsync(string? sessionId, bool isSingleTurn, CancellationToken ct);
    Task SaveAsync(AgentSession session, CancellationToken ct);
    Task DeleteAsync(string sessionId, CancellationToken ct);

    /// <summary>
    /// H15 — Acquires a per-session exclusive lock to prevent concurrent requests from
    /// overwriting each other's message history (last-writer-wins data loss).
    /// Dispose the returned handle to release the lock.
    /// Single-turn sessions pass a freshly generated ID and never collide, so this
    /// is a no-op for them in practice.
    /// </summary>
    Task<IDisposable> AcquireSessionLockAsync(string sessionId, CancellationToken ct);
}

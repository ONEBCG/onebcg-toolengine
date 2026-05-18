namespace ToolEngine.Llm.Session;

using System.Text.Json;
using Microsoft.Extensions.Logging;
using ToolEngine.Core.Abstractions.Common;
using ToolEngine.Llm.Abstractions;
using ToolEngine.Llm.Models;

public sealed class AgentSessionStore : IAgentSessionStore
{
    private static readonly TimeSpan SingleTurnTtl = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan MultiTurnTtl  = TimeSpan.FromHours(24);

    private readonly ICacheProvider             _cache;
    private readonly ILogger<AgentSessionStore> _logger;

    public AgentSessionStore(ICacheProvider cache, ILogger<AgentSessionStore> logger)
    {
        _cache  = cache;
        _logger = logger;
    }

    public async Task<AgentSession> GetOrCreateAsync(string? sessionId, bool isSingleTurn, CancellationToken ct)
    {
        var id = sessionId ?? Guid.NewGuid().ToString();

        if (sessionId is not null)
        {
            var existing = await _cache.GetStringAsync(CacheKey(sessionId), ct);
            if (existing is not null)
            {
                var session = DeserializeSession(id, isSingleTurn, existing);
                if (session is not null)
                    return session;
            }
        }

        return new AgentSession { SessionId = id, IsSingleTurn = isSingleTurn };
    }

    public Task SaveAsync(AgentSession session, CancellationToken ct)
    {
        var ttl  = session.IsSingleTurn ? SingleTurnTtl : MultiTurnTtl;
        var json = SerializeSession(session);
        return _cache.SetStringAsync(CacheKey(session.SessionId), json, ttl, ct);
    }

    public Task DeleteAsync(string sessionId, CancellationToken ct) =>
        _cache.RemoveAsync(CacheKey(sessionId), ct);

    private static string CacheKey(string sessionId) => $"agent-session:{sessionId}";

    private static string SerializeSession(AgentSession session)
    {
        var dto = new SessionDto(
            session.SessionId,
            session.IsSingleTurn,
            session.Messages.Select(m => new MessageDto(
                m.Role.ToString(),
                m.Content,
                m.ToolCall is null ? null : new ToolCallDto(m.ToolCall.Id, m.ToolCall.ToolName, m.ToolCall.Arguments.GetRawText()),
                m.ToolCallId)).ToList(),
            session.TotalUsage.InputTokens,
            session.TotalUsage.OutputTokens,
            session.TotalUsage.EstimatedCostUsd);
        return JsonSerializer.Serialize(dto);
    }

    private static AgentSession? DeserializeSession(string id, bool isSingleTurn, string json)
    {
        try
        {
            var dto = JsonSerializer.Deserialize<SessionDto>(json);
            if (dto is null) return null;

            var session = new AgentSession { SessionId = id, IsSingleTurn = isSingleTurn };
            foreach (var m in dto.Messages)
            {
                if (!System.Enum.TryParse<MessageRole>(m.Role, out var role)) continue;

                LlmMessage msg;
                if (m.ToolCall is not null)
                {
                    var args = JsonDocument.Parse(m.ToolCall.ArgumentsJson).RootElement;
                    msg = LlmMessage.AssistantToolUse(new LlmToolCall(m.ToolCall.Id, m.ToolCall.ToolName, args));
                }
                else if (role == MessageRole.Tool && m.ToolCallId is not null)
                {
                    msg = LlmMessage.ToolResult(m.ToolCallId, m.Content ?? string.Empty);
                }
                else
                {
                    msg = new LlmMessage { Role = role, Content = m.Content };
                }

                session.AddMessage(msg);
            }

            session.RecordUsage(new LlmUsage(dto.InputTokens, dto.OutputTokens, dto.EstimatedCostUsd));
            return session;
        }
        catch (Exception ex)
        {
            // Swallow deserialization errors — a new session will be created
            _ = ex;
            return null;
        }
    }

    // DTOs for serialization
    private sealed record SessionDto(
        string          SessionId,
        bool            IsSingleTurn,
        List<MessageDto> Messages,
        int             InputTokens,
        int             OutputTokens,
        decimal         EstimatedCostUsd);

    private sealed record MessageDto(
        string       Role,
        string?      Content,
        ToolCallDto? ToolCall,
        string?      ToolCallId);

    private sealed record ToolCallDto(
        string Id,
        string ToolName,
        string ArgumentsJson);
}

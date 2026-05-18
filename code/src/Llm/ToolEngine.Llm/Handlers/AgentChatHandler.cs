namespace ToolEngine.Llm.Handlers;

using MediatR;
using ToolEngine.Llm.Commands;
using ToolEngine.Llm.Session;

public sealed class AgentChatHandler : IRequestHandler<AgentChatCommand, AgentChatResponse>
{
    private readonly AgentOrchestrator _orchestrator;

    public AgentChatHandler(AgentOrchestrator orchestrator) => _orchestrator = orchestrator;

    public async Task<AgentChatResponse> Handle(AgentChatCommand cmd, CancellationToken ct)
    {
        var result = await _orchestrator.RunAsync(
            cmd.CorrelationId,
            cmd.TenantId,
            cmd.UserId,
            cmd.Text,
            cmd.SessionId,
            cmd.LlmProviderOverride,
            ct);

        return new AgentChatResponse(
            result.Success,
            result.Reply,
            result.ToolInvoked,
            result.ToolResult,
            result.SessionId,
            result.Usage,
            result.ErrorMessage,
            result.PendingInvocationId);
    }
}

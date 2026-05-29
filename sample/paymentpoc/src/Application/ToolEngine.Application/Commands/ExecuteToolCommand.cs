using System.Text.Json;
using MediatR;
using ToolEngine.Application.Abstractions;
using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Core.Domain.Enums;
using ToolEngine.Tools.Abstractions.Interfaces;

namespace ToolEngine.Application.Commands;

// ── ExecuteToolCommand ────────────────────────────────────────────────────────

public sealed record ExecuteToolCommand(
    Guid        CorrelationId,
    string?     ToolNamespace,
    string      ToolName,
    string      ToolVersion,
    JsonElement Input,
    string?     UserId,
    CallerType  CallerType             = CallerType.Human,
    string?     GovernanceMetadataJson = null,
    string?     IdempotencyKey         = null,
    int         MaxResponseTokens      = 4096)
    : IRequest<IToolResponse>, IExecuteToolCommand
{
    public string FullName =>
        string.IsNullOrEmpty(ToolNamespace) ? ToolName : $"{ToolNamespace}.{ToolName}";
}

// ── Handler ───────────────────────────────────────────────────────────────────

public sealed class ExecuteToolHandler : IRequestHandler<ExecuteToolCommand, IToolResponse>
{
    private readonly IToolExecutor _executor;

    public ExecuteToolHandler(IToolExecutor executor) => _executor = executor;

    public async Task<IToolResponse> Handle(
        ExecuteToolCommand command, CancellationToken cancellationToken)
    {
        var request = new ToolRequest<JsonElement>(
            CorrelationId:  command.CorrelationId,
            ToolName:       command.ToolName,
            ToolVersion:    command.ToolVersion,
            Input:          command.Input,
            ToolNamespace:  command.ToolNamespace,
            MaxResponseTokens: command.MaxResponseTokens);

        return await _executor.ExecuteAsync<JsonElement, JsonElement>(request, cancellationToken);
    }
}

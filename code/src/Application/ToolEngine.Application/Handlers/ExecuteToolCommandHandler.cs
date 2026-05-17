namespace ToolEngine.Application.Handlers;

using MediatR;
using ToolEngine.Application.Commands;
using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Core.Domain.Enums;
using ToolEngine.Tools.Abstractions.Base;

public sealed class ExecuteToolCommandHandler<TInput, TOutput>
    : IRequestHandler<ExecuteToolCommand<TInput, TOutput>, ToolResponse<TOutput>>
{
    private readonly IToolExecutor _executor;

    public ExecuteToolCommandHandler(IToolExecutor executor) =>
        _executor = executor;

    public Task<ToolResponse<TOutput>> Handle(
        ExecuteToolCommand<TInput, TOutput> command,
        CancellationToken ct) =>
        _executor.ExecuteAsync<TInput, TOutput>(
            new ToolRequest<TInput>(
                command.CorrelationId,
                command.TenantId,
                command.ToolName,
                command.ToolVersion,
                command.Input,
                command.Mode,
                UserId:        command.UserId,
                ToolNamespace: command.ToolNamespace),
            ct);
}

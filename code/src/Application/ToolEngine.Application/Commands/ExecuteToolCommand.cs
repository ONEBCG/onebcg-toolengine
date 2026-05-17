namespace ToolEngine.Application.Commands;

using MediatR;
using ToolEngine.Application.Abstractions;
using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Core.Domain.Enums;

/// <summary>
/// CQRS command for a single tool invocation.
/// CorrelationId must be provided by the caller — never generated here.
/// ToolNamespace + ToolName together form the FullName used for registry lookup.
/// </summary>
public sealed record ExecuteToolCommand<TInput, TOutput>(
    Guid          CorrelationId,
    string        TenantId,
    string        UserId,
    string        ToolName,
    string        ToolVersion,
    TInput        Input,
    ToolType      ToolType,
    ExecutionMode Mode               = ExecutionMode.Sequential,
    string        ToolNamespace      = "",
    int           MaxResponseTokens  = 25_000)
    : IRequest<ToolResponse<TOutput>>, IExecuteToolCommand;

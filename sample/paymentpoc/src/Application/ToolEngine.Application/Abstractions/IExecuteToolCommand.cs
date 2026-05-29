using ToolEngine.Core.Domain.Enums;

namespace ToolEngine.Application.Abstractions;

// Shared interface implemented by ExecuteToolCommand.
// All pipeline behaviors guard on this interface — ensures behaviors only fire
// for tool-execution requests, not other MediatR commands (ProcessPaymentCommand, etc.).
public interface IExecuteToolCommand
{
    Guid        CorrelationId          { get; }
    string?     UserId                 { get; }
    string      ToolName               { get; }
    string      ToolVersion            { get; }
    string?     ToolNamespace          { get; }
    string      FullName               => string.IsNullOrEmpty(ToolNamespace) ? ToolName : $"{ToolNamespace}.{ToolName}";
    int         MaxResponseTokens      { get; }
    CallerType  CallerType             { get; }   // H4
    string?     GovernanceMetadataJson { get; }   // H5
    string?     IdempotencyKey         { get; }   // F8
}

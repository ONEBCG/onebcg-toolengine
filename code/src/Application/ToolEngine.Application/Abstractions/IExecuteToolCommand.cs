namespace ToolEngine.Application.Abstractions;

using ToolEngine.Core.Domain.Enums;

/// <summary>
/// Non-generic marker interface. Implemented by ExecuteToolCommand&lt;TIn, TOut&gt;.
/// Allows AuditBehavior and ValidationBehavior to inspect command properties
/// without generic type constraints.
/// </summary>
public interface IExecuteToolCommand
{
    Guid     CorrelationId { get; }
    string   TenantId     { get; }
    string   UserId       { get; }
    string   ToolNamespace { get; }
    string   ToolName     { get; }
    string   ToolVersion  { get; }
    ToolType ToolType     { get; }
}

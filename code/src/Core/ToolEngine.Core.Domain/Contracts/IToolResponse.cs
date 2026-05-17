namespace ToolEngine.Core.Domain.Contracts;

/// <summary>
/// Non-generic view of a tool response. Implemented by ToolResponse&lt;TOutput&gt;.
/// Allows AuditBehavior to inspect outcomes without generic type constraints.
/// </summary>
public interface IToolResponse
{
    Guid             CorrelationId       { get; }
    bool             Success             { get; }
    ToolError?       Error               { get; }
    ToolUsageMetrics Metrics             { get; }
    DateTimeOffset   Timestamp           { get; }
    /// <summary>Set when execution is suspended awaiting human approval.</summary>
    Guid?            PendingInvocationId { get; }
}

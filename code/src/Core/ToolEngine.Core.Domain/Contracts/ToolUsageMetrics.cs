namespace ToolEngine.Core.Domain.Contracts;

/// <summary>Resource consumption recorded at the end of every tool invocation.</summary>
public sealed record ToolUsageMetrics(
    TimeSpan Duration,
    int      RetryCount = 0,
    int?     TokensIn   = null,
    int?     TokensOut  = null,
    string?  ModelUsed  = null)
{
    public static ToolUsageMetrics Empty => new(TimeSpan.Zero);
}

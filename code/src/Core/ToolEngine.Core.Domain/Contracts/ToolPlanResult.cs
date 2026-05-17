namespace ToolEngine.Core.Domain.Contracts;

/// <summary>
/// Result of a multi-step ToolPlan execution (Sequential, Parallel, or DAG).
/// Each step result is keyed by StepId.
/// TotalMetrics.Duration = wall-clock time, not sum of step durations.
/// </summary>
public sealed record ToolPlanResult(
    Guid                                          CorrelationId,
    bool                                          Success,
    IReadOnlyDictionary<string, ToolStepResult>   StepResults,
    ToolUsageMetrics                              TotalMetrics,
    DateTimeOffset                                CompletedAt);

public sealed record ToolStepResult(
    string         StepId,
    string         ToolFullName,
    bool           Success,
    object?        Output,
    ToolError?     Error,
    TimeSpan       Duration,
    bool           Skipped = false);

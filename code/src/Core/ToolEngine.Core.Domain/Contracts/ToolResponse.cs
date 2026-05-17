namespace ToolEngine.Core.Domain.Contracts;

/// <summary>
/// The single exit contract for every tool invocation — both streaming and non-streaming.
/// For streaming invocations this wraps the final summary after all chunks are emitted.
/// IsTruncated and NextPageToken support pagination for large results.
/// </summary>
public sealed record ToolResponse<TOutput>(
    Guid             CorrelationId,
    bool             Success,
    TOutput?         Data,
    ToolError?       Error,
    ToolUsageMetrics Metrics,
    DateTimeOffset   Timestamp,
    // True when the response was truncated to fit MaxResponseTokens.
    bool             IsTruncated   = false,
    // Opaque token the agent passes in the next request to get the next page.
    string?          NextPageToken = null) : IToolResponse
{
    public static ToolResponse<TOutput> Ok(
        Guid correlationId,
        TOutput data,
        ToolUsageMetrics? metrics      = null,
        bool              isTruncated  = false,
        string?           nextPageToken = null) =>
        new(correlationId,
            true,
            data,
            null,
            metrics ?? ToolUsageMetrics.Empty,
            DateTimeOffset.UtcNow,
            isTruncated,
            nextPageToken);

    public static ToolResponse<TOutput> Fail(
        Guid correlationId,
        ToolError error,
        ToolUsageMetrics? metrics = null) =>
        new(correlationId,
            false,
            default,
            error,
            metrics ?? ToolUsageMetrics.Empty,
            DateTimeOffset.UtcNow);
}

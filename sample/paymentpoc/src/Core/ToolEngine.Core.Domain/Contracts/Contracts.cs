using ToolEngine.Core.Domain.Enums;

namespace ToolEngine.Core.Domain.Contracts;

// ── ToolRequest ──────────────────────────────────────────────────────────────

public sealed record ToolRequest<TInput>(
    Guid                      CorrelationId,
    string                    ToolName,
    string                    ToolVersion,
    TInput                    Input,
    ExecutionMode             Mode              = ExecutionMode.Sequential,
    bool                      Streaming         = false,
    string?                   UserId            = null,
    Dictionary<string,string>?Metadata          = null,
    int                       MaxResponseTokens = 4096,
    string?                   ResponseFormat    = null,
    string?                   ToolNamespace     = null)
{
    public string FullName =>
        string.IsNullOrEmpty(ToolNamespace) ? ToolName : $"{ToolNamespace}.{ToolName}";
}

// ── ToolError ────────────────────────────────────────────────────────────────

public sealed record ToolError(int HttpStatusCode, string ErrorCode, string Description)
{
    public static ToolError FromError(Core.Domain.Common.Error e, int httpStatus) =>
        new(httpStatus, e.Code, e.Description);

    public static ToolError NotFound(string description)   => new(404, "NOT_FOUND",       description);
    public static ToolError Unauthorized(string description)=> new(401, "UNAUTHORIZED",    description);
    public static ToolError Forbidden(string description)  => new(403, "FORBIDDEN",        description);
    public static ToolError Validation(string description) => new(400, "VALIDATION_ERROR", description);
    public static ToolError Internal(string description)   => new(500, "INTERNAL_ERROR",   description);
    public static ToolError ApprovalPending(Guid id)       => new(202, "APPROVAL_PENDING", id.ToString());
}

// ── ToolUsageMetrics ─────────────────────────────────────────────────────────

public sealed record ToolUsageMetrics(long DurationMs, int TokensUsed);

// ── IToolResponse (non-generic — required by MediatR behaviors) ──────────────

public interface IToolResponse
{
    bool       Success           { get; }
    ToolError? Error             { get; }
    bool       IsSuspended       => Error?.ErrorCode == "APPROVAL_PENDING";
    Guid?      PendingInvocationId =>
        IsSuspended && Guid.TryParse(Error!.Description, out var id) ? id : null;
}

// ── ToolResponse<TOutput> ────────────────────────────────────────────────────

public sealed record ToolResponse<TOutput>(
    Guid             CorrelationId,
    bool             Success,
    TOutput?         Data,
    ToolError?       Error,
    ToolUsageMetrics Metrics,
    DateTimeOffset   Timestamp) : IToolResponse
{
    public static ToolResponse<TOutput> Ok(
        Guid correlationId, TOutput data, ToolUsageMetrics? metrics = null) =>
        new(correlationId, true, data, null,
            metrics ?? new ToolUsageMetrics(0, 0), DateTimeOffset.UtcNow);

    public static ToolResponse<TOutput> Fail(
        Guid correlationId, ToolError error) =>
        new(correlationId, false, default, error,
            new ToolUsageMetrics(0, 0), DateTimeOffset.UtcNow);

    public static ToolResponse<TOutput> Suspended(
        Guid correlationId, Guid pendingInvocationId) =>
        new(correlationId, false, default,
            ToolError.ApprovalPending(pendingInvocationId),
            new ToolUsageMetrics(0, 0), DateTimeOffset.UtcNow);
}

// ── AcknowledgementStatement — EU AI Act Article 14 §4 (H3) ─────────────────

public sealed record AcknowledgementStatement(
    string         RegBasis,           // "EU AI Act Article 14 §4"
    string         RiskLevel,          // "High" | "Critical"
    string         ToolFullName,
    string         OperatorStatement,
    DateTimeOffset IssuedAt);

// ── ToolChunk — streaming ────────────────────────────────────────────────────

public sealed record ToolChunk(Guid CorrelationId, string Content, int Index, bool IsFinal);

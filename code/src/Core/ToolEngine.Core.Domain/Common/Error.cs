namespace ToolEngine.Core.Domain.Common;

using ToolEngine.Core.Domain.Constants;

/// <summary>
/// Structured domain error. Code is always a constant from <see cref="ErrorCodes"/>;
/// Description is a human-readable message safe to include in API responses.
///
/// <para>
/// Railway-oriented: <see cref="Result{T}"/> wraps either a value or one of these errors,
/// eliminating throw/catch for expected failures in the tool pipeline.
/// </para>
/// </summary>
public sealed record Error(string Code, string Description)
{
    public static readonly Error None    = new(string.Empty, string.Empty);
    public static readonly Error Unknown = new(ErrorCodes.Unknown, "An unexpected error occurred.");

    public static Error NotFound(string resource, object id) =>
        new(ErrorCodes.NotFound, $"{resource} with id '{id}' was not found.");

    public static Error Validation(string message) =>
        new(ErrorCodes.Validation, message);

    public static Error Unauthorized(string message = "Unauthorized access.") =>
        new(ErrorCodes.Unauthorized, message);

    public static Error Conflict(string message) =>
        new(ErrorCodes.Conflict, message);

    public static Error ToolNotFound(string name, string version) =>
        new(ErrorCodes.ToolNotFound, $"Tool '{name}@{version}' is not registered.");

    public static Error ToolNotFound(string ns, string name, string version) =>
        new(ErrorCodes.ToolNotFound,
            $"Tool '{ns}.{name}@{version}' is not registered for this tenant.");

    public static Error TenantNotAllowed(string tenantId, string toolFullName) =>
        new(ErrorCodes.TenantNotAllowed,
            $"Tenant '{tenantId}' does not have access to tool '{toolFullName}'.");

    public static Error LoopDetected(string toolName, int callCount) =>
        new(ErrorCodes.AgentLoopDetected,
            $"Tool '{toolName}' called {callCount} times in this session. " +
            "Circuit open — agent loop suspected.");

    public static Error ApprovalDenied(string toolName) =>
        new(ErrorCodes.ApprovalDenied,
            $"Human approval was denied for tool '{toolName}'.");

    public static Error TokenBudgetExceeded(int estimated, int limit) =>
        new(ErrorCodes.TokenBudgetExceeded,
            $"Response estimated at {estimated} tokens exceeds limit of {limit}.");

    public static Error SecretExpired(string secretName) =>
        new(ErrorCodes.SecretExpired,
            $"Credential '{secretName}' has expired. Re-invoke the tool.");

    /// <summary>
    /// Returned when a tool is suspended awaiting human approval.
    /// InvocationId is the PendingApproval.Id — poll GET /invocations/{id}/status.
    /// </summary>
    public static Error ApprovalPending(Guid invocationId) =>
        new(ErrorCodes.ApprovalPending,
            $"Tool execution is suspended pending human approval. " +
            $"Poll GET /invocations/{invocationId}/status for result.");

    public static Error ApprovalExpired(Guid invocationId) =>
        new(ErrorCodes.ApprovalExpired,
            $"Approval request {invocationId} expired without a decision.");

    public static Error InvalidOtp() =>
        new(ErrorCodes.InvalidOtp, "The OTP provided is invalid or has expired.");

    public static Error InvalidApprovalToken() =>
        new(ErrorCodes.InvalidApprovalToken, "The approval token is invalid or has already been used.");
}

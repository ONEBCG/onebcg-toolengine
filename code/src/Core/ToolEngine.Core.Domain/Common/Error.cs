namespace ToolEngine.Core.Domain.Common;

/// <summary>
/// Structured error. Code is a SCREAMING_SNAKE_CASE constant string, e.g. "TOOL_NOT_FOUND".
/// </summary>
public sealed record Error(string Code, string Description)
{
    public static readonly Error None    = new(string.Empty, string.Empty);
    public static readonly Error Unknown = new("UNKNOWN_ERROR", "An unexpected error occurred.");

    public static Error NotFound(string resource, object id) =>
        new("NOT_FOUND", $"{resource} with id '{id}' was not found.");

    public static Error Validation(string message) =>
        new("VALIDATION_ERROR", message);

    public static Error Unauthorized(string message = "Unauthorized access.") =>
        new("UNAUTHORIZED", message);

    public static Error Conflict(string message) =>
        new("CONFLICT", message);

    public static Error ToolNotFound(string name, string version) =>
        new("TOOL_NOT_FOUND", $"Tool '{name}@{version}' is not registered.");

    public static Error ToolNotFound(string ns, string name, string version) =>
        new("TOOL_NOT_FOUND",
            $"Tool '{ns}.{name}@{version}' is not registered for this tenant.");

    public static Error TenantNotAllowed(string tenantId, string toolFullName) =>
        new("TENANT_NOT_ALLOWED",
            $"Tenant '{tenantId}' does not have access to tool '{toolFullName}'.");

    public static Error LoopDetected(string toolName, int callCount) =>
        new("AGENT_LOOP_DETECTED",
            $"Tool '{toolName}' called {callCount} times in this session. " +
            $"Circuit open — agent loop suspected.");

    public static Error ApprovalDenied(string toolName) =>
        new("APPROVAL_DENIED",
            $"Human approval was denied for tool '{toolName}'.");

    public static Error TokenBudgetExceeded(int estimated, int limit) =>
        new("TOKEN_BUDGET_EXCEEDED",
            $"Response estimated at {estimated} tokens exceeds limit of {limit}.");

    public static Error SecretExpired(string secretName) =>
        new("SECRET_EXPIRED",
            $"Credential '{secretName}' has expired. Re-invoke the tool.");

    // Returned when a tool is suspended awaiting human approval.
    // InvocationId is the PendingApproval.Id — poll GET /invocations/{id}/status.
    public static Error ApprovalPending(Guid invocationId) =>
        new("APPROVAL_PENDING",
            $"Tool execution is suspended pending human approval. " +
            $"Poll GET /invocations/{invocationId}/status for result.");

    public static Error ApprovalExpired(Guid invocationId) =>
        new("APPROVAL_EXPIRED",
            $"Approval request {invocationId} expired without a decision.");

    public static Error InvalidOtp() =>
        new("INVALID_OTP", "The OTP provided is invalid or has expired.");

    public static Error InvalidApprovalToken() =>
        new("INVALID_APPROVAL_TOKEN", "The approval token is invalid or has already been used.");
}

namespace ToolEngine.Core.Domain.Constants;

/// <summary>
/// Canonical SCREAMING_SNAKE_CASE error code strings used throughout the pipeline.
///
/// Centralising these prevents silent divergence between the code that creates an error
/// and the code that matches on it (e.g. API error-response mapping, client SDKs, tests).
/// Every Error factory method in <see cref="Common.Error"/> references these constants.
/// </summary>
public static class ErrorCodes
{
    // ── Generic ───────────────────────────────────────────────────────────────

    /// <summary>Unclassified failure with no specific code.</summary>
    public const string Unknown = "UNKNOWN_ERROR";

    /// <summary>Requested resource does not exist.</summary>
    public const string NotFound = "NOT_FOUND";

    /// <summary>Request payload failed validation rules.</summary>
    public const string Validation = "VALIDATION_ERROR";

    /// <summary>Caller lacks permission for the requested operation.</summary>
    public const string Unauthorized = "UNAUTHORIZED";

    /// <summary>Requested operation conflicts with current resource state.</summary>
    public const string Conflict = "CONFLICT";

    // ── Tool execution ────────────────────────────────────────────────────────

    /// <summary>No tool matching the requested namespace/name/version is registered.</summary>
    public const string ToolNotFound = "TOOL_NOT_FOUND";

    /// <summary>Tenant's namespace allowlist does not include the requested tool.</summary>
    public const string TenantNotAllowed = "TENANT_NOT_ALLOWED";

    /// <summary>
    /// The same tool was invoked too many times within a single correlation context.
    /// Indicates an agent loop — circuit opened to prevent runaway cost.
    /// </summary>
    public const string AgentLoopDetected = "AGENT_LOOP_DETECTED";

    /// <summary>Human approval was explicitly denied for the tool invocation.</summary>
    public const string ApprovalDenied = "APPROVAL_DENIED";

    /// <summary>
    /// Estimated response token count exceeds the per-request cap configured for the tenant.
    /// </summary>
    public const string TokenBudgetExceeded = "TOKEN_BUDGET_EXCEEDED";

    /// <summary>
    /// Tenant has consumed its full daily tool-call quota (Tenant.DailyToolCallBudget).
    /// Budget resets at midnight UTC.
    /// </summary>
    public const string DailyBudgetExceeded = "DAILY_BUDGET_EXCEEDED";

    // ── Approval lifecycle ────────────────────────────────────────────────────

    /// <summary>
    /// Tool execution is suspended awaiting an out-of-band human decision.
    /// Poll GET /invocations/{id}/status to check for resolution.
    /// </summary>
    public const string ApprovalPending = "APPROVAL_PENDING";

    /// <summary>The approval request window elapsed without a decision.</summary>
    public const string ApprovalExpired = "APPROVAL_EXPIRED";

    // ── OTP / token verification ──────────────────────────────────────────────

    /// <summary>The supplied OTP is incorrect or its PBKDF2 hash has expired.</summary>
    public const string InvalidOtp = "INVALID_OTP";

    /// <summary>The magic-link approval token is not recognised or has already been consumed.</summary>
    public const string InvalidApprovalToken = "INVALID_APPROVAL_TOKEN";

    // ── Infrastructure ────────────────────────────────────────────────────────

    /// <summary>
    /// The credential (API key, secret) referenced by the tool has passed its expiry date.
    /// Re-invoke after refreshing the secret in the vault.
    /// </summary>
    public const string SecretExpired = "SECRET_EXPIRED";

    /// <summary>
    /// An unhandled exception was caught and recorded as an audit event.
    /// The original exception message is stored in the event's errorMessage field.
    /// </summary>
    public const string Exception = "EXCEPTION";

    /// <summary>
    /// Post-selection ToolGuard blocked the tool returned by the LLM.
    /// Indicates a possible prompt-injection attempt where the model was tricked
    /// into selecting a tool it was not shown in the schema.
    /// </summary>
    public const string ToolGuardBlocked = "TOOL_GUARD_BLOCKED";
}

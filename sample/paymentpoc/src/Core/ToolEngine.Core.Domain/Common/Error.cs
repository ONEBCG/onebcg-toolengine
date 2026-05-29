namespace ToolEngine.Core.Domain.Common;

public sealed record Error(string Code, string Description)
{
    public static Error NotFound(string entity, string id) =>
        new("NOT_FOUND", $"{entity} '{id}' was not found.");

    public static Error Validation(string description) =>
        new("VALIDATION_ERROR", description);

    public static Error Unauthorized(string description) =>
        new("UNAUTHORIZED", description);

    public static Error Conflict(string description) =>
        new("CONFLICT", description);

    public static Error Internal(string description) =>
        new("INTERNAL_ERROR", description);

    public static Error ApprovalPending(Guid invocationId) =>
        new("APPROVAL_PENDING", invocationId.ToString());

    public static Error InvalidOtp() =>
        new("INVALID_OTP", "The OTP provided is incorrect.");

    public static Error InvalidApprovalToken() =>
        new("INVALID_APPROVAL_TOKEN", "The approval token is invalid or expired.");
}

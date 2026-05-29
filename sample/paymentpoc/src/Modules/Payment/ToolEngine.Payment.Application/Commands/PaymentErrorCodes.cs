namespace ToolEngine.Payment.Application.Commands;

/// <summary>Error codes returned by payment pipeline stages.</summary>
internal static class PaymentErrorCodes
{
    public const string NotFound           = "NOT_FOUND";
    public const string NotSubmitted       = "NOT_SUBMITTED";
    public const string ApprovalPending    = "APPROVAL_PENDING";
    public const string ExecutionFailed    = "EXECUTION_FAILED";
    public const string ReconcileFailed    = "RECONCILIATION_FAILED";
    public const string NullApprovalId     = "NULL_APPROVAL_ID";
    public const string InstructionMissing = "INSTRUCTION_MISSING";
}

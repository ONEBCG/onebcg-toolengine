namespace ToolEngine.Payment.Domain.Enums;

public enum PaymentStatus
{
    Initiated,
    PayeeVerified,
    PpmChecked,
    WhtCalculated,
    KycScreened,
    PendingApproval,
    ApprovalGranted,
    SubmittedToBank,
    Settled,

    // Exception states
    BlockedUnknownPayee,
    BlockedInactivePayee,
    BlockedContract,
    BlockedKyc,
    BlockedRejected,
    HeldTaxReview,
    HeldComplianceReview,
    HeldReconciliation,
    FailedExecution,
    ExceptionQueue,
}

public enum PayeeStatus
{
    Active,
    Inactive,
    Suspended,
    PendingReview,
}

public enum WhtConfidenceLevel
{
    High,
    Medium,
    ReviewRequired,
}

public enum KycMatchResult
{
    NoMatch,          // 0.00 – 0.49 — CLEAR
    PotentialMatch,   // 0.50 – 0.79 — hold for compliance review
    ConfirmedMatch,   // 0.80 – 1.00 — block immediately
}

public enum PaymentRail
{
    Swift,
    Sepa,
    Ach,
    Neft,
    Rtgs,
    FasterPayments,
}

public enum ServiceType
{
    SoftwareLicense,   // Royalty — higher WHT
    CloudSaas,         // Business income / FTS — lower/nil
    ManagementConsulting,  // FTS — moderate WHT
    InterestOnLoan,    // Interest
    DividendDistribution,  // Dividend
    ContractStaffing,  // Employment / business income
    Other,
}

public enum EntityType
{
    Corporate,
    Individual,
    Government,
    Partnership,
}

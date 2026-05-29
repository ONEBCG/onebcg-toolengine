using ToolEngine.Core.Domain.Common;
using ToolEngine.Payment.Domain.Enums;

namespace ToolEngine.Payment.Domain.Entities;

// Aggregate root for a single payment instruction.
// Id = PRID (Payment Reference ID) assigned at Stage 0.
// Immutable post-bank-submission: any amendment requires a new instruction (Stage 6 safeguard).
public sealed class PaymentInstruction : AggregateRoot<Guid>
{
    // ── Payer ─────────────────────────────────────────────────────────────────
    public string  PayerName         { get; private set; } = default!;
    public string  PayerJurisdiction { get; private set; } = default!;
    public string  PayerEntityId     { get; private set; } = default!;

    // ── Payee (populated at initiation; detail attached after Stage 1) ────────
    public string  PayeeRef          { get; private set; } = default!;   // external ref provided by initiator
    public Guid?   VerifiedPayeeId   { get; private set; }               // FK set after Stage 1

    // ── Instruction ───────────────────────────────────────────────────────────
    public decimal     GrossAmount   { get; private set; }
    public string      Currency      { get; private set; } = default!;
    public ServiceType ServiceType   { get; private set; }
    public string      PpmId         { get; private set; } = default!;
    public string      InitiatorId   { get; private set; } = default!;
    public DateTimeOffset InitiatedAt { get; private set; }

    // ── Pipeline status ───────────────────────────────────────────────────────
    public PaymentStatus Status       { get; private set; }
    public int           CurrentStage { get; private set; }

    // ── Stage 3: WHT ─────────────────────────────────────────────────────────
    public decimal?          WhtRate            { get; private set; }
    public decimal?          WhtAmount          { get; private set; }
    public decimal?          NetPayableAmount   { get; private set; }
    public WhtConfidenceLevel? WhtConfidence    { get; private set; }
    public string?           WhtJustification   { get; private set; }
    public string?           ApplicableTreaty   { get; private set; }
    public string?           ServiceClassification { get; private set; }

    // ── Stage 4: KYC ─────────────────────────────────────────────────────────
    public KycMatchResult?   KycResult          { get; private set; }
    public decimal?          KycMatchScore      { get; private set; }
    public string?           KycScreeningRef    { get; private set; }

    // ── Stage 5: Approval ────────────────────────────────────────────────────
    public Guid?             PendingApprovalId  { get; private set; }
    public string?           ApprovalTier       { get; private set; }

    // ── Stage 6: Execution ───────────────────────────────────────────────────
    public string?           BankTransactionId  { get; private set; }
    public DateTimeOffset?   SubmittedToBankAt  { get; private set; }
    public DateTimeOffset?   SettledAt          { get; private set; }

    // ── Exception ─────────────────────────────────────────────────────────────
    public string?           BlockReason        { get; private set; }

    // ── GDPR / audit retention ────────────────────────────────────────────────
    // Spec §11: audit records retained minimum 7 years (FATF / OECD / local statute)
    public DateTimeOffset    RetainUntil        { get; private set; }

    private PaymentInstruction() { }

    public static PaymentInstruction Create(
        string payerName, string payerJurisdiction, string payerEntityId,
        string payeeRef, decimal grossAmount, string currency,
        ServiceType serviceType, string ppmId, string initiatorId,
        DateTimeOffset now,
        Guid? id = null) =>
        new()
        {
            Id               = id ?? Guid.NewGuid(),
            PayerName        = payerName,
            PayerJurisdiction= payerJurisdiction,
            PayerEntityId    = payerEntityId,
            PayeeRef         = payeeRef,
            GrossAmount      = grossAmount,
            Currency         = currency,
            ServiceType      = serviceType,
            PpmId            = ppmId,
            InitiatorId      = initiatorId,
            InitiatedAt      = now,
            Status           = PaymentStatus.Initiated,
            CurrentStage     = 0,
            // 7-year retention per FATF, OECD, local statute
            RetainUntil      = now.AddYears(7),
            CreatedAt        = now,
            UpdatedAt        = now,
        };

    public void AttachVerifiedPayee(Guid payeeId)
    {
        VerifiedPayeeId = payeeId;
        Status          = PaymentStatus.PayeeVerified;
        CurrentStage    = 1;
        UpdatedAt       = DateTimeOffset.UtcNow;
    }

    public void MarkPpmChecked()
    {
        Status       = PaymentStatus.PpmChecked;
        CurrentStage = 2;
        UpdatedAt    = DateTimeOffset.UtcNow;
    }

    public void ApplyWhtCalculation(
        decimal whtRate, decimal whtAmount, decimal netPayable,
        WhtConfidenceLevel confidence, string justification,
        string? treaty, string serviceClassification)
    {
        WhtRate               = whtRate;
        WhtAmount             = whtAmount;
        NetPayableAmount      = netPayable;
        WhtConfidence         = confidence;
        WhtJustification      = justification;
        ApplicableTreaty      = treaty;
        ServiceClassification = serviceClassification;
        Status                = confidence == WhtConfidenceLevel.ReviewRequired
            ? PaymentStatus.HeldTaxReview
            : PaymentStatus.WhtCalculated;
        CurrentStage          = 3;
        UpdatedAt             = DateTimeOffset.UtcNow;
    }

    public void ApplyKycResult(KycMatchResult result, decimal score, string screeningRef)
    {
        KycResult       = result;
        KycMatchScore   = score;
        KycScreeningRef = screeningRef;
        Status = result switch
        {
            KycMatchResult.NoMatch        => PaymentStatus.KycScreened,
            KycMatchResult.PotentialMatch => PaymentStatus.HeldComplianceReview,
            KycMatchResult.ConfirmedMatch => PaymentStatus.BlockedKyc,
            _                             => PaymentStatus.ExceptionQueue,
        };
        CurrentStage = 4;
        UpdatedAt    = DateTimeOffset.UtcNow;
    }

    public void MarkPendingApproval(Guid pendingApprovalId, string approvalTier)
    {
        PendingApprovalId = pendingApprovalId;
        ApprovalTier      = approvalTier;
        Status            = PaymentStatus.PendingApproval;
        CurrentStage      = 5;
        UpdatedAt         = DateTimeOffset.UtcNow;
    }

    public void MarkApprovalGranted()
    {
        Status       = PaymentStatus.ApprovalGranted;
        UpdatedAt    = DateTimeOffset.UtcNow;
    }

    public void MarkSubmittedToBank(string bankTransactionId)
    {
        BankTransactionId  = bankTransactionId;
        SubmittedToBankAt  = DateTimeOffset.UtcNow;
        Status             = PaymentStatus.SubmittedToBank;
        CurrentStage       = 6;
        UpdatedAt          = DateTimeOffset.UtcNow;
    }

    public void MarkSettled()
    {
        SettledAt    = DateTimeOffset.UtcNow;
        Status       = PaymentStatus.Settled;
        CurrentStage = 7;
        UpdatedAt    = DateTimeOffset.UtcNow;
    }

    public void Block(PaymentStatus blockStatus, string reason)
    {
        Status      = blockStatus;
        BlockReason = reason;
        UpdatedAt   = DateTimeOffset.UtcNow;
    }
}

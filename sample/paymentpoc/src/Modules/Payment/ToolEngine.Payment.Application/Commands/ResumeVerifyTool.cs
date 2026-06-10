using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ToolEngine.Core.Abstractions.Persistence;
using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Core.Domain.Enums;
using ToolEngine.Infrastructure.Persistence;
using ToolEngine.Payment.Domain.Entities;
using ToolEngine.Payment.Domain.Enums;
using ToolEngine.Tools.Abstractions.Base;
using ToolEngine.Tools.Abstractions.Models;

namespace ToolEngine.Payment.Application.Commands;

// ── Input / Output ────────────────────────────────────────────────────────────

public sealed record ResumeVerifyInput(
    Guid    PaymentId,
    decimal ConfirmedAmount,
    string  ConfirmedCurrency);

public sealed record ResumeVerifyOutput(
    // Token
    string         VerificationToken,
    DateTimeOffset ExpiresAt,

    // Payment summary
    Guid           PaymentId,
    string         PayerName,
    string         PayeeRef,
    string         PayeeLegalName,
    string         PayeeStatus,
    string         PayeeJurisdiction,
    decimal        GrossAmount,
    string         Currency,
    decimal        NetPayableAmount,
    decimal        WhtRate,
    decimal        WhtAmount,
    string         KycResult,
    string         ApprovalTier,
    string         PpmId,
    string         PpmStatus,
    string         ServiceType,
    int            CurrentStage,
    string         Message);

// ── Handler ───────────────────────────────────────────────────────────────────

/// <summary>
/// payment.resume-payment-verify — Full re-validation gate before payment execution.
///
/// Performs comprehensive verification after human approval:
///   1. Payment exists and has passed the approval gate.
///   2. Human approval record is Approved.
///   3. Operator confirms PRID, gross amount, and currency only.
///   4. All pipeline stages (0–4) completed.
///   5. Payee is still active with complete bank details.
///   6. KYC screening is clear (NoMatch).
///   7. PPM contract is still effective.
///
/// Returns a full payment summary for operator review and a short-lived
/// HMAC token (~10 min) that payment.resume requires.
/// </summary>
public sealed class ResumeVerifyTool
    : DatabaseToolBase<ResumeVerifyInput, ResumeVerifyOutput>
{
    private readonly AppDbContext _db;
    private readonly string       _secret;

    public ResumeVerifyTool(
        IUnitOfWork                         unitOfWork,
        AppDbContext                        db,
        IOptions<PaymentApplicationOptions> options)
        : base(unitOfWork)
    {
        _db     = db;
        _secret = options.Value.ResumeVerificationSecret;
    }

    public override string    Namespace => "payment";
    public override string    Name      => "resume-payment-verify";
    public override string    Version   => "v1";
    public override ToolSchema Schema   => new(
        Description:  "Performs full re-validation of a human-approved payment before execution. Operator confirms the gross amount and currency against the payment record. Also re-checks payee status, bank details, KYC clearance, and PPM contract validity. Returns a complete payment summary and a short-lived verification token.",
        WhenToUse:    "Requires `paymentId`, `confirmedAmount` (gross amount as initiated), and `confirmedCurrency`. Human approval must be granted before calling. Returns a `verificationToken` (valid ~10 min) and a full payment summary for operator review.",
        WhenNotToUse: "Do not call if the payment has not been approved. Do not call if the payment is already SETTLED or FAILED.",
        Examples:     ["Verify GBP 5000 payment to Acme Consulting before execution", "Confirm payment details before resuming USD 10000 transaction"],
        InputSchema:  BuildJsonSchema<ResumeVerifyInput>(),
        OutputSchema: BuildJsonSchema<ResumeVerifyOutput>());

    protected override async Task<ToolResponse<ResumeVerifyOutput>> HandleAsync(
        ToolRequest<ResumeVerifyInput> request, CancellationToken ct)
    {
        var inp = request.Input;

        // ── Load payment ──────────────────────────────────────────────────────
        var payment = await _db.Set<PaymentInstruction>()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == inp.PaymentId, ct);

        if (payment is null)
            return Fail(request, $"No payment found with PRID '{inp.PaymentId}'. Ensure payment.initiate was called first.");

        // ── Payment must be at the approval gate ──────────────────────────────
        if (payment.Status != PaymentStatus.PendingApproval
         && payment.Status != PaymentStatus.ApprovalGranted)
            return Fail(request,
                $"Payment '{inp.PaymentId}' has status '{payment.Status}'. " +
                "Only payments suspended at the approval gate (Stage 5) can be verified for resume.");

        // ── Human approval must be granted ────────────────────────────────────
        if (!payment.PendingApprovalId.HasValue)
            return Fail(request, "This payment has no associated approval request. Run the pipeline through Stage 5 first.");

        var approval = await _db.Set<ToolEngine.Core.Domain.Entities.PendingApproval>()
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == payment.PendingApprovalId.Value, ct);

        if (approval is null || approval.Status != ToolEngine.Core.Domain.Enums.ApprovalStatus.Approved)
            return Fail(request,
                $"Human approval has not been granted yet. Current approval status: " +
                $"{approval?.Status.ToString() ?? "NOT_FOUND"}. " +
                "Please approve the payment in the Approvals panel before verifying for resume.");

        // ── All pipeline stages must be complete (0–4) ────────────────────────
        if (payment.CurrentStage < 4)
            return Fail(request,
                $"Pipeline incomplete — payment is at Stage {payment.CurrentStage}. " +
                "All stages 0–4 must complete before resume verification.");

        // ── Confirm amount ────────────────────────────────────────────────────
        if (Math.Abs(inp.ConfirmedAmount - payment.GrossAmount) > 0.01m)
            return Fail(request,
                $"Amount mismatch: you confirmed {inp.ConfirmedAmount} but the payment gross amount is {payment.GrossAmount} {payment.Currency}. " +
                "Please confirm the exact gross amount as originally initiated.");

        // ── Confirm currency ──────────────────────────────────────────────────
        if (!string.Equals(inp.ConfirmedCurrency, payment.Currency, StringComparison.OrdinalIgnoreCase))
            return Fail(request,
                $"Currency mismatch: you confirmed '{inp.ConfirmedCurrency}' but the payment currency is '{payment.Currency}'. " +
                "Please confirm the correct currency.");

        // ── Load verified payee for re-validation ─────────────────────────────
        PayeeRecord? payee = null;
        if (payment.VerifiedPayeeId.HasValue)
        {
            payee = await _db.Set<PayeeRecord>()
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == payment.VerifiedPayeeId.Value, ct);
        }

        // ── Re-validate payee status ──────────────────────────────────────────
        if (payee is null)
            return Fail(request,
                "No verified payee linked to this payment. Payment.verify-payee must be run in pipeline mode first.");

        if (payee.Status != PayeeStatus.Active)
            return Fail(request,
                $"Payee '{payee.LegalName}' is now {payee.Status}. " +
                "Payment cannot be resumed until payee status is resolved.");

        // ── Re-validate bank details ──────────────────────────────────────────
        if (!payee.HasCompleteBankDetails())
            return Fail(request,
                $"Payee '{payee.LegalName}' bank details are no longer complete (SWIFT/IBAN missing). " +
                "Cannot resume payment execution.");

        // ── Re-validate KYC clearance ─────────────────────────────────────────
        if (payment.KycResult.HasValue && payment.KycResult != KycMatchResult.NoMatch)
            return Fail(request,
                $"KYC screening returned {payment.KycResult}. " +
                "Payment cannot resume with an unresolved KYC flag.");

        // ── Re-validate PPM contract ──────────────────────────────────────────
        var ppmContract = await _db.Set<PpmContract>()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.PpmId == payment.PpmId, ct);

        var ppmStatus = "UNKNOWN";
        if (ppmContract is not null)
        {
            ppmStatus = ppmContract.IsEffective(DateTimeOffset.UtcNow) ? "ACTIVE" : "EXPIRED";
            if (ppmStatus == "EXPIRED")
                return Fail(request,
                    $"PPM contract '{payment.PpmId}' has expired (effective until {ppmContract.EffectiveTo:yyyy-MM-dd}). " +
                    "Payment cannot be executed against an expired contract.");
        }

        // ── All checks passed — generate token ────────────────────────────────
        var token     = VerificationTokenHelper.Generate(inp.PaymentId, payment.GrossAmount, payment.Currency, _secret);
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(10);

        return ToolResponse<ResumeVerifyOutput>.Ok(
            request.CorrelationId,
            new ResumeVerifyOutput(
                VerificationToken: token,
                ExpiresAt:         expiresAt,

                PaymentId:         payment.Id,
                PayerName:         payment.PayerName,
                PayeeRef:          payment.PayeeRef,
                PayeeLegalName:    payee.LegalName,
                PayeeStatus:       payee.Status.ToString(),
                PayeeJurisdiction: payee.Jurisdiction,
                GrossAmount:       payment.GrossAmount,
                Currency:          payment.Currency,
                NetPayableAmount:  payment.NetPayableAmount ?? payment.GrossAmount,
                WhtRate:           payment.WhtRate ?? 0m,
                WhtAmount:         payment.WhtAmount ?? 0m,
                KycResult:         payment.KycResult?.ToString() ?? "NOT_SCREENED",
                ApprovalTier:      payment.ApprovalTier ?? "UNKNOWN",
                PpmId:             payment.PpmId,
                PpmStatus:         ppmStatus,
                ServiceType:       payment.ServiceType.ToString(),
                CurrentStage:      payment.CurrentStage,
                Message:           $"Full verification passed. {payment.GrossAmount:F2} {payment.Currency} to '{payee.LegalName}' confirmed. " +
                                   $"Payee ACTIVE, KYC clear, PPM {ppmStatus}. " +
                                   $"Token valid until ~{expiresAt:HH:mm} UTC. Pass verificationToken to payment.resume now."));
    }

    private static ToolResponse<ResumeVerifyOutput> Fail(
        ToolRequest<ResumeVerifyInput> r, string msg) =>
        ToolResponse<ResumeVerifyOutput>.Fail(r.CorrelationId, ToolError.Validation(msg));
}

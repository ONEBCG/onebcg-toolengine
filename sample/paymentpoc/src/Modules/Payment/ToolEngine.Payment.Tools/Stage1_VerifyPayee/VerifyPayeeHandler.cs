using Microsoft.EntityFrameworkCore;
using ToolEngine.Core.Abstractions.Persistence;
using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Infrastructure.Persistence;
using ToolEngine.Payment.Domain.Entities;
using ToolEngine.Payment.Domain.Enums;
using ToolEngine.Tools.Abstractions.Base;
using ToolEngine.Tools.Abstractions.Models;

namespace ToolEngine.Payment.Tools.Stage1_VerifyPayee;

// ── Input / Output ────────────────────────────────────────────────────────────

public sealed record VerifyPayeeInput(Guid PaymentId, string PayeeRef);

public sealed record VerifyPayeeOutput(
    Guid       PayeeId,
    string     LegalName,
    string     Jurisdiction,
    string     EntityType,
    string     Status,
    bool       HasCompleteBankDetails,
    string?    SwiftBic,
    string?    Iban,
    string     Message);

// ── Handler ───────────────────────────────────────────────────────────────────

/// <summary>
/// Stage 1 — Payee Verification (Database).
/// Looks up the payee by PayeeRef in the internal entity database.
/// Enforces all Stage 1 barriers: not found, inactive/suspended, pending review,
/// incomplete bank details (spec §4 Stage 1).
/// </summary>
public sealed class VerifyPayeeHandler
    : DatabaseToolBase<VerifyPayeeInput, VerifyPayeeOutput>
{
    private readonly AppDbContext _db;

    public VerifyPayeeHandler(IUnitOfWork unitOfWork, AppDbContext db)
        : base(unitOfWork) => _db = db;

    public override string    Namespace => "payment";
    public override string    Name      => "verify-payee";
    public override string    Version   => "v1";
    public override ToolSchema Schema   => new(
        Description:  "Verifies the payee exists in the entity database and is active with complete bank details. Links the verified payee to the PaymentInstruction.",
        WhenToUse:    "Call after payment.initiate. Pass `paymentId` (prid from initiate) and `payeeRef` (payee name). The returned `payeeId` must be passed to payment.ppm-check as `verifiedPayeeId` and to payment.kyc-screen as `payeeId`. The returned `jurisdiction` is needed by payment.calculate-wht as `payeeJurisdiction`. The returned `entityType` and `legalName` are needed by payment.kyc-screen.",
        WhenNotToUse: "Do not call before payment.initiate. Do not call for payee onboarding or KYC screening — those are separate tools.",
        Examples:     ["Verify Acme Consulting is an approved payee", "Check that Risq Capital has complete bank details"],
        InputSchema:  BuildJsonSchema<VerifyPayeeInput>(),
        OutputSchema: BuildJsonSchema<VerifyPayeeOutput>());

    protected override async Task<ToolResponse<VerifyPayeeOutput>> HandleAsync(
        ToolRequest<VerifyPayeeInput> request, CancellationToken ct)
    {
        var inp = request.Input;

        // Attempt lookup by Id parse, then by LegalName substring (simple ref matching for POC)
        PayeeRecord? payee = null;

        if (Guid.TryParse(inp.PayeeRef, out var payeeGuid))
            payee = await _db.Set<PayeeRecord>().FirstOrDefaultAsync(p => p.Id == payeeGuid, ct);

        payee ??= await _db.Set<PayeeRecord>()
            .FirstOrDefaultAsync(p =>
                EF.Functions.Like(p.LegalName, $"%{inp.PayeeRef}%"), ct);

        // ── Barrier: NOT FOUND ───────────────────────────────────────────────
        if (payee is null)
        {
            await UpdatePaymentStatusAsync(inp.PaymentId,
                PaymentStatus.BlockedUnknownPayee, "UNKNOWN_PAYEE", ct);
            return ToolResponse<VerifyPayeeOutput>.Fail(
                request.CorrelationId,
                ToolError.NotFound($"Payee '{inp.PayeeRef}' not found. Routed to EHQ: UNKNOWN_PAYEE."));
        }

        // ── Barrier: INACTIVE / SUSPENDED ───────────────────────────────────
        if (payee.Status == PayeeStatus.Inactive || payee.Status == PayeeStatus.Suspended)
        {
            await UpdatePaymentStatusAsync(inp.PaymentId,
                PaymentStatus.BlockedInactivePayee, $"Payee status: {payee.Status}", ct);
            return ToolResponse<VerifyPayeeOutput>.Fail(
                request.CorrelationId,
                ToolError.Validation($"Payee '{payee.LegalName}' is {payee.Status}. Payment blocked."));
        }

        // ── Barrier: PENDING_REVIEW ──────────────────────────────────────────
        if (payee.Status == PayeeStatus.PendingReview)
        {
            await UpdatePaymentStatusAsync(inp.PaymentId,
                PaymentStatus.ExceptionQueue, "Payee PENDING_REVIEW", ct);
            return ToolResponse<VerifyPayeeOutput>.Fail(
                request.CorrelationId,
                ToolError.Validation($"Payee '{payee.LegalName}' is PENDING_REVIEW. Payment held."));
        }

        // ── Barrier: INCOMPLETE BANK DETAILS ────────────────────────────────
        if (!payee.HasCompleteBankDetails())
        {
            await UpdatePaymentStatusAsync(inp.PaymentId,
                PaymentStatus.ExceptionQueue, "Incomplete bank details", ct);
            return ToolResponse<VerifyPayeeOutput>.Fail(
                request.CorrelationId,
                ToolError.Validation($"Payee '{payee.LegalName}' has incomplete bank details (SWIFT/IBAN required)."));
        }

        // ── PASS — attach payee to payment ───────────────────────────────────
        var payment = await _db.Set<PaymentInstruction>()
            .FirstOrDefaultAsync(p => p.Id == inp.PaymentId, ct);

        // Guard: if the instruction was deleted between Stage 0 and here, fail
        // explicitly rather than silently skipping state advancement — a silent
        // skip returns success but leaves the payment in the wrong state, causing
        // cascade failures in Stages 2+.
        if (payment is null)
            return ToolResponse<VerifyPayeeOutput>.Fail(
                request.CorrelationId,
                ToolError.NotFound(
                    $"PaymentInstruction '{inp.PaymentId}' was not found when attaching verified payee."));

        payment.AttachVerifiedPayee(payee.Id);
        await UnitOfWork.SaveChangesAsync(ct);

        return ToolResponse<VerifyPayeeOutput>.Ok(
            request.CorrelationId,
            new VerifyPayeeOutput(
                PayeeId:               payee.Id,
                LegalName:             payee.LegalName,
                Jurisdiction:          payee.Jurisdiction,
                EntityType:            payee.EntityType.ToString(),
                Status:                payee.Status.ToString(),
                HasCompleteBankDetails: true,
                SwiftBic:              payee.SwiftBic,
                Iban:                  payee.Iban,
                Message:               $"Payee '{payee.LegalName}' verified and ACTIVE. Proceeding to PPM check."));
    }

    private async Task UpdatePaymentStatusAsync(
        Guid paymentId, PaymentStatus status, string reason, CancellationToken ct)
    {
        var payment = await _db.Set<PaymentInstruction>()
            .FirstOrDefaultAsync(p => p.Id == paymentId, ct);
        if (payment is null) return;
        payment.Block(status, reason);
        await UnitOfWork.SaveChangesAsync(ct);
    }
}

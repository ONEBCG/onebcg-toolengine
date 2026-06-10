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

public sealed record VerifyPayeeInput(Guid? PaymentId, string PayeeRef);

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
///
/// Two modes:
///   Standalone (paymentId omitted) — read-only lookup: returns payee details
///     (status, jurisdiction, bank details, entity type) with no DB writes.
///   Pipeline (paymentId provided) — lookup + attach: verifies the payee and
///     links it to the PaymentInstruction. Updates payment status on failure.
///
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
        Description:  "Verifies if the payee exists in the entity database and is active with complete bank details. When a `paymentId` is provided, links the verified payee to the PaymentInstruction.",
        WhenToUse:    "Requires `payeeRef` (payee name or ID). `paymentId` is optional — if provided (prid from payment.initiate), the verified payee is linked to the payment record and payment status is updated on failure. If omitted, returns payee details only with no payment-side effects. Returns `payeeId`, `legalName`, `jurisdiction`, `entityType`, `status`, `swiftBic`, `iban`.",
        WhenNotToUse: "Do not call for payee onboarding or KYC screening — those are separate tools.",
        Examples:     ["Verify Acme Consulting is an approved payee", "Check that Risq Capital has complete bank details", "Verify and link Acme Consulting to payment PRID-abc123"],
        InputSchema:  BuildJsonSchema<VerifyPayeeInput>(),
        OutputSchema: BuildJsonSchema<VerifyPayeeOutput>());

    protected override async Task<ToolResponse<VerifyPayeeOutput>> HandleAsync(
        ToolRequest<VerifyPayeeInput> request, CancellationToken ct)
    {
        var inp            = request.Input;
        var hasPipeline    = inp.PaymentId.HasValue;

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
            if (hasPipeline)
                await UpdatePaymentStatusAsync(inp.PaymentId!.Value,
                    PaymentStatus.BlockedUnknownPayee, "UNKNOWN_PAYEE", ct);
            return ToolResponse<VerifyPayeeOutput>.Fail(
                request.CorrelationId,
                ToolError.NotFound($"Payee '{inp.PayeeRef}' not found.{(hasPipeline ? " Routed to EHQ: UNKNOWN_PAYEE." : "")}"));
        }

        // ── Barrier: INACTIVE / SUSPENDED ───────────────────────────────────
        if (payee.Status == PayeeStatus.Inactive || payee.Status == PayeeStatus.Suspended)
        {
            if (hasPipeline)
                await UpdatePaymentStatusAsync(inp.PaymentId!.Value,
                    PaymentStatus.BlockedInactivePayee, $"Payee status: {payee.Status}", ct);
            return ToolResponse<VerifyPayeeOutput>.Fail(
                request.CorrelationId,
                ToolError.Validation($"Payee '{payee.LegalName}' is {payee.Status}.{(hasPipeline ? " Payment blocked." : "")}"));
        }

        // ── Barrier: PENDING_REVIEW ──────────────────────────────────────────
        if (payee.Status == PayeeStatus.PendingReview)
        {
            if (hasPipeline)
                await UpdatePaymentStatusAsync(inp.PaymentId!.Value,
                    PaymentStatus.ExceptionQueue, "Payee PENDING_REVIEW", ct);
            return ToolResponse<VerifyPayeeOutput>.Fail(
                request.CorrelationId,
                ToolError.Validation($"Payee '{payee.LegalName}' is PENDING_REVIEW.{(hasPipeline ? " Payment held." : "")}"));
        }

        // ── Barrier: INCOMPLETE BANK DETAILS ────────────────────────────────
        if (!payee.HasCompleteBankDetails())
        {
            if (hasPipeline)
                await UpdatePaymentStatusAsync(inp.PaymentId!.Value,
                    PaymentStatus.ExceptionQueue, "Incomplete bank details", ct);
            return ToolResponse<VerifyPayeeOutput>.Fail(
                request.CorrelationId,
                ToolError.Validation($"Payee '{payee.LegalName}' has incomplete bank details (SWIFT/IBAN required)."));
        }

        // ── Pipeline mode: attach payee to payment ───────────────────────────
        if (hasPipeline)
        {
            var payment = await _db.Set<PaymentInstruction>()
                .FirstOrDefaultAsync(p => p.Id == inp.PaymentId!.Value, ct);

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
        }

        var message = hasPipeline
            ? $"Payee '{payee.LegalName}' verified and ACTIVE. Linked to payment. Proceeding to next stage."
            : $"Payee '{payee.LegalName}' verified and ACTIVE.";

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
                Message:               message));
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

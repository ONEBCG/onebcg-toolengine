using Microsoft.EntityFrameworkCore;
using ToolEngine.Core.Abstractions.Persistence;
using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Infrastructure.Persistence;
using ToolEngine.Payment.Domain.Entities;
using ToolEngine.Payment.Domain.Enums;
using ToolEngine.Tools.Abstractions.Base;
using ToolEngine.Tools.Abstractions.Models;

namespace ToolEngine.Payment.Tools.Stage7_Reconcile;

// ── Input / Output ────────────────────────────────────────────────────────────

public sealed record ReconcilePaymentInput(
    Guid    PaymentId,
    string  BankTransactionId,
    decimal SettledAmount,
    string  Currency,
    string? BankReference);

public sealed record ReconcilePaymentOutput(
    bool    IsReconciled,
    string  FinalStatus,
    decimal DiscrepancyAmount,
    bool    HasDiscrepancy,
    string  Message);

// ── Handler ───────────────────────────────────────────────────────────────────

/// <summary>
/// Stage 7 — Post-Payment Reconciliation and Audit Close (Database).
/// Matches bank settlement confirmation against PaymentInstruction record.
/// Updates PPM cumulative payment tracker for Stage 2 aggregate cap enforcement.
/// Archives audit trail. Flags discrepancies for Finance review (spec §4 Stage 7).
/// </summary>
public sealed class ReconcilePaymentHandler
    : DatabaseToolBase<ReconcilePaymentInput, ReconcilePaymentOutput>
{
    private readonly AppDbContext _db;

    public ReconcilePaymentHandler(IUnitOfWork unitOfWork, AppDbContext db)
        : base(unitOfWork) => _db = db;

    public override string    Namespace => "payment";
    public override string    Name      => "reconcile";
    public override string    Version   => "v1";
    public override ToolSchema Schema   => new(
        Description:  "Reconciles the executed payment against the bank transaction and marks it as SETTLED. Final step in the payment lifecycle.",
        WhenToUse:    "Requires `paymentId`, `bankTransactionId` (from payment.execute-payment output — system-generated, no other source), `settledAmount` (the bank-confirmed amount — typically netPayableAmount from payment.calculate-wht, or grossAmount if WHT was zero), `currency`, and optionally `bankReference`.",
        WhenNotToUse: "Do not call before payment.execute-payment has succeeded and returned a bank transaction ID.",
        Examples:     ["Reconcile settled payment with bank transaction TX-12345", "Mark payment as settled after bank confirmation"],
        InputSchema:  BuildJsonSchema<ReconcilePaymentInput>(),
        OutputSchema: BuildJsonSchema<ReconcilePaymentOutput>());

    protected override async Task<ToolResponse<ReconcilePaymentOutput>> HandleAsync(
        ToolRequest<ReconcilePaymentInput> request, CancellationToken ct)
    {
        var inp = request.Input;

        var payment = await _db.Set<PaymentInstruction>()
            .FirstOrDefaultAsync(p => p.Id == inp.PaymentId, ct);

        if (payment is null)
            return ToolResponse<ReconcilePaymentOutput>.Fail(
                request.CorrelationId, ToolError.NotFound($"Payment '{inp.PaymentId}' not found."));

        if (payment.BankTransactionId != inp.BankTransactionId)
            return ToolResponse<ReconcilePaymentOutput>.Fail(
                request.CorrelationId,
                ToolError.Validation(
                    $"Bank TX ID mismatch. Expected: '{payment.BankTransactionId}', received: '{inp.BankTransactionId}'."));

        // ── Discrepancy check ─────────────────────────────────────────────────
        var expected          = payment.NetPayableAmount ?? payment.GrossAmount;
        var discrepancyAmount = Math.Abs(inp.SettledAmount - expected);
        var hasDiscrepancy    = discrepancyAmount > 0.01m;   // tolerance: 1 cent

        if (hasDiscrepancy)
        {
            payment.Block(PaymentStatus.HeldReconciliation,
                $"Settlement discrepancy: expected {expected} {inp.Currency}, " +
                $"settled {inp.SettledAmount} {inp.Currency}. Flagged for Finance review.");
            await UnitOfWork.SaveChangesAsync(ct);

            return ToolResponse<ReconcilePaymentOutput>.Ok(
                request.CorrelationId,
                new ReconcilePaymentOutput(
                    IsReconciled:     false,
                    FinalStatus:      PaymentStatus.HeldReconciliation.ToString(),
                    DiscrepancyAmount: discrepancyAmount,
                    HasDiscrepancy:   true,
                    Message:          $"HELD_RECONCILIATION: Settlement amount mismatch of {discrepancyAmount} {inp.Currency}. " +
                                      "Finance team notified. Requires manual resolution."));
        }

        // ── SETTLED — update payment + PPM cumulative tracker ────────────────
        payment.MarkSettled();

        // Update PPM aggregate cap (Stage 2 contract cap enforcement)
        if (!string.IsNullOrWhiteSpace(payment.PpmId) && payment.VerifiedPayeeId.HasValue)
        {
            var contract = await _db.Set<PpmContract>()
                .FirstOrDefaultAsync(c => c.PpmId == payment.PpmId
                                       && c.PayeeId == payment.VerifiedPayeeId.Value, ct);
            contract?.IncrementCumulativePaid(inp.SettledAmount);
        }

        await UnitOfWork.SaveChangesAsync(ct);

        return ToolResponse<ReconcilePaymentOutput>.Ok(
            request.CorrelationId,
            new ReconcilePaymentOutput(
                IsReconciled:     true,
                FinalStatus:      PaymentStatus.Settled.ToString(),
                DiscrepancyAmount: 0m,
                HasDiscrepancy:   false,
                Message:          $"Payment SETTLED. Bank TX: {inp.BankTransactionId}. " +
                                  $"Amount: {inp.SettledAmount} {inp.Currency}. " +
                                  "Audit trail closed. PPM cumulative tracker updated."));
    }
}

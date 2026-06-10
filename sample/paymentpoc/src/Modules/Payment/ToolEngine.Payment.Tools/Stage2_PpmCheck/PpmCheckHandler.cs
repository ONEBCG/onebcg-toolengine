using Microsoft.EntityFrameworkCore;
using ToolEngine.Core.Abstractions.Persistence;
using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Infrastructure.Persistence;
using ToolEngine.Payment.Domain.Entities;
using ToolEngine.Payment.Domain.Enums;
using ToolEngine.Tools.Abstractions.Base;
using ToolEngine.Tools.Abstractions.Models;

namespace ToolEngine.Payment.Tools.Stage2_PpmCheck;

// ── Input / Output ────────────────────────────────────────────────────────────

public sealed record PpmCheckInput(
    Guid        PaymentId,
    string      PpmId,
    Guid        VerifiedPayeeId,
    ServiceType ServiceType,
    decimal     GrossAmount,
    string      Currency);

public sealed record PpmCheckOutput(
    bool    IsPermitted,
    string  PpmId,
    string  ContractVersion,
    decimal RemainingCapacity,
    string? ClauseReference,
    string  Message);

// ── Handler ───────────────────────────────────────────────────────────────────

/// <summary>
/// Stage 2 — Contractual Obligation Check (Database).
/// Validates the payment instruction against the governing PPM agreement.
/// Enforces all Stage 2 barriers and produces a structured decision record
/// referencing the specific clause that blocked or permitted the payment (spec §4 Stage 2).
/// </summary>
public sealed class PpmCheckHandler
    : DatabaseToolBase<PpmCheckInput, PpmCheckOutput>
{
    private readonly AppDbContext _db;

    public PpmCheckHandler(IUnitOfWork unitOfWork, AppDbContext db)
        : base(unitOfWork) => _db = db;

    public override string    Namespace => "payment";
    public override string    Name      => "ppm-check";
    public override string    Version   => "v1";
    public override ToolSchema Schema   => new(
        Description:  "Validates the payment against the governing PPM contract: permitted service types, approved currencies, per-transaction cap, and aggregate cap. Blocks if any condition is violated.",
        WhenToUse:    "Requires `paymentId` (prid from payment.initiate), `ppmId` (the governing PPM contract ID, e.g. PPM-001), `verifiedPayeeId` (payeeId GUID from payment.verify-payee), `serviceType`, `grossAmount`, and `currency`.",
        WhenNotToUse: "Do not call before payment.initiate and payment.verify-payee have both succeeded. Do not use for contract creation or amendment.",
        Examples:     ["Check if GBP 50000 consulting payment is permitted under PPM-001", "Validate USD 10000 payment against expired contract PPM-002"],
        InputSchema:  BuildJsonSchema<PpmCheckInput>(),
        OutputSchema: BuildJsonSchema<PpmCheckOutput>());

    protected override async Task<ToolResponse<PpmCheckOutput>> HandleAsync(
        ToolRequest<PpmCheckInput> request, CancellationToken ct)
    {
        var inp = request.Input;

        var contract = await _db.Set<PpmContract>()
            .FirstOrDefaultAsync(c => c.PpmId == inp.PpmId
                                   && c.PayeeId == inp.VerifiedPayeeId, ct);

        // ── PPM not found or not linked to payee ─────────────────────────────
        if (contract is null)
        {
            await BlockPaymentAsync(inp.PaymentId, PaymentStatus.BlockedContract,
                "No active PPM found for this payer-payee pair.", ct);
            return Fail(request.CorrelationId,
                "CONTRACT_PAYEE_MISMATCH",
                $"No PPM '{inp.PpmId}' found for payee {inp.VerifiedPayeeId}. Clause: §2.1 Approved Payee List.");
        }

        // ── Barrier: contract not effective ──────────────────────────────────
        if (!contract.IsEffective(DateTimeOffset.UtcNow))
        {
            await BlockPaymentAsync(inp.PaymentId, PaymentStatus.BlockedContract,
                "CONTRACT_INACTIVE", ct);
            return Fail(request.CorrelationId,
                "CONTRACT_INACTIVE",
                $"PPM '{inp.PpmId}' is expired or not yet effective. Clause: §3.1 Contract Validity Period.");
        }

        // ── Barrier: service type not approved ───────────────────────────────
        if (!contract.PermitsServiceType(inp.ServiceType))
        {
            await BlockPaymentAsync(inp.PaymentId, PaymentStatus.BlockedContract,
                "CONTRACT_SERVICE_NOT_APPROVED", ct);
            return Fail(request.CorrelationId,
                "CONTRACT_SERVICE_NOT_APPROVED",
                $"Service type '{inp.ServiceType}' not permitted under PPM '{inp.PpmId}'. Clause: §4.2 Permitted Services.");
        }

        // ── Barrier: currency not approved ───────────────────────────────────
        if (!contract.PermitsCurrency(inp.Currency))
        {
            await BlockPaymentAsync(inp.PaymentId, PaymentStatus.BlockedContract,
                "CONTRACT_CURRENCY_NOT_APPROVED", ct);
            return Fail(request.CorrelationId,
                "CONTRACT_CURRENCY_NOT_APPROVED",
                $"Currency '{inp.Currency}' not approved under PPM '{inp.PpmId}'. Clause: §5.1 Approved Currencies.");
        }

        // ── Barrier: per-transaction limit ───────────────────────────────────
        if (!contract.IsWithinTransactionLimit(inp.GrossAmount))
        {
            await BlockPaymentAsync(inp.PaymentId, PaymentStatus.BlockedContract,
                "CONTRACT_AMOUNT_EXCEEDED", ct);
            return Fail(request.CorrelationId,
                "CONTRACT_AMOUNT_EXCEEDED",
                $"Amount {inp.GrossAmount} {inp.Currency} exceeds per-transaction limit {contract.MaxSingleTransaction}. Clause: §5.2 Transaction Limit.");
        }

        // ── Barrier: aggregate cap ────────────────────────────────────────────
        if (!contract.IsWithinAggregateCapacity(inp.GrossAmount))
        {
            await BlockPaymentAsync(inp.PaymentId, PaymentStatus.BlockedContract,
                "CONTRACT_CAP_BREACH", ct);
            return Fail(request.CorrelationId,
                "CONTRACT_CAP_BREACH",
                $"Payment would breach aggregate cap. Remaining capacity: {contract.RemainingAggregateCapacity} {inp.Currency}. Clause: §5.3 Aggregate Cap.");
        }

        // ── PASS ──────────────────────────────────────────────────────────────
        var payment = await _db.Set<PaymentInstruction>()
            .FirstOrDefaultAsync(p => p.Id == inp.PaymentId, ct);

        // Guard: fail explicitly rather than silently skipping state advancement.
        if (payment is null)
            return ToolResponse<PpmCheckOutput>.Fail(
                request.CorrelationId,
                ToolError.NotFound(
                    $"PaymentInstruction '{inp.PaymentId}' was not found when marking PPM checked."));

        payment.MarkPpmChecked();
        await UnitOfWork.SaveChangesAsync(ct);

        return ToolResponse<PpmCheckOutput>.Ok(
            request.CorrelationId,
            new PpmCheckOutput(
                IsPermitted:       true,
                PpmId:             contract.PpmId,
                ContractVersion:   contract.ContractVersion,
                RemainingCapacity: contract.RemainingAggregateCapacity - inp.GrossAmount,
                ClauseReference:   null,
                Message:           $"Payment permitted under PPM '{inp.PpmId}' v{contract.ContractVersion}. Proceeding to tax calculation."));
    }

    private ToolResponse<PpmCheckOutput> Fail(Guid correlationId, string code, string message) =>
        ToolResponse<PpmCheckOutput>.Fail(correlationId, new ToolError(422, code, message));

    private async Task BlockPaymentAsync(
        Guid paymentId, PaymentStatus status, string reason, CancellationToken ct)
    {
        var payment = await _db.Set<PaymentInstruction>()
            .FirstOrDefaultAsync(p => p.Id == paymentId, ct);
        if (payment is null) return;
        payment.Block(status, reason);
        await UnitOfWork.SaveChangesAsync(ct);
    }
}

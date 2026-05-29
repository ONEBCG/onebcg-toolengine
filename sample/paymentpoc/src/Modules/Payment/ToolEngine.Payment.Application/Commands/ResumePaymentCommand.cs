using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ToolEngine.Application.Commands;
using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Core.Domain.Enums;
using ToolEngine.Infrastructure.Persistence;
using ToolEngine.Payment.Domain.Entities;
using ToolEngine.Payment.Domain.Enums;
using ToolEngine.Payment.Tools.Stage6_ExecutePayment;
using ToolEngine.Payment.Tools.Stage7_Reconcile;

namespace ToolEngine.Payment.Application.Commands;

// ── Command ───────────────────────────────────────────────────────────────────

public sealed record ResumePaymentCommand(
    Guid       Prid,
    string?    ApproverId,
    CallerType CallerType = CallerType.Human) : IRequest<ResumePaymentResult>;

public sealed record ResumePaymentResult(
    bool    IsSuccess,
    string  Status,
    string  Message,
    string? BankTransactionId = null,
    string? ErrorCode         = null);

// ── Handler ───────────────────────────────────────────────────────────────────

/// <summary>
/// Executes Stages 6-7 after approval has been granted.
/// Pre-conditions checked before calling Stage 6:
///   - Payment exists and is in ApprovalGranted status.
///   - PendingApproval record is Approved.
/// Immutability safeguard enforced by Stage 6 handler:
///   - Only payments in ApprovalGranted status can be submitted to bank.
/// </summary>
public sealed class ResumePaymentCommandHandler
    : IRequestHandler<ResumePaymentCommand, ResumePaymentResult>
{
    private readonly ISender      _mediator;
    private readonly AppDbContext _db;

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        // Must match ToolExecutor._jsonOptions — enums serialise as strings there.
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    public ResumePaymentCommandHandler(ISender mediator, AppDbContext db)
    {
        _mediator = mediator;
        _db       = db;
    }

    public async Task<ResumePaymentResult> Handle(
        ResumePaymentCommand cmd, CancellationToken ct)
    {
        // ── Load payment + approval ───────────────────────────────────────────
        var payment = await _db.Set<PaymentInstruction>()
            .FirstOrDefaultAsync(p => p.Id == cmd.Prid, ct);

        if (payment is null)
            return new ResumePaymentResult(false, PaymentErrorCodes.NotFound,
                $"Payment '{cmd.Prid}' not found.");

        // Must be in PendingApproval or ApprovalGranted status
        if (payment.Status != PaymentStatus.PendingApproval
         && payment.Status != PaymentStatus.ApprovalGranted)
            return new ResumePaymentResult(false, payment.Status.ToString(),
                $"Cannot resume payment — current status is '{payment.Status}'. " +
                "Expected PendingApproval or ApprovalGranted.");

        // Guard: a payment that was never submitted for approval cannot be resumed.
        // Without this check, a payment that skipped the approval gate (no PendingApprovalId)
        // would bypass the approval requirement and go straight to bank execution.
        if (!payment.PendingApprovalId.HasValue)
            return new ResumePaymentResult(false, PaymentErrorCodes.NotSubmitted,
                "Payment was never submitted for approval. " +
                "POST /api/v1/payments to run the pipeline through the approval gate first.");

        // Verify PendingApproval record is actually Approved
        var approval = await _db.Set<Core.Domain.Entities.PendingApproval>()
            .FirstOrDefaultAsync(a => a.Id == payment.PendingApprovalId.Value, ct);

        if (approval is null || approval.Status != Core.Domain.Enums.ApprovalStatus.Approved)
            return new ResumePaymentResult(false, PaymentErrorCodes.ApprovalPending,
                "Approval has not been granted yet. " +
                $"Current approval status: {approval?.Status.ToString() ?? "NOT_FOUND"}.");

        // Transition to ApprovalGranted if still PendingApproval
        if (payment.Status == PaymentStatus.PendingApproval)
        {
            payment.MarkApprovalGranted();
            await _db.SaveChangesAsync(ct);
        }

        // Load payee for bank details
        var payee = payment.VerifiedPayeeId.HasValue
            ? await _db.Set<PayeeRecord>()
                .FirstOrDefaultAsync(p => p.Id == payment.VerifiedPayeeId.Value, ct)
            : null;

        var rail = ResolvePaymentRail(payee);

        // ── Stage 6: Execute Payment ─────────────────────────────────────────
        var s6Response = await _mediator.Send(new ExecuteToolCommand(
            CorrelationId:  Guid.NewGuid(),
            ToolNamespace:  PaymentPipeline.Namespace,
            ToolName:       PaymentPipeline.Stage.ExecutePayment,
            ToolVersion:    PaymentPipeline.Version,
            Input:          ToJson(new ExecutePaymentInput(
                PaymentId:        cmd.Prid,
                NetPayableAmount: payment.NetPayableAmount ?? payment.GrossAmount,
                Currency:         payment.Currency,
                PayeeLegalName:   payee?.LegalName ?? "UNKNOWN",
                Iban:             payee?.Iban,
                SwiftBic:         payee?.SwiftBic,
                BankAccountNumber: payee?.BankAccountNumber,
                PaymentRail:      rail)),
            UserId:         cmd.ApproverId,
            CallerType:     cmd.CallerType,
            IdempotencyKey: $"{cmd.Prid}:{PaymentPipeline.Namespace}.{PaymentPipeline.Stage.ExecutePayment}"), ct);

        if (!s6Response.Success)
            return new ResumePaymentResult(false, PaymentErrorCodes.ExecutionFailed,
                s6Response.Error?.Description ?? "Stage 6 execution failed.",
                ErrorCode: s6Response.Error?.ErrorCode);

        // Extract bank TX ID from response
        string? bankTxId = null;
        if (s6Response is ToolResponse<JsonElement> s6Typed && s6Typed.Success)
        {
            var s6Out = s6Typed.Data.Deserialize<ExecutePaymentOutput>(_json);
            bankTxId = s6Out?.BankTransactionId;
        }

        await LogAuditAsync(cmd.Prid, 6, "PaymentExecution", "SUBMITTED",
            $"Submitted to bank. TX: {bankTxId}", cmd.ApproverId, ct);

        // ── Stage 7: Reconcile ────────────────────────────────────────────────
        // POC: immediate reconcile with same amount (stub bank always settles at full amount)
        var s7Response = await _mediator.Send(new ExecuteToolCommand(
            CorrelationId:  Guid.NewGuid(),
            ToolNamespace:  PaymentPipeline.Namespace,
            ToolName:       PaymentPipeline.Stage.Reconcile,
            ToolVersion:    PaymentPipeline.Version,
            Input:          ToJson(new ReconcilePaymentInput(
                PaymentId:         cmd.Prid,
                BankTransactionId: bankTxId ?? string.Empty,
                SettledAmount:     payment.NetPayableAmount ?? payment.GrossAmount,
                Currency:          payment.Currency,
                BankReference:     bankTxId)),
            UserId:         cmd.ApproverId,
            CallerType:     cmd.CallerType,
            IdempotencyKey: $"{cmd.Prid}:{PaymentPipeline.Namespace}.{PaymentPipeline.Stage.Reconcile}"), ct);

        if (!s7Response.Success)
            return new ResumePaymentResult(false, PaymentErrorCodes.ReconcileFailed,
                s7Response.Error?.Description ?? "Stage 7 reconciliation failed.",
                BankTransactionId: bankTxId,
                ErrorCode: s7Response.Error?.ErrorCode);

        await LogAuditAsync(cmd.Prid, 7, "Reconciliation", "SETTLED",
            $"Reconciled. Bank TX: {bankTxId}", cmd.ApproverId, ct);

        return new ResumePaymentResult(
            IsSuccess:        true,
            Status:           "SETTLED",
            Message:          $"Payment fully settled. Bank TX: {bankTxId}.",
            BankTransactionId: bankTxId);
    }

    private static PaymentRail ResolvePaymentRail(PayeeRecord? payee)
    {
        if (payee is null) return PaymentRail.Swift;
        if (!string.IsNullOrWhiteSpace(payee.Iban)) return PaymentRail.Sepa;
        return PaymentRail.Swift;
    }

    private static JsonElement ToJson(object obj) =>
        JsonSerializer.SerializeToElement(obj, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

    private async Task LogAuditAsync(
        Guid paymentId, int stage, string stageName,
        string outcome, string? details, string? actorId, CancellationToken ct)
    {
        var log = PaymentAuditLog.Create(
            paymentId, stage, stageName, outcome, details, actorId, DateTimeOffset.UtcNow);
        _db.Set<PaymentAuditLog>().Add(log);
        await _db.SaveChangesAsync(ct);
    }
}

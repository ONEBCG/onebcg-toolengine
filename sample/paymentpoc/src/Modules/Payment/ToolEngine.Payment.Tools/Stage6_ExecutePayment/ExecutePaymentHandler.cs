using Microsoft.EntityFrameworkCore;
using ToolEngine.Core.Abstractions.Persistence;
using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Infrastructure.Persistence;
using ToolEngine.Payment.Domain.Entities;
using ToolEngine.Payment.Domain.Enums;
using ToolEngine.Tools.Abstractions.Base;
using ToolEngine.Tools.Abstractions.Models;

namespace ToolEngine.Payment.Tools.Stage6_ExecutePayment;

// ── Input / Output ────────────────────────────────────────────────────────────

public sealed record ExecutePaymentInput(
    Guid        PaymentId,
    decimal     NetPayableAmount,
    string      Currency,
    string      PayeeLegalName,
    string?     Iban,
    string?     SwiftBic,
    string?     BankAccountNumber,
    PaymentRail PaymentRail);

public sealed record ExecutePaymentOutput(
    string         BankTransactionId,
    string         Status,
    PaymentRail    RailUsed,
    DateTimeOffset SubmittedAt,
    string         Message);

// ── Handler ───────────────────────────────────────────────────────────────────

/// <summary>
/// Stage 6 — Payment Execution (Api).
///
/// POC STUB: Returns a mock bank transaction ID and SUBMITTED_TO_BANK status.
/// Per spec safeguard: instruction is immutable once submitted to bank.
/// Any amendment requires a new instruction through the full pipeline from Stage 0.
///
/// EXPANSION PATH (when bank API is wired):
///   1. Inject ISecretVault to retrieve bank API credentials.
///   2. Construct payment message per selected rail:
///        SWIFT  → MT103 format
///        SEPA   → ISO 20022 pacs.008
///        ACH    → NACHA format
///        NEFT/RTGS → RBI API
///   3. Submit to bank API, receive acknowledgement reference.
///   4. Implement retry with exponential back-off (max 3 attempts over 4 hours).
///   5. Poll for settlement confirmation; call payment.reconcile on SETTLED event.
///   6. Trigger separate WHT remittance instruction (own PRID, own pipeline run).
/// </summary>
public sealed class ExecutePaymentHandler
    : ApiToolBase<ExecutePaymentInput, ExecutePaymentOutput>
{
    private readonly IUnitOfWork  _unitOfWork;
    private readonly AppDbContext _db;

    public ExecutePaymentHandler(
        IHttpClientFactory httpClientFactory,
        IUnitOfWork unitOfWork,
        AppDbContext db)
        : base(httpClientFactory)
    {
        _unitOfWork = unitOfWork;
        _db         = db;
    }

    public override string    Namespace => "payment";
    public override string    Name      => "execute-payment";
    public override string    Version   => "v1";
    public override ToolSchema Schema   => new(
        Description:  "Submits the payment instruction to the bank for execution. Only callable after human approval has been granted. Returns a bank transaction ID.",
        WhenToUse:    "Call only after human approval is confirmed (payment.compile-dossier returned SUSPENDED and a human approved it). Requires `paymentId`, `netPayableAmount` (from calculate-wht output), `currency`, payee bank details (`iban`, `swiftBic`, or `bankAccountNumber`), and `paymentRail`. The returned `bankTransactionId` is required by payment.reconcile.",
        WhenNotToUse: "Do not call before human approval is granted. Do not call if payment.compile-dossier has not been executed. Do not call to retry a failed bank submission without creating a new payment instruction.",
        Examples:     ["Execute the approved GBP 5000 payment to Acme Consulting", "Submit payment to bank after approval"],
        InputSchema:  BuildJsonSchema<ExecutePaymentInput>(),
        OutputSchema: BuildJsonSchema<ExecutePaymentOutput>());

    protected override async Task<ToolResponse<ExecutePaymentOutput>> HandleAsync(
        ToolRequest<ExecutePaymentInput> request, CancellationToken ct)
    {
        var inp = request.Input;

        // Validate payment is in ApprovalGranted state
        var payment = await _db.Set<PaymentInstruction>()
            .FirstOrDefaultAsync(p => p.Id == inp.PaymentId, ct);

        if (payment is null)
            return ToolResponse<ExecutePaymentOutput>.Fail(
                request.CorrelationId, ToolError.NotFound($"Payment '{inp.PaymentId}' not found."));

        if (payment.Status != PaymentStatus.ApprovalGranted)
            return ToolResponse<ExecutePaymentOutput>.Fail(
                request.CorrelationId,
                ToolError.Validation(
                    $"Cannot execute payment — status is '{payment.Status}'. " +
                    "Approval must be granted before execution (Stage 5 safeguard)."));

        // ── POC STUB — mock bank submission ───────────────────────────────────
        var bankTxId    = $"BANK-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}".ToUpperInvariant();
        var submittedAt = DateTimeOffset.UtcNow;

        // Update payment aggregate — marks as SubmittedToBank (immutable from this point)
        payment.MarkSubmittedToBank(bankTxId);
        await _unitOfWork.SaveChangesAsync(ct);

        return ToolResponse<ExecutePaymentOutput>.Ok(
            request.CorrelationId,
            new ExecutePaymentOutput(
                BankTransactionId: bankTxId,
                Status:            "SUBMITTED_TO_BANK",
                RailUsed:          inp.PaymentRail,
                SubmittedAt:       submittedAt,
                Message:           $"[STUB] Payment submitted to {inp.PaymentRail} rail. " +
                                   $"Bank TX ID: {bankTxId}. " +
                                   "Monitoring for settlement confirmation. " +
                                   "Instruction is now immutable — amendments require a new pipeline run."));
    }
}

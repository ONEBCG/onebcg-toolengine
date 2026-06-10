using ToolEngine.Core.Abstractions.Persistence;
using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Core.Domain.Enums;
using ToolEngine.Infrastructure.Persistence;
using ToolEngine.Payment.Domain.Entities;
using ToolEngine.Payment.Domain.Enums;
using ToolEngine.Tools.Abstractions.Base;
using ToolEngine.Tools.Abstractions.Models;

namespace ToolEngine.Payment.Tools.Stage0_Initiate;

// ── Input ─────────────────────────────────────────────────────────────────────

public sealed record InitiatePaymentInput(
    string      PayerName,
    string      PayerJurisdiction,
    string      PayerEntityId,
    string      PayeeRef,
    decimal     GrossAmount,
    string      Currency,
    ServiceType ServiceType,
    string      PpmId,
    string      InitiatorId);

// ── Output ────────────────────────────────────────────────────────────────────

public sealed record InitiatePaymentOutput(
    Guid   Prid,
    string Status,
    string Message);

// ── Handler ───────────────────────────────────────────────────────────────────

/// <summary>
/// Stage 0 — Payment Initiation (Database).
/// Validates input fields then creates and persists a PaymentInstruction,
/// returning its PRID. Every caller — ProcessPaymentCommandHandler, LLM agent,
/// CLI, Scenario runner — receives a real PRID they can pass directly to
/// subsequent stage tools (verify-payee, ppm-check, etc.).
/// </summary>
public sealed class InitiatePaymentHandler
    : DatabaseToolBase<InitiatePaymentInput, InitiatePaymentOutput>
{
    private readonly AppDbContext _db;

    public InitiatePaymentHandler(IUnitOfWork unitOfWork, AppDbContext db)
        : base(unitOfWork) => _db = db;

    private static readonly HashSet<string> _supportedCurrencies =
        new(StringComparer.OrdinalIgnoreCase) { "USD", "GBP", "EUR", "SGD", "AED", "CHF", "INR", "AUD" };

    private const decimal SingleTransactionLimit = 10_000_000m;

    public override string    Namespace => "payment";
    public override string    Name      => "initiate";
    public override string    Version   => "v1";
    public override ToolSchema Schema   => new(
        Description:  "Creates a PaymentInstruction record and returns a unique PRID (Payment Reference ID). The PRID is the key identifier passed as `paymentId` to every subsequent payment tool. Always the first step for any new payment.",
        WhenToUse:    "Call first for every new payment. Provide payer details, payee name, amount, currency, PPM ID, and service type. The returned `prid` field must be passed as `paymentId` to every subsequent payment tool (payment.verify-payee, payment.ppm-check, payment.calculate-wht, payment.kyc-screen, payment.compile-dossier, payment.execute-payment, payment.reconcile).",
        WhenNotToUse: "Do not call if a payment is already in progress (PRID exists). Do not call to resume a suspended payment.",
        Examples:     ["Process GBP 5000 payment to Acme Consulting", "Initiate a USD 10000 consulting payment to Horizon Advisory under PPM-002"],
        InputSchema:  BuildJsonSchema<InitiatePaymentInput>(),
        OutputSchema: BuildJsonSchema<InitiatePaymentOutput>());

    protected override async Task<ToolResponse<InitiatePaymentOutput>> HandleAsync(
        ToolRequest<InitiatePaymentInput> request, CancellationToken ct)
    {
        var inp = request.Input;

        // ── Mandatory field checks ────────────────────────────────────────────
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(inp.PayerName))         missing.Add("PayerName");
        if (string.IsNullOrWhiteSpace(inp.PayerJurisdiction)) missing.Add("PayerJurisdiction");
        if (string.IsNullOrWhiteSpace(inp.PayerEntityId))     missing.Add("PayerEntityId");
        if (string.IsNullOrWhiteSpace(inp.PayeeRef))          missing.Add("PayeeRef");
        if (string.IsNullOrWhiteSpace(inp.Currency))          missing.Add("Currency");
        if (string.IsNullOrWhiteSpace(inp.PpmId))             missing.Add("PpmId");
        if (string.IsNullOrWhiteSpace(inp.InitiatorId))       missing.Add("InitiatorId");

        if (missing.Count > 0)
            return ToolResponse<InitiatePaymentOutput>.Fail(
                request.CorrelationId,
                ToolError.Validation($"Missing mandatory fields: {string.Join(", ", missing)}."));

        // ── Currency check ───────────────────────────────────────────────────
        if (!_supportedCurrencies.Contains(inp.Currency))
            return ToolResponse<InitiatePaymentOutput>.Fail(
                request.CorrelationId,
                ToolError.Validation($"ERR_CURRENCY_UNSUPPORTED: '{inp.Currency}' is not a supported currency."));

        // ── Amount: positive, non-zero ───────────────────────────────────────
        if (inp.GrossAmount <= 0)
            return ToolResponse<InitiatePaymentOutput>.Fail(
                request.CorrelationId,
                ToolError.Validation("GrossAmount must be a positive, non-zero value."));

        // ── Amount ceiling ───────────────────────────────────────────────────
        if (inp.GrossAmount > SingleTransactionLimit)
            return ToolResponse<InitiatePaymentOutput>.Fail(
                request.CorrelationId,
                ToolError.Validation($"Amount {inp.GrossAmount} exceeds the single-transaction system limit of {SingleTransactionLimit}."));

        // ── Create PaymentInstruction ─────────────────────────────────────────
        // Persisting here makes this tool self-contained: every caller (pipeline
        // orchestrator, LLM agent, scenario runner) gets a real PRID without any
        // additional orchestration step between validation and the first DB stage.
        var instruction = PaymentInstruction.Create(
            inp.PayerName, inp.PayerJurisdiction, inp.PayerEntityId,
            inp.PayeeRef, inp.GrossAmount, inp.Currency,
            inp.ServiceType, inp.PpmId, inp.InitiatorId,
            DateTimeOffset.UtcNow);

        _db.Set<PaymentInstruction>().Add(instruction);
        await UnitOfWork.SaveChangesAsync(ct);

        return ToolResponse<InitiatePaymentOutput>.Ok(
            request.CorrelationId,
            new InitiatePaymentOutput(
                Prid:    instruction.Id,
                Status:  "PASS",
                Message: $"Payment instruction created. PRID: {instruction.Id}. Call payment.verify-payee next."));
    }
}

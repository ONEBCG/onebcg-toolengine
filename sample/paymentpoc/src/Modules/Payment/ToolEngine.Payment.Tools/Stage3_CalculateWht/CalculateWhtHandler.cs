using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Payment.Domain.Enums;
using ToolEngine.Tools.Abstractions.Base;
using ToolEngine.Tools.Abstractions.Models;

namespace ToolEngine.Payment.Tools.Stage3_CalculateWht;

// ── Input / Output ────────────────────────────────────────────────────────────

public sealed record CalculateWhtInput(
    Guid               PaymentId,
    string             PayerJurisdiction,
    string             PayeeJurisdiction,
    ServiceType        ServiceType,
    decimal            GrossAmount,
    string             Currency,
    int                TaxYear);

public sealed record CalculateWhtOutput(
    decimal            WhtRatePct,
    decimal            WhtAmount,
    decimal            NetPayableAmount,
    WhtConfidenceLevel ConfidenceLevel,
    string             Justification,
    string?            ApplicableTreaty,
    string?            TreatyArticle,
    string             ServiceClassification,
    string             Message);

// ── Handler ───────────────────────────────────────────────────────────────────

/// <summary>
/// Stage 3 — Tax Withholding Calculation (Logic).
///
/// POC STUB: Returns 0% WHT for all inputs. Net payable = Gross amount.
/// Confidence is always HIGH so the pipeline proceeds without holding.
///
/// EXPANSION PATH (when real engine is wired):
///   1. Inject IWhtRateRepository to query WhtRateEntry table.
///   2. Inject IVectorSearchService to query tax treaty corpus (Layer 1 of spec §7).
///   3. Replace the stub body below with:
///        a) Vector retrieval → applicable treaty + article identification
///        b) Rate table lookup → exact WHT rate (standard or reduced treaty rate)
///        c) Confidence scoring: HIGH / MEDIUM / REVIEW_REQUIRED
///        d) Justification narrative for audit trail
///   4. Handle REVIEW_REQUIRED by returning WhtConfidenceLevel.ReviewRequired —
///      PaymentInstruction.ApplyWhtCalculation will set status = HeldTaxReview.
///
/// The Input/Output contracts and ToolSchema are production-ready and must NOT change
/// when the real engine is plugged in — only HandleAsync body changes.
/// </summary>
public sealed class CalculateWhtHandler
    : LogicToolBase<CalculateWhtInput, CalculateWhtOutput>
{
    public override string    Namespace => "payment";
    public override string    Name      => "calculate-wht";
    public override string    Version   => "v1";
    public override ToolSchema Schema   => new(
        Description:  "Calculates withholding tax (WHT) based on payer/payee jurisdictions and service type. Returns WHT rate, WHT amount, and net payable amount.",
        WhenToUse:    "Call after payment.verify-payee. Requires `paymentId` (prid from initiate), `payerJurisdiction`, `payeeJurisdiction` (the `jurisdiction` field from verify-payee output), `serviceType`, `grossAmount`, `currency`, and `taxYear` (current year).",
        WhenNotToUse: "Do not call before payment.initiate. Do not use for general tax advice — only for payment WHT calculation.",
        Examples:     [
            "Calculate WHT on GBP 5000 consulting payment from GB to GB",
            "Determine net payable on USD 50000 cross-border payment",
        ],
        InputSchema:  BuildJsonSchema<CalculateWhtInput>(),
        OutputSchema: BuildJsonSchema<CalculateWhtOutput>());

    protected override Task<ToolResponse<CalculateWhtOutput>> HandleAsync(
        ToolRequest<CalculateWhtInput> request, CancellationToken ct)
    {
        var inp = request.Input;

        // ── POC STUB ──────────────────────────────────────────────────────────
        // Returns 0% WHT. NetPayable = GrossAmount.
        // This stub is intentionally minimal and structured for direct replacement.
        // See expansion path in XML doc comment above.

        const decimal stubWhtRatePct = 0m;
        var whtAmount      = Math.Round(inp.GrossAmount * (stubWhtRatePct / 100m), 2);
        var netPayable     = inp.GrossAmount - whtAmount;

        var output = new CalculateWhtOutput(
            WhtRatePct:           stubWhtRatePct,
            WhtAmount:            whtAmount,
            NetPayableAmount:     netPayable,
            ConfidenceLevel:      WhtConfidenceLevel.High,
            Justification:        "[STUB] WHT engine not yet wired. 0% rate applied as placeholder. " +
                                  $"Payer: {inp.PayerJurisdiction} | Payee: {inp.PayeeJurisdiction} | " +
                                  $"Service: {inp.ServiceType} | Tax Year: {inp.TaxYear}.",
            ApplicableTreaty:     null,
            TreatyArticle:        null,
            ServiceClassification: inp.ServiceType.ToString(),
            Message:              $"WHT calculated: {stubWhtRatePct}% | WHT Amount: {whtAmount} {inp.Currency} | " +
                                  $"Net Payable: {netPayable} {inp.Currency}. Proceeding to KYC screening.");

        return Task.FromResult(ToolResponse<CalculateWhtOutput>.Ok(request.CorrelationId, output));
    }
}

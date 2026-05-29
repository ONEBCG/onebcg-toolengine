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
        Description:  "Determines the applicable withholding tax (WHT) rate and computes the net disbursable amount.",
        WhenToUse:    "Call after PPM check (Stage 2). Requires payer/payee jurisdictions, service type, gross amount, and tax year.",
        WhenNotToUse: "Do not use for domestic payroll tax or VAT/GST calculations — those require different engines.",
        Examples:     [
            "Calculate WHT on USD 50000 consulting fee from GB payer to IN payee for tax year 2026",
            "Determine WHT on USD 10000 software license fee from US payer to SG payee",
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

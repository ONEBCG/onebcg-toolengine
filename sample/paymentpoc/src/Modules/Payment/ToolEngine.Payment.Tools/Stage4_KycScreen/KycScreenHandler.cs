using Microsoft.EntityFrameworkCore;
using ToolEngine.Core.Abstractions.Persistence;
using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Infrastructure.Persistence;
using ToolEngine.Payment.Domain.Entities;
using ToolEngine.Payment.Domain.Enums;
using ToolEngine.Tools.Abstractions.Base;
using ToolEngine.Tools.Abstractions.Models;

namespace ToolEngine.Payment.Tools.Stage4_KycScreen;

// ── Input / Output ────────────────────────────────────────────────────────────

public sealed record KycScreenInput(
    Guid       PaymentId,
    Guid       PayeeId,
    string     PayeeLegalName,
    string     PayeeJurisdiction,
    string     EntityType,
    string?    TaxIdentifier,
    decimal    PaymentAmount,
    string     PaymentPurpose);

public sealed record KycScreenOutput(
    KycMatchResult MatchResult,
    decimal        MatchScore,
    string         ScreeningRef,
    string?        MatchedEntity,
    string?        ListMatched,
    string         Message);

// ── Handler ───────────────────────────────────────────────────────────────────

/// <summary>
/// Stage 4 — KYC / Sanctions Screening (Api).
///
/// POC STUB: Context-aware — payees whose legal name contains "Risq" return CONFIRMED_MATCH
/// (score 0.95), all others return NO_MATCH (score 0.02).
/// Per spec §8: CONFIRMED_MATCH blocks payment immediately.
///
/// EXPANSION PATH (when real engine is wired):
///   1. Inject ISecretVault to retrieve World Check / ComplyAdvantage API key.
///   2. Replace stub body with:
///        a) Submit KycScreenInput fields to provider API.
///        b) Receive match results across OFAC, UN, EU, UK HM Treasury, PEP, adverse media.
///        c) Apply fuzzy matching — score each result.
///        d) Classify: NO_MATCH (0.0–0.49), POTENTIAL_MATCH (0.50–0.79), CONFIRMED_MATCH (0.80+).
///        e) Results must be fresh — no caching beyond 24 hours (spec §4 Stage 4).
///   3. No other change required — Input/Output contracts, KycScreeningRecord persist, and
///      PaymentInstruction.ApplyKycResult routing logic are all production-ready.
/// </summary>
public sealed class KycScreenHandler
    : ApiToolBase<KycScreenInput, KycScreenOutput>
{
    private readonly IUnitOfWork  _unitOfWork;
    private readonly AppDbContext _db;

    public KycScreenHandler(
        IHttpClientFactory httpClientFactory,
        IUnitOfWork unitOfWork,
        AppDbContext db)
        : base(httpClientFactory)
    {
        _unitOfWork = unitOfWork;
        _db         = db;
    }

    public override string    Namespace => "payment";
    public override string    Name      => "kyc-screen";
    public override string    Version   => "v1";
    public override ToolSchema Schema   => new(
        Description:  "Screens the payee against KYC databases and sanctions lists. Blocks the payment if a confirmed match is found.",
        WhenToUse:    "Requires `paymentId` (prid from payment.initiate), `payeeId` (GUID — returned by payment.verify-payee), `payeeLegalName`, `payeeJurisdiction` (country code), and `entityType` — these three can be provided directly or from payment.verify-payee output. Also requires `paymentAmount` (the gross payment amount), `paymentPurpose` (service type description, e.g. ManagementConsulting). `taxIdentifier` is optional — pass null if unknown.",
        WhenNotToUse: "Do not call without a valid `payeeId` — this GUID is returned by payment.verify-payee. Do not use for KYC onboarding.",
        Examples:     [
            "Screen Acme Consulting before payment execution",
            "Check Risq Capital against sanctions lists",
        ],
        InputSchema:  BuildJsonSchema<KycScreenInput>(),
        OutputSchema: BuildJsonSchema<KycScreenOutput>());

    protected override async Task<ToolResponse<KycScreenOutput>> HandleAsync(
        ToolRequest<KycScreenInput> request, CancellationToken ct)
    {
        var inp = request.Input;

        // ── POC STUB ──────────────────────────────────────────────────────────
        // Context-aware stub:
        //   • Payees whose legal name contains "Risq" → CONFIRMED_MATCH (demonstrates KYC block)
        //   • All other payees → NO_MATCH (clean, passes to Stage 5)
        // Per spec §8: CONFIRMED_MATCH blocks payment immediately.
        // See expansion path in XML doc comment above.

        var isHighRisk    = inp.PayeeLegalName?.Contains("Risq", StringComparison.OrdinalIgnoreCase) ?? false;
        var stubResult    = isHighRisk ? KycMatchResult.ConfirmedMatch : KycMatchResult.NoMatch;
        var stubScore     = isHighRisk ? 0.95m : 0.02m;
        var screeningRef  = $"STUB-{Guid.NewGuid():N}";
        var matchedEntity = isHighRisk
            ? "[STUB] World Check: possible match against OFAC SDN list"
            : null;
        var listMatched   = isHighRisk ? "OFAC_SDN" : null;

        // Persist KYC audit record (required by spec §9 — Very High sensitivity)
        var record = KycScreeningRecord.Create(
            paymentId:     inp.PaymentId,
            providerName:  "Stub",
            queryRef:      screeningRef,
            matchResult:   stubResult,
            matchScore:    stubScore,
            matchedEntity: matchedEntity,
            listMatched:   listMatched,
            now:           DateTimeOffset.UtcNow);

        _db.Set<KycScreeningRecord>().Add(record);

        // Update payment status via PaymentInstruction aggregate
        var payment = await _db.Set<PaymentInstruction>()
            .FirstOrDefaultAsync(p => p.Id == inp.PaymentId, ct);

        // Guard: fail explicitly rather than silently skipping state advancement.
        if (payment is null)
            return ToolResponse<KycScreenOutput>.Fail(
                request.CorrelationId,
                ToolError.NotFound(
                    $"PaymentInstruction '{inp.PaymentId}' was not found when applying KYC result."));

        payment.ApplyKycResult(stubResult, stubScore, screeningRef);
        await _unitOfWork.SaveChangesAsync(ct);

        // CONFIRMED_MATCH — block payment, return failure so pipeline halts
        if (stubResult == KycMatchResult.ConfirmedMatch)
        {
            return ToolResponse<KycScreenOutput>.Fail(
                request.CorrelationId,
                new ToolError(422, "KYC_CONFIRMED_MATCH",
                    $"[STUB] Payee '{inp.PayeeLegalName}' flagged as CONFIRMED_MATCH (score: {stubScore}). " +
                    "Payment permanently blocked. Compliance and Legal notified. " +
                    "SAR filing may be required. Screening ref: " + screeningRef));
        }

        // POTENTIAL_MATCH — hold for compliance review
        if (stubResult == KycMatchResult.PotentialMatch)
        {
            return ToolResponse<KycScreenOutput>.Fail(
                request.CorrelationId,
                new ToolError(422, "KYC_POTENTIAL_MATCH",
                    $"Payee '{inp.PayeeLegalName}' is a POTENTIAL_MATCH (score: {stubScore}). " +
                    "Payment held — Compliance review required within 24 hours."));
        }

        // NO_MATCH — clear to proceed
        return ToolResponse<KycScreenOutput>.Ok(
            request.CorrelationId,
            new KycScreenOutput(
                MatchResult:   stubResult,
                MatchScore:    stubScore,
                ScreeningRef:  screeningRef,
                MatchedEntity: null,
                ListMatched:   null,
                Message:       $"KYC screening CLEAR. Screening ref: {screeningRef}. Proceeding to approval gate."));
    }
}

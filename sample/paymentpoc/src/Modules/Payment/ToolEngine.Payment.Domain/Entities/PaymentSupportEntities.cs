using ToolEngine.Core.Domain.Common;
using ToolEngine.Payment.Domain.Enums;

namespace ToolEngine.Payment.Domain.Entities;

// ── WhtRateEntry ─────────────────────────────────────────────────────────────
// POC: all entries seeded with 0% rate (stub). Real rates added when engine is wired.
// READ-ONLY by the engine — Tax team maintains; engine cannot modify.
public sealed class WhtRateEntry : Entity<Guid>
{
    public string      PayerCountry         { get; private set; } = default!;
    public string      PayeeCountry         { get; private set; } = default!;
    public ServiceType ServiceCategory      { get; private set; }
    public string?     TreatyArticle        { get; private set; }
    public decimal     StandardRatePct      { get; private set; }
    public decimal     ReducedTreatyRatePct { get; private set; }
    public string?     ConditionsForReduced { get; private set; }
    public bool        TreatyExists         { get; private set; }
    public string      RuleVersion          { get; private set; } = default!;

    private WhtRateEntry() { }

    public static WhtRateEntry Create(
        string payerCountry, string payeeCountry, ServiceType serviceCategory,
        string? treatyArticle, decimal standardRatePct, decimal reducedTreatyRatePct,
        string? conditionsForReduced, bool treatyExists, string ruleVersion,
        DateTimeOffset now) =>
        new()
        {
            Id                   = Guid.NewGuid(),
            PayerCountry         = payerCountry.ToUpperInvariant(),
            PayeeCountry         = payeeCountry.ToUpperInvariant(),
            ServiceCategory      = serviceCategory,
            TreatyArticle        = treatyArticle,
            StandardRatePct      = standardRatePct,
            ReducedTreatyRatePct = reducedTreatyRatePct,
            ConditionsForReduced = conditionsForReduced,
            TreatyExists         = treatyExists,
            RuleVersion          = ruleVersion,
            CreatedAt            = now,
            UpdatedAt            = now,
        };
}

// ── KycScreeningRecord ───────────────────────────────────────────────────────
// Append-only KYC audit record per payment — Very High sensitivity (spec §9)
public sealed class KycScreeningRecord : Entity<Guid>
{
    public Guid          PaymentId      { get; private set; }
    public string        ProviderName   { get; private set; } = default!; // "WorldCheck" | "Stub"
    public string        QueryRef       { get; private set; } = default!; // provider's reference
    public KycMatchResult MatchResult   { get; private set; }
    public decimal       MatchScore     { get; private set; }
    public string?       MatchedEntity  { get; private set; }
    public string?       ListMatched    { get; private set; }  // "OFAC" | "UN" | etc.
    public DateTimeOffset ScreenedAt    { get; private set; }
    public string?       OfficerDecision { get; private set; } // set when Compliance reviews
    public string?       OfficerUserId  { get; private set; }

    private KycScreeningRecord() { }

    public static KycScreeningRecord Create(
        Guid paymentId, string providerName, string queryRef,
        KycMatchResult matchResult, decimal matchScore,
        string? matchedEntity, string? listMatched, DateTimeOffset now) =>
        new()
        {
            Id             = Guid.NewGuid(),
            PaymentId      = paymentId,
            ProviderName   = providerName,
            QueryRef       = queryRef,
            MatchResult    = matchResult,
            MatchScore     = matchScore,
            MatchedEntity  = matchedEntity,
            ListMatched    = listMatched,
            ScreenedAt     = now,
            CreatedAt      = now,
            UpdatedAt      = now,
        };

    public void RecordOfficerDecision(string decision, string officerUserId)
    {
        OfficerDecision = decision;
        OfficerUserId   = officerUserId;
        UpdatedAt       = DateTimeOffset.UtcNow;
    }
}

// ── PaymentAuditLog ──────────────────────────────────────────────────────────
// One entry per stage — spec §7 mandatory audit fields; 7-year retention
public sealed class PaymentAuditLog : Entity<Guid>
{
    public Guid          PaymentId    { get; private set; }
    public int           Stage        { get; private set; }
    public string        StageName    { get; private set; } = default!;
    public string        Outcome      { get; private set; } = default!;  // "PASS"|"FAIL"|"HOLD"|"BLOCKED"
    public string?       Details      { get; private set; }
    public string?       ActorId      { get; private set; }
    public DateTimeOffset EnteredAt   { get; private set; }
    public DateTimeOffset? ExitedAt   { get; private set; }

    private PaymentAuditLog() { }

    public static PaymentAuditLog Create(
        Guid paymentId, int stage, string stageName,
        string outcome, string? details, string? actorId, DateTimeOffset now) =>
        new()
        {
            Id        = Guid.NewGuid(),
            PaymentId = paymentId,
            Stage     = stage,
            StageName = stageName,
            Outcome   = outcome,
            Details   = details,
            ActorId   = actorId,
            EnteredAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };

    public void RecordExit()
    {
        ExitedAt  = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}

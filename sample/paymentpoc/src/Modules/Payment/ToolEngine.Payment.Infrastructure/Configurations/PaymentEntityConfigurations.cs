using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ToolEngine.Payment.Domain.Entities;
using ToolEngine.Payment.Domain.Enums;

namespace ToolEngine.Payment.Infrastructure.Configurations;

// ── PaymentInstruction ────────────────────────────────────────────────────────

internal sealed class PaymentInstructionConfiguration
    : IEntityTypeConfiguration<PaymentInstruction>
{
    public void Configure(EntityTypeBuilder<PaymentInstruction> b)
    {
        b.ToTable("PaymentInstructions");
        b.HasKey(p => p.Id);
        b.Property(p => p.PayerName).HasMaxLength(256).IsRequired();
        b.Property(p => p.PayerJurisdiction).HasMaxLength(8).IsRequired();    // ISO 3166-1 alpha-2/3
        b.Property(p => p.PayerEntityId).HasMaxLength(256).IsRequired();
        b.Property(p => p.PayeeRef).HasMaxLength(256).IsRequired();
        b.Property(p => p.GrossAmount).HasColumnType("decimal(18,4)").IsRequired();
        b.Property(p => p.Currency).HasMaxLength(3).IsRequired();
        b.Property(p => p.ServiceType).HasConversion<string>().HasMaxLength(64).IsRequired();
        b.Property(p => p.PpmId).HasMaxLength(128).IsRequired();
        b.Property(p => p.InitiatorId).HasMaxLength(256).IsRequired();
        b.Property(p => p.Status).HasConversion<string>().HasMaxLength(64).IsRequired();
        b.Property(p => p.CurrentStage).IsRequired().HasDefaultValue(0);

        // Stage 3 — WHT
        b.Property(p => p.WhtRate).HasColumnType("decimal(7,4)");
        b.Property(p => p.WhtAmount).HasColumnType("decimal(18,4)");
        b.Property(p => p.NetPayableAmount).HasColumnType("decimal(18,4)");
        b.Property(p => p.WhtConfidence).HasConversion<string>().HasMaxLength(32);
        b.Property(p => p.WhtJustification).HasMaxLength(2048);
        b.Property(p => p.ApplicableTreaty).HasMaxLength(512);
        b.Property(p => p.ServiceClassification).HasMaxLength(128);

        // Stage 4 — KYC
        b.Property(p => p.KycResult).HasConversion<string>().HasMaxLength(32);
        b.Property(p => p.KycMatchScore).HasColumnType("decimal(5,4)");
        b.Property(p => p.KycScreeningRef).HasMaxLength(256);

        // Stage 5 — Approval
        b.Property(p => p.ApprovalTier).HasMaxLength(64);

        // Stage 6 — Execution
        b.Property(p => p.BankTransactionId).HasMaxLength(256);

        // Audit/GDPR
        b.Property(p => p.BlockReason).HasMaxLength(1024);
        b.Property(p => p.RetainUntil).IsRequired();

        b.HasIndex(p => p.InitiatedAt);
        b.HasIndex(p => p.RetainUntil);
        b.HasIndex(p => p.VerifiedPayeeId);
    }
}

// ── PayeeRecord ───────────────────────────────────────────────────────────────

internal sealed class PayeeRecordConfiguration : IEntityTypeConfiguration<PayeeRecord>
{
    public void Configure(EntityTypeBuilder<PayeeRecord> b)
    {
        b.ToTable("PayeeRecords");
        b.HasKey(p => p.Id);
        b.Property(p => p.LegalName).HasMaxLength(512).IsRequired();
        b.Property(p => p.Jurisdiction).HasMaxLength(8).IsRequired();
        b.Property(p => p.EntityType).HasConversion<string>().HasMaxLength(32).IsRequired();
        b.Property(p => p.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        b.Property(p => p.TaxIdentifier).HasMaxLength(128);
        b.Property(p => p.RegistrationNumber).HasMaxLength(128);
        b.Property(p => p.BankAccountNumber).HasMaxLength(64);
        b.Property(p => p.Iban).HasMaxLength(64);
        b.Property(p => p.SwiftBic).HasMaxLength(16);
        b.Property(p => p.RoutingCode).HasMaxLength(64);

        b.HasIndex(p => p.LegalName);
        b.HasIndex(p => new { p.Jurisdiction, p.Status });
        b.HasIndex(p => p.TaxIdentifier);
    }
}

// ── PpmContract ───────────────────────────────────────────────────────────────

internal sealed class PpmContractConfiguration : IEntityTypeConfiguration<PpmContract>
{
    public void Configure(EntityTypeBuilder<PpmContract> b)
    {
        b.ToTable("PpmContracts");
        b.HasKey(c => c.Id);
        b.Property(c => c.PpmId).HasMaxLength(128).IsRequired();
        b.Property(c => c.PayerEntityId).HasMaxLength(256).IsRequired();
        b.Property(c => c.PermittedServiceTypes).HasMaxLength(1024).IsRequired();
        b.Property(c => c.ApprovedCurrencies).HasMaxLength(128).IsRequired();
        b.Property(c => c.MaxSingleTransaction).HasColumnType("decimal(18,4)").IsRequired();
        b.Property(c => c.AggregateCapAmount).HasColumnType("decimal(18,4)").IsRequired();
        b.Property(c => c.CumulativePaid).HasColumnType("decimal(18,4)").IsRequired().HasDefaultValue(0m);
        b.Property(c => c.ContractDocumentPath).HasMaxLength(2048);
        b.Property(c => c.ContractVersion).HasMaxLength(64).IsRequired();

        b.HasIndex(c => new { c.PpmId, c.PayeeId }).IsUnique();
        b.HasIndex(c => c.PayeeId);
        b.HasIndex(c => new { c.IsActive, c.EffectiveTo });
    }
}

// ── WhtRateEntry ──────────────────────────────────────────────────────────────

internal sealed class WhtRateEntryConfiguration : IEntityTypeConfiguration<WhtRateEntry>
{
    public void Configure(EntityTypeBuilder<WhtRateEntry> b)
    {
        b.ToTable("WhtRateEntries");
        b.HasKey(w => w.Id);
        b.Property(w => w.PayerCountry).HasMaxLength(4).IsRequired();
        b.Property(w => w.PayeeCountry).HasMaxLength(4).IsRequired();
        b.Property(w => w.ServiceCategory).HasConversion<string>().HasMaxLength(64).IsRequired();
        b.Property(w => w.TreatyArticle).HasMaxLength(256);
        b.Property(w => w.StandardRatePct).HasColumnType("decimal(7,4)").IsRequired();
        b.Property(w => w.ReducedTreatyRatePct).HasColumnType("decimal(7,4)").IsRequired();
        b.Property(w => w.ConditionsForReduced).HasMaxLength(1024);
        b.Property(w => w.RuleVersion).HasMaxLength(32).IsRequired();

        b.HasIndex(w => new { w.PayerCountry, w.PayeeCountry, w.ServiceCategory }).IsUnique();
    }
}

// ── KycScreeningRecord ────────────────────────────────────────────────────────

internal sealed class KycScreeningRecordConfiguration
    : IEntityTypeConfiguration<KycScreeningRecord>
{
    public void Configure(EntityTypeBuilder<KycScreeningRecord> b)
    {
        b.ToTable("KycScreeningRecords");
        b.HasKey(k => k.Id);
        b.Property(k => k.ProviderName).HasMaxLength(128).IsRequired();
        b.Property(k => k.QueryRef).HasMaxLength(256).IsRequired();
        b.Property(k => k.MatchResult).HasConversion<string>().HasMaxLength(32).IsRequired();
        b.Property(k => k.MatchScore).HasColumnType("decimal(5,4)").IsRequired();
        b.Property(k => k.MatchedEntity).HasMaxLength(512);
        b.Property(k => k.ListMatched).HasMaxLength(256);
        b.Property(k => k.OfficerDecision).HasMaxLength(1024);
        b.Property(k => k.OfficerUserId).HasMaxLength(256);

        b.HasIndex(k => k.PaymentId);
        b.HasIndex(k => k.MatchResult);
        b.HasIndex(k => k.ScreenedAt);
    }
}

// ── PaymentAuditLog ───────────────────────────────────────────────────────────

internal sealed class PaymentAuditLogConfiguration : IEntityTypeConfiguration<PaymentAuditLog>
{
    public void Configure(EntityTypeBuilder<PaymentAuditLog> b)
    {
        b.ToTable("PaymentAuditLogs");
        b.HasKey(l => l.Id);
        b.Property(l => l.Stage).IsRequired();
        b.Property(l => l.StageName).HasMaxLength(128).IsRequired();
        b.Property(l => l.Outcome).HasMaxLength(32).IsRequired();
        b.Property(l => l.Details).HasMaxLength(4096);
        b.Property(l => l.ActorId).HasMaxLength(256);

        b.HasIndex(l => l.PaymentId);
        b.HasIndex(l => new { l.PaymentId, l.Stage });
    }
}

namespace ToolEngine.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ToolEngine.Core.Domain.Entities;

/// <summary>
/// H1 — EF Core mapping for the append-only ToolInvocationEvent table.
///
/// The table is designed to be INSERT-only at the database level:
/// the application DB user should be granted INSERT but not UPDATE or DELETE
/// on this table. Enforced out-of-band in the deployment runbook (§4 Database Hardening).
///
/// Rationale: SOC 2 CC6.2 (logical access), NIST Cyber AI Profile (traceability),
/// EU AI Act Article 17 (logging requirements for high-risk AI systems).
/// </summary>
internal sealed class ToolInvocationEventConfiguration
    : IEntityTypeConfiguration<ToolInvocationEvent>
{
    public void Configure(EntityTypeBuilder<ToolInvocationEvent> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.TenantId).HasMaxLength(100).IsRequired();
        builder.Property(e => e.UserId).HasMaxLength(200).IsRequired();
        builder.Property(e => e.ToolNamespace).HasMaxLength(100);
        builder.Property(e => e.ToolName).HasMaxLength(200).IsRequired();
        builder.Property(e => e.ToolVersion).HasMaxLength(20).IsRequired();
        builder.Property(e => e.ErrorCode).HasMaxLength(100);
        builder.Property(e => e.ErrorMessage).HasMaxLength(1000);

        // GovernanceMetadataJson is unconstrained JSON — no max length.
        builder.Property(e => e.GovernanceMetadataJson);

        // ToolFullName is computed — not stored.
        builder.Ignore(e => e.ToolFullName);

        // Primary query patterns:
        //   - "All events for a correlation" (approval status polling, incident review)
        //   - "All events for a tenant in a time window" (SOC 2 evidence export)
        builder.HasIndex(e => e.CorrelationId);
        builder.HasIndex(e => new { e.TenantId, e.OccurredAt });

        // FK to ToolInvocationRecord — soft reference; no cascade delete.
        // The event table must survive even if ToolInvocationRecord rows are anonymised.
        builder.HasIndex(e => e.InvocationRecordId);
    }
}

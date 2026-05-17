namespace ToolEngine.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ToolEngine.Core.Domain.Entities;

internal sealed class ToolInvocationRecordConfiguration
    : IEntityTypeConfiguration<ToolInvocationRecord>
{
    public void Configure(EntityTypeBuilder<ToolInvocationRecord> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.TenantId).HasMaxLength(100).IsRequired();
        builder.Property(r => r.UserId).HasMaxLength(200).IsRequired();
        builder.Property(r => r.ToolNamespace).HasMaxLength(100);
        builder.Property(r => r.ToolName).HasMaxLength(200).IsRequired();
        builder.Property(r => r.ToolVersion).HasMaxLength(20).IsRequired();

        // ToolFullName is a computed property — not stored; exclude from EF model.
        builder.Ignore(r => r.ToolFullName);
        builder.Property(r => r.ErrorCode).HasMaxLength(100);
        builder.Property(r => r.ErrorMessage).HasMaxLength(1000);

        // H4 — CallerType: stored as int column.
        builder.Property(r => r.CallerType).IsRequired();

        // H2 — GDPR retention columns.
        builder.Property(r => r.RetainUntil).IsRequired();
        builder.Property(r => r.IsAnonymized).IsRequired();

        // H5 — ISO 42001 governance metadata. Unconstrained JSON blob; nullable.
        builder.Property(r => r.GovernanceMetadataJson);

        builder.HasIndex(r => r.CorrelationId);
        builder.HasIndex(r => new { r.TenantId, r.InvokedAt });
        // H2 — Retention sweep job queries by RetainUntil + IsAnonymized.
        builder.HasIndex(r => new { r.RetainUntil, r.IsAnonymized });

        builder.Ignore(r => r.DomainEvents);
    }
}

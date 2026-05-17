namespace ToolEngine.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ToolEngine.Core.Domain.Entities;

internal sealed class PendingApprovalConfiguration
    : IEntityTypeConfiguration<PendingApproval>
{
    public void Configure(EntityTypeBuilder<PendingApproval> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.TenantId).HasMaxLength(100).IsRequired();
        builder.Property(p => p.UserId).HasMaxLength(200).IsRequired();
        builder.Property(p => p.ToolNamespace).HasMaxLength(100).IsRequired();
        builder.Property(p => p.ToolName).HasMaxLength(200).IsRequired();
        builder.Property(p => p.ToolVersion).HasMaxLength(20).IsRequired();
        builder.Property(p => p.SerializedInput).IsRequired();
        builder.Property(p => p.ApprovalToken).HasMaxLength(64).IsRequired();
        builder.Property(p => p.OtpHash).HasMaxLength(256);
        builder.Property(p => p.ApproverEmail).HasMaxLength(320);
        builder.Property(p => p.ApprovalReason).HasMaxLength(500).IsRequired();
        builder.Property(p => p.SerializedResult);
        builder.Property(p => p.FailedOtpAttempts); // int defaults to 0 — no relational override needed

        // H3 — EU AI Act Article 14 acknowledgement JSON. Set for High/Critical risk tools only.
        builder.Property(p => p.AcknowledgementJson);

        // ToolFullName and IsExpired are computed — not stored.
        builder.Ignore(p => p.ToolFullName);
        builder.Ignore(p => p.IsExpired);
        builder.Ignore(p => p.DomainEvents);

        builder.HasIndex(p => p.ApprovalToken).IsUnique();
        builder.HasIndex(p => new { p.TenantId, p.Status });
        builder.HasIndex(p => p.CorrelationId);
    }
}

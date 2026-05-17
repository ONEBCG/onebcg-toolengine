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

        builder.HasIndex(r => r.CorrelationId);
        builder.HasIndex(r => new { r.TenantId, r.InvokedAt });

        builder.Ignore(r => r.DomainEvents);
    }
}

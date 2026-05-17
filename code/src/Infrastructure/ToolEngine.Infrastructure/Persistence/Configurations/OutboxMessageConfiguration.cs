namespace ToolEngine.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ToolEngine.Infrastructure.Persistence.Entities;

internal sealed class OutboxMessageConfiguration
    : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.HasKey(o => o.Id);
        builder.Property(o => o.TenantId).HasMaxLength(100).IsRequired();
        builder.Property(o => o.ToolFullName).HasMaxLength(300).IsRequired();
        builder.Property(o => o.ChannelType).HasMaxLength(50).IsRequired();
        builder.Property(o => o.LastError).HasMaxLength(1000);

        builder.HasIndex(o => o.PendingApprovalId);
        // Partial index pattern: dispatch service queries unsent messages.
        builder.HasIndex(o => new { o.SentAt, o.NextRetryAt });
    }
}

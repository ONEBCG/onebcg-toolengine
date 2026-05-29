using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ToolEngine.Core.Domain.Entities;

namespace ToolEngine.Infrastructure.Persistence.Configurations;

// ── ToolInvocationRecord ──────────────────────────────────────────────────────

internal sealed class ToolInvocationRecordConfiguration
    : IEntityTypeConfiguration<ToolInvocationRecord>
{
    public void Configure(EntityTypeBuilder<ToolInvocationRecord> b)
    {
        b.ToTable("ToolInvocationRecords");
        b.HasKey(r => r.Id);
        b.Property(r => r.UserId).HasMaxLength(256);
        b.Property(r => r.ToolFullName).HasMaxLength(512).IsRequired();
        b.Property(r => r.ToolVersion).HasMaxLength(32).IsRequired();
        b.Property(r => r.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        b.Property(r => r.CallerType).HasConversion<string>().HasMaxLength(32).IsRequired();
        b.Property(r => r.GovernanceMetadataJson).HasMaxLength(4096);
        b.Property(r => r.ErrorCode).HasMaxLength(128);
        b.Property(r => r.ErrorMessage).HasMaxLength(2048);
        b.Property(r => r.RetainUntil).IsRequired();
        b.Property(r => r.IsAnonymized).IsRequired().HasDefaultValue(false);

        b.HasIndex(r => new { r.ToolFullName, r.InvokedAt });
        b.HasIndex(r => r.RetainUntil);
    }
}

// ── ToolInvocationEvent ───────────────────────────────────────────────────────

internal sealed class ToolInvocationEventConfiguration
    : IEntityTypeConfiguration<ToolInvocationEvent>
{
    public void Configure(EntityTypeBuilder<ToolInvocationEvent> b)
    {
        b.ToTable("ToolInvocationEvents");
        b.HasKey(e => e.Id);
        b.Property(e => e.InvocationRecordId).IsRequired();
        b.Property(e => e.EventType).HasMaxLength(64).IsRequired();
        b.Property(e => e.OccurredAt).IsRequired();
        b.Property(e => e.CallerType).HasConversion<string>().HasMaxLength(32).IsRequired();
        b.Property(e => e.GovernanceMetadataJson).HasMaxLength(4096);

        b.HasIndex(e => e.InvocationRecordId);
        b.HasIndex(e => e.OccurredAt);
    }
}

// ── PendingApproval ───────────────────────────────────────────────────────────

internal sealed class PendingApprovalConfiguration : IEntityTypeConfiguration<PendingApproval>
{
    public void Configure(EntityTypeBuilder<PendingApproval> b)
    {
        b.ToTable("PendingApprovals");
        b.HasKey(a => a.Id);
        b.Property(a => a.ToolFullName).HasMaxLength(512).IsRequired();
        b.Property(a => a.ApprovalToken).HasMaxLength(128).IsRequired();
        b.Property(a => a.OtpHash).HasMaxLength(256);
        b.Property(a => a.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        b.Property(a => a.Risk).HasConversion<string>().HasMaxLength(32).IsRequired();
        b.Property(a => a.Channel).HasConversion<string>().HasMaxLength(64).IsRequired();
        b.Property(a => a.ExpiresAt).IsRequired();
        b.Property(a => a.FailedOtpAttempts).IsRequired().HasDefaultValue(0);
        b.Property(a => a.IdempotencyKey).HasMaxLength(512);
        b.Property(a => a.AcknowledgementJson).HasMaxLength(4096);
        b.Property(a => a.SerializedResult).HasMaxLength(8192);
        b.Property(a => a.DenialReason).HasMaxLength(1024);

        b.HasIndex(a => a.ApprovalToken).IsUnique();
        b.HasIndex(a => a.Status);
        b.HasIndex(a => a.IdempotencyKey);
        b.HasIndex(a => a.ExpiresAt);
    }
}

// ── OutboxMessage ─────────────────────────────────────────────────────────────

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> b)
    {
        b.ToTable("OutboxMessages");
        b.HasKey(m => m.Id);
        b.Property(m => m.MessageType).HasMaxLength(256).IsRequired();
        b.Property(m => m.Payload).HasMaxLength(65536).IsRequired();
        b.Property(m => m.RetryCount).IsRequired().HasDefaultValue(0);
        b.Property(m => m.Error).HasMaxLength(2048);

        b.HasIndex(m => m.SentAt);
        b.HasIndex(m => m.RetryCount);
    }
}

// ── ScenarioExecution ──────────────────────────────────────────────────────────

internal sealed class ScenarioExecutionConfiguration
    : IEntityTypeConfiguration<ScenarioExecution>
{
    public void Configure(EntityTypeBuilder<ScenarioExecution> b)
    {
        b.ToTable("ScenarioExecutions");
        b.HasKey(e => e.Id);

        b.Property(e => e.ScenarioName).HasMaxLength(256).IsRequired();
        b.Property(e => e.ScenarioVersion).HasMaxLength(32).IsRequired();
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        b.Property(e => e.SuspendedAtStepId).HasMaxLength(256);
        b.Property(e => e.FailedAtStepId).HasMaxLength(256);
        b.Property(e => e.FailureReason).HasMaxLength(2048);
        b.Property(e => e.StepContextJson).IsRequired().HasDefaultValue("{}");
        b.Property(e => e.InputJson).IsRequired().HasDefaultValue("{}");
        b.Property(e => e.InitiatedBy).HasMaxLength(256);

        b.HasIndex(e => new { e.ScenarioName, e.Status });
        b.HasIndex(e => e.CreatedAt);
        b.HasIndex(e => e.PendingApprovalId);
    }
}

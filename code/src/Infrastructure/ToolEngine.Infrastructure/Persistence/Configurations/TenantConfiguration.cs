namespace ToolEngine.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ToolEngine.Core.Domain.Entities;

internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasMaxLength(100);
        builder.Property(t => t.Name).HasMaxLength(200).IsRequired();
        builder.Property(t => t.LlmProviderOverride).HasMaxLength(100);
        builder.Property(t => t.LlmApiKeyRef).HasMaxLength(500);
        builder.Property(t => t.MaxResponseTokens);
        builder.Property(t => t.DailyToolCallBudget);
        builder.Property(t => t.CreatedBy).HasMaxLength(200).IsRequired();
        builder.Property(t => t.UpdatedBy).HasMaxLength(200);

        // AllowedTools is backed by a private List<string> field; stored as JSON.
        builder.Property<List<string>>("_allowedTools")
               .HasField("_allowedTools")
               .HasConversion(
                   v => System.Text.Json.JsonSerializer.Serialize(
                            v, (System.Text.Json.JsonSerializerOptions?)null),
                   v => System.Text.Json.JsonSerializer.Deserialize<List<string>>(
                            v, (System.Text.Json.JsonSerializerOptions?)null)
                        ?? new List<string>(),
                   new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<string>>(
                       (a, b) => a != null && b != null && a.SequenceEqual(b),
                       v => v.Aggregate(0, (h, s) => HashCode.Combine(h, s.GetHashCode())),
                       v => v.ToList()));

        // AllowedNamespaces is backed by a private List<string> field; stored as JSON.
        builder.Property<List<string>>("_allowedNamespaces")
               .HasField("_allowedNamespaces")
               .HasConversion(
                   v => System.Text.Json.JsonSerializer.Serialize(
                            v, (System.Text.Json.JsonSerializerOptions?)null),
                   v => System.Text.Json.JsonSerializer.Deserialize<List<string>>(
                            v, (System.Text.Json.JsonSerializerOptions?)null)
                        ?? new List<string>(),
                   new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<string>>(
                       (a, b) => a != null && b != null && a.SequenceEqual(b),
                       v => v.Aggregate(0, (h, s) => HashCode.Combine(h, s.GetHashCode())),
                       v => v.ToList()));

        builder.Ignore(t => t.DomainEvents);
    }
}

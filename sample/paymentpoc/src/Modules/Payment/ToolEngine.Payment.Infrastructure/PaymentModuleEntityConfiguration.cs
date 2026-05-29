using Microsoft.EntityFrameworkCore;
using ToolEngine.Infrastructure;
using ToolEngine.Payment.Infrastructure.Configurations;

namespace ToolEngine.Payment.Infrastructure;

/// <summary>
/// Registers all payment domain entity type configurations with AppDbContext.
/// Registered as Singleton by AddPaymentModule — AppDbContext receives this
/// via IEnumerable&lt;IModuleEntityConfiguration&gt; and calls Apply() during OnModelCreating.
/// </summary>
public sealed class PaymentModuleEntityConfiguration : IModuleEntityConfiguration
{
    public void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new PaymentInstructionConfiguration());
        modelBuilder.ApplyConfiguration(new PayeeRecordConfiguration());
        modelBuilder.ApplyConfiguration(new PpmContractConfiguration());
        modelBuilder.ApplyConfiguration(new WhtRateEntryConfiguration());
        modelBuilder.ApplyConfiguration(new KycScreeningRecordConfiguration());
        modelBuilder.ApplyConfiguration(new PaymentAuditLogConfiguration());
    }
}

namespace ToolEngine.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using ToolEngine.Core.Domain.Entities;
using ToolEngine.Infrastructure.Persistence.Entities;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Tenant>               Tenants               => Set<Tenant>();
    public DbSet<ToolInvocationRecord> ToolInvocationRecords => Set<ToolInvocationRecord>();
    /// <summary>
    /// H1 — Append-only SOC 2 audit event log. Application DB user has INSERT permission only.
    /// One row per lifecycle transition (Invoked → Running → Succeeded/Failed/Suspended).
    /// </summary>
    public DbSet<ToolInvocationEvent>  ToolInvocationEvents  => Set<ToolInvocationEvent>();
    public DbSet<PendingApproval>      PendingApprovals      => Set<PendingApproval>();
    /// <summary>Outbox messages pending channel dispatch. Written atomically with PendingApproval.</summary>
    public DbSet<OutboxMessage>        OutboxMessages        => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // SQLite has no native DateTimeOffset type — it stores values as TEXT and cannot
        // translate range comparisons (>=, <=, >, <) on DateTimeOffset columns.
        // This breaks DailyBudgetBehavior (InvokedAt >= startOfDayUtc) and
        // NotificationDispatchService (NextRetryAt <= now) at runtime.
        //
        // Fix: apply DateTimeOffset → long (Unix milliseconds) value converters for every
        // DateTimeOffset property on every entity when the SQLite provider is active.
        // Long values are fully sortable by SQLite so all comparison operators translate correctly.
        //
        // PostgreSQL and SQL Server have native DateTimeOffset support — converters are
        // NOT applied for those providers.
        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
            ApplySqliteDateTimeOffsetConverters(builder);

        base.OnModelCreating(builder);
    }

    /// <summary>
    /// Registers <c>DateTimeOffset → long</c> (Unix milliseconds) and
    /// <c>DateTimeOffset? → long?</c> value converters on every entity property
    /// whose CLR type is <see cref="DateTimeOffset"/> or <see cref="Nullable{DateTimeOffset}"/>.
    ///
    /// Must only be called when the active provider is SQLite
    /// (guarded in <see cref="OnModelCreating"/>).
    /// </summary>
    private static void ApplySqliteDateTimeOffsetConverters(ModelBuilder builder)
    {
        var converter = new ValueConverter<DateTimeOffset, long>(
            dto => dto.ToUnixTimeMilliseconds(),
            ms  => DateTimeOffset.FromUnixTimeMilliseconds(ms));

        var nullableConverter = new ValueConverter<DateTimeOffset?, long?>(
            dto => dto.HasValue ? dto.Value.ToUnixTimeMilliseconds() : (long?)null,
            ms  => ms.HasValue  ? DateTimeOffset.FromUnixTimeMilliseconds(ms.Value) : null);

        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTimeOffset))
                    property.SetValueConverter(converter);
                else if (property.ClrType == typeof(DateTimeOffset?))
                    property.SetValueConverter(nullableConverter);
            }
        }
    }
}

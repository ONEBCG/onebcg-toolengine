namespace ToolEngine.Infrastructure.Extensions;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using ToolEngine.Core.Abstractions.Common;
using ToolEngine.Core.Abstractions.Persistence;
using ToolEngine.Core.Abstractions.Security;
using ToolEngine.Core.Domain.Entities;
using ToolEngine.Infrastructure.Approval;
using ToolEngine.Infrastructure.BackgroundServices;
using ToolEngine.Infrastructure.Cache;
using ToolEngine.Infrastructure.Common;
using ToolEngine.Infrastructure.Persistence;
using ToolEngine.Infrastructure.Persistence.Entities;
using ToolEngine.Tools.Abstractions.Interfaces;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers infrastructure services. The composition root supplies the DB provider action:
    ///   opt.UseSqlite(...)     — development (default)
    ///   opt.UseNpgsql(...)     — PostgreSQL (production)
    ///   opt.UseSqlServer(...)  — SQL Server (production)
    ///
    /// Cache provider (ICacheProvider) must be registered by the host BEFORE this call
    /// when using Redis. If not registered, a default MemoryCacheProvider is added.
    /// </summary>
    public static IServiceCollection AddToolInfrastructure(
        this IServiceCollection         services,
        Action<DbContextOptionsBuilder> configureDb)
    {
        services.AddDbContext<AppDbContext>(configureDb);

        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // ── Write repositories ───────────────────────────────────────────────
        services.AddScoped<IRepository<Tenant, string>,
                           Repository<Tenant, string>>();
        services.AddScoped<IRepository<ToolInvocationRecord, Guid>,
                           Repository<ToolInvocationRecord, Guid>>();
        services.AddScoped<IRepository<PendingApproval, Guid>,
                           Repository<PendingApproval, Guid>>();
        services.AddScoped<IRepository<OutboxMessage, Guid>,
                           Repository<OutboxMessage, Guid>>();
        // H1 — append-only event log write repository.
        services.AddScoped<IRepository<ToolInvocationEvent, Guid>,
                           Repository<ToolInvocationEvent, Guid>>();

        // ── Read repositories ────────────────────────────────────────────────
        // F5: Tenant uses CachedTenantReadRepository (scoped per-request cache).
        // Eliminates duplicate DB reads across TenantAuth/TokenBudget/DailyBudget behaviors.
        services.AddScoped<IReadRepository<Tenant, string>,
                           CachedTenantReadRepository>();
        services.AddScoped<IReadRepository<ToolInvocationRecord, Guid>,
                           ReadRepository<ToolInvocationRecord, Guid>>();
        services.AddScoped<IReadRepository<PendingApproval, Guid>,
                           ReadRepository<PendingApproval, Guid>>();
        services.AddScoped<IReadRepository<OutboxMessage, Guid>,
                           ReadRepository<OutboxMessage, Guid>>();
        // H1 — append-only event log read repository.
        services.AddScoped<IReadRepository<ToolInvocationEvent, Guid>,
                           ReadRepository<ToolInvocationEvent, Guid>>();

        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();

        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

        // Zero Trust secret vault — dev stub. Replace with AzureKeyVaultSecretVault in prod.
        services.AddSingleton<ISecretVault, NullSecretVault>();

        // ── Cache provider fallback ──────────────────────────────────────────
        // If the host registered ICacheProvider before calling AddToolInfrastructure,
        // this block is skipped. Otherwise, MemoryCacheProvider is used (dev default).
        services.AddMemoryCache();
        if (!services.Any(d => d.ServiceType == typeof(ICacheProvider)))
            services.AddSingleton<ICacheProvider, MemoryCacheProvider>();

        // ── Approval engine ──────────────────────────────────────────────────
        services.AddOptions<ApprovalOptions>().BindConfiguration("Approval");

        // Email sender — dev stub (logs only). Replace with SendGrid/SES in production.
        services.AddSingleton<IEmailSender, LoggingEmailSender>();

        // Approval notification channels.
        services.AddSingleton<IApprovalChannel, DashboardChannel>();
        services.AddSingleton<IApprovalChannel, EmailMagicLinkChannel>();
        services.AddSingleton<IApprovalChannel, EmailOtpChannel>();
        services.AddSingleton<IApprovalChannel, WebhookChannel>();

        // Channel selector — routes by risk tier and tenant overrides.
        services.AddSingleton<ApprovalChannelSelector>();

        // HTTP client used by WebhookChannel.
        services.AddHttpClient("approval-webhook");

        // API approval gate — async, DB-persisted, outbox-notified.
        // CLI overrides this with ConsoleApprovalGate in Cli/Program.cs.
        services.AddScoped<IHumanApprovalGate, AsyncApprovalGate>();

        // ── Background services ──────────────────────────────────────────────
        // F7: Outbox dispatch — processes OutboxMessages and delivers channel notifications.
        services.AddHostedService<NotificationDispatchService>();

        return services;
    }

    /// <summary>
    /// Registers the Redis-backed ICacheProvider. Call this BEFORE AddToolInfrastructure
    /// when "Cache:Provider" = "redis", after registering IDistributedCache (Redis).
    /// The Infrastructure fallback in AddToolInfrastructure will then skip MemoryCacheProvider.
    /// </summary>
    public static IServiceCollection AddDistributedCacheProvider(
        this IServiceCollection services)
    {
        services.AddSingleton<ICacheProvider, DistributedCacheProvider>();
        return services;
    }
}

/// <summary>
/// Development-only ISecretVault stub.
/// Returns placeholder credentials that are valid for 8 hours.
/// Replace with Azure Key Vault, HashiCorp Vault, or AWS Secrets Manager in production.
/// </summary>
internal sealed class NullSecretVault : ISecretVault
{
    public Task<Secret> GetSecretAsync(
        string toolNamespace, string toolName, string secretName,
        Guid correlationId, CancellationToken ct = default) =>
        Task.FromResult(new Secret(
            Value:         "dev-placeholder",
            ExpiresAt:     DateTimeOffset.UtcNow.AddHours(8),
            SecretName:    secretName,
            CorrelationId: correlationId));

    public Task RevokeAsync(Guid correlationId, CancellationToken ct = default) =>
        Task.CompletedTask;
}

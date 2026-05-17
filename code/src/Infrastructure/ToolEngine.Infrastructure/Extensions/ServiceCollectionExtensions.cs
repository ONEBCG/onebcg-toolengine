namespace ToolEngine.Infrastructure.Extensions;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ToolEngine.Core.Abstractions.Common;
using ToolEngine.Core.Abstractions.Persistence;
using ToolEngine.Core.Abstractions.Security;
using ToolEngine.Core.Domain.Entities;
using ToolEngine.Infrastructure.Common;
using ToolEngine.Infrastructure.Persistence;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers infrastructure services. Pass a provider-specific configuration
    /// action from the composition root, e.g. opt.UseSqlServer(...) or opt.UseNpgsql(...).
    /// Infrastructure itself has no provider dependency.
    /// </summary>
    public static IServiceCollection AddToolInfrastructure(
        this IServiceCollection         services,
        Action<DbContextOptionsBuilder> configureDb)
    {
        services.AddDbContext<AppDbContext>(configureDb);

        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddScoped<IRepository<Tenant, string>,
                           Repository<Tenant, string>>();
        services.AddScoped<IRepository<ToolInvocationRecord, Guid>,
                           Repository<ToolInvocationRecord, Guid>>();

        services.AddScoped<IReadRepository<Tenant, string>,
                           ReadRepository<Tenant, string>>();
        services.AddScoped<IReadRepository<ToolInvocationRecord, Guid>,
                           ReadRepository<ToolInvocationRecord, Guid>>();

        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();

        services.AddSingleton<Microsoft.AspNetCore.Http.IHttpContextAccessor,
                              Microsoft.AspNetCore.Http.HttpContextAccessor>();
        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

        // Zero Trust secret vault — dev stub. Replace with AzureKeyVaultSecretVault in prod.
        services.AddSingleton<ISecretVault, NullSecretVault>();

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

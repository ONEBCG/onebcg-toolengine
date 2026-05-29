using Microsoft.Extensions.DependencyInjection;
using ToolEngine.Infrastructure;
using ToolEngine.Payment.Infrastructure.Seeders;

namespace ToolEngine.Payment.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers payment infrastructure services:
    ///   - PaymentModuleEntityConfiguration → IModuleEntityConfiguration (Singleton)
    ///   - PaymentModuleSeeder → IModuleSeeder (Transient)
    /// Called internally by Payment.Api's AddPaymentModule extension.
    /// </summary>
    public static IServiceCollection AddPaymentInfrastructure(
        this IServiceCollection services)
    {
        services.AddSingleton<IModuleEntityConfiguration, PaymentModuleEntityConfiguration>();
        services.AddTransient<IModuleSeeder, PaymentModuleSeeder>();
        return services;
    }
}

using MediatR;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using ToolEngine.Infrastructure;
using ToolEngine.Payment.Application.Commands;
using ToolEngine.Payment.Application.Extensions;
using ToolEngine.Payment.Infrastructure;
using ToolEngine.Payment.Infrastructure.Seeders;
using ToolEngine.Payment.Infrastructure.Extensions;
using ToolEngine.Payment.Tools.Extensions;
using ToolEngine.Payment.Tools.Scenarios;
using ToolEngine.Tools.Abstractions.Interfaces;

namespace ToolEngine.Payment.Api.Extensions;

/// <summary>
/// Payment module entry point.
/// Call builder.Services.AddPaymentModule() in the host Program.cs to register
/// all payment services, tools, scenarios, MediatR handlers, and EF configurations.
/// Then call await RegisterPaymentModuleAsync(app.Services, registry) after build.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPaymentModule(
        this IServiceCollection services)
    {
        // ── Tool handlers (8 payment pipeline stages) ─────────────────────────
        services.AddPaymentTools();

        // ── Scenario definitions ───────────────────────────────────────────────
        services.AddTransient<PaymentComplianceScenario>();
        services.AddTransient<PaymentExpiredPpmScenario>();
        services.AddTransient<PaymentKycBlockScenario>();
        services.AddTransient<PaymentOverLimitScenario>();

        // ── Application layer (MediatR handlers + validators) ─────────────────
        services.AddPaymentApplicationServices();

        // ── Infrastructure — EF entity configurations + seeder ─────────────────
        services.AddPaymentInfrastructure();

        return services;
    }

    /// <summary>
    /// Registers tool descriptors and scenarios into the engine registries.
    /// Must be called AFTER the DI container is built (uses CreateScope internally).
    /// </summary>
    public static async Task RegisterPaymentModuleAsync(
        IServiceProvider services,
        IScenarioRegistry scenarioRegistry)
    {
        await ToolEngine.Payment.Tools.Extensions.ServiceCollectionExtensions
            .RegisterPaymentToolDescriptors(services);

        ToolEngine.Payment.Tools.Extensions.ServiceCollectionExtensions
            .RegisterPaymentScenarios(services, scenarioRegistry);
    }
}

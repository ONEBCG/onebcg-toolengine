using Microsoft.Extensions.DependencyInjection;
using ToolEngine.Payment.Tools.Scenarios;
using ToolEngine.Payment.Tools.Stage0_Initiate;
using ToolEngine.Payment.Tools.Stage1_VerifyPayee;
using ToolEngine.Payment.Tools.Stage2_PpmCheck;
using ToolEngine.Payment.Tools.Stage3_CalculateWht;
using ToolEngine.Payment.Tools.Stage4_KycScreen;
using ToolEngine.Payment.Tools.Stage5_CompileDossier;
using ToolEngine.Payment.Tools.Stage6_ExecutePayment;
using ToolEngine.Payment.Tools.Stage7_Reconcile;
using ToolEngine.Tools.Abstractions.Interfaces;
using ToolEngine.Tools.Abstractions.Models;

namespace ToolEngine.Payment.Tools.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all 8 payment pipeline tool handlers as Transient.
    /// Transient is mandatory — handlers depend on scoped services (IUnitOfWork, AppDbContext).
    /// Singleton or Scoped would cause captive dependency issues (Phase 2 / M1 constraint).
    /// Also self-registers each handler's ToolDescriptor into IToolRegistry.
    /// </summary>
    public static IServiceCollection AddPaymentTools(this IServiceCollection services)
    {
        // Register handlers as Transient
        services.AddTransient<InitiatePaymentHandler>();
        services.AddTransient<VerifyPayeeHandler>();
        services.AddTransient<PpmCheckHandler>();
        services.AddTransient<CalculateWhtHandler>();
        services.AddTransient<KycScreenHandler>();
        services.AddTransient<CompileApprovalDossierHandler>();
        services.AddTransient<ExecutePaymentHandler>();
        services.AddTransient<ReconcilePaymentHandler>();

        // Scenario definitions (Transient — each resolve gets a fresh instance)
        services.AddTransient<PaymentComplianceScenario>();
        services.AddTransient<PaymentExpiredPpmScenario>();
        services.AddTransient<PaymentKycBlockScenario>();
        services.AddTransient<PaymentOverLimitScenario>();

        return services;
    }

    /// <summary>
    /// Populates IToolRegistry with all payment tool descriptors.
    /// Must be called after the DI container is built (using IServiceProvider).
    /// </summary>
    public static async Task RegisterPaymentToolDescriptors(IServiceProvider provider)
    {
        var registry = provider.GetRequiredService<IToolRegistry>();

        await RegisterToolAsync<InitiatePaymentHandler>(provider, registry, Core.Domain.Enums.ToolType.Database);
        await RegisterToolAsync<VerifyPayeeHandler>(provider, registry, Core.Domain.Enums.ToolType.Database);
        await RegisterToolAsync<PpmCheckHandler>(provider, registry, Core.Domain.Enums.ToolType.Database);
        await RegisterToolAsync<CalculateWhtHandler>(provider, registry, Core.Domain.Enums.ToolType.Logic);
        await RegisterToolAsync<KycScreenHandler>(provider, registry, Core.Domain.Enums.ToolType.Api);
        await RegisterToolAsync<CompileApprovalDossierHandler>(provider, registry, Core.Domain.Enums.ToolType.Composite);
        await RegisterToolAsync<ExecutePaymentHandler>(provider, registry, Core.Domain.Enums.ToolType.Api);
        await RegisterToolAsync<ReconcilePaymentHandler>(provider, registry, Core.Domain.Enums.ToolType.Database);
    }

    /// <summary>
    /// Registers all payment scenario definitions into IScenarioRegistry.
    /// Must be called after the DI container is built — same pattern as
    /// RegisterPaymentToolDescriptors. Call from Program.cs startup.
    /// </summary>
    public static void RegisterPaymentScenarios(
        IServiceProvider provider,
        IScenarioRegistry registry)
    {
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;

        registry.Register(sp.GetRequiredService<PaymentComplianceScenario>());
        registry.Register(sp.GetRequiredService<PaymentExpiredPpmScenario>());
        registry.Register(sp.GetRequiredService<PaymentKycBlockScenario>());
        registry.Register(sp.GetRequiredService<PaymentOverLimitScenario>());
    }

    private static void RegisterTool<THandler>(
        IServiceProvider provider, IToolRegistry registry, Core.Domain.Enums.ToolType toolType)
        where THandler : ToolEngine.Tools.Abstractions.Base.ToolHandlerBase
    {
        using var scope   = provider.CreateScope();
        var       handler = scope.ServiceProvider.GetRequiredService<THandler>();
        registry.Register(handler.ToDescriptor(toolType, typeof(THandler)));
    }

    private static async Task RegisterToolAsync<THandler>(
        IServiceProvider provider, IToolRegistry registry, Core.Domain.Enums.ToolType toolType)
        where THandler : ToolEngine.Tools.Abstractions.Base.ToolHandlerBase
    {
        await using var scope   = provider.CreateAsyncScope();   // <-- async scope
        var             handler = scope.ServiceProvider.GetRequiredService<THandler>();
        registry.Register(handler.ToDescriptor(toolType, typeof(THandler)));
    }
}

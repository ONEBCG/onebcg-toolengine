using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using ToolEngine.Payment.Application.Commands;
using ToolEngine.Tools.Abstractions.Interfaces;

namespace ToolEngine.Payment.Application.Extensions;

/// <summary>
/// Registers Payment Application layer services:
///   - MediatR handlers (ProcessPaymentCommand, ResumePaymentCommand, queries)
///   - FluentValidation validators from this assembly
/// Called internally by Payment.Api's AddPaymentModule — not called directly by the host.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPaymentApplicationServices(
        this IServiceCollection services)
    {
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(ProcessPaymentCommand).Assembly));

        services.AddValidatorsFromAssembly(
            typeof(ProcessPaymentCommand).Assembly,
            includeInternalTypes: true);

        // Resume tools — live in Application (need ISender for payment.resume)
        services.AddTransient<ResumeVerifyTool>();
        services.AddTransient<ResumePaymentTool>();

        return services;
    }

    /// <summary>
    /// Registers payment resume tool descriptors into IToolRegistry.
    /// Called from Payment.Api after the DI container is built — same pattern
    /// as RegisterPaymentToolDescriptors in Payment.Tools.
    /// </summary>
    public static async Task RegisterResumeToolsAsync(
        IServiceProvider provider,
        IToolRegistry    registry)
    {
        await RegisterToolAsync<ResumeVerifyTool>(provider, registry,
            ToolEngine.Core.Domain.Enums.ToolType.Database);

        await RegisterToolAsync<ResumePaymentTool>(provider, registry,
            ToolEngine.Core.Domain.Enums.ToolType.Database);
    }

    private static async Task RegisterToolAsync<THandler>(
        IServiceProvider provider,
        IToolRegistry    registry,
        ToolEngine.Core.Domain.Enums.ToolType toolType)
        where THandler : ToolEngine.Tools.Abstractions.Base.ToolHandlerBase
    {
        await using var scope   = provider.CreateAsyncScope();
        var             handler = scope.ServiceProvider.GetRequiredService<THandler>();
        registry.Register(handler.ToDescriptor(toolType, typeof(THandler)));
    }
}

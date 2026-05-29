using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using ToolEngine.Payment.Application.Commands;

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

        return services;
    }
}

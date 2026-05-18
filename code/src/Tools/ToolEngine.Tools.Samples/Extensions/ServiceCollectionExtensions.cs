namespace ToolEngine.Tools.Samples.Extensions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ToolEngine.Tools.Registry;
using ToolEngine.Tools.Samples.Api.Weather;
using ToolEngine.Tools.Samples.Composite.WeatherReport;
using ToolEngine.Tools.Samples.Database.UserLookup;
using ToolEngine.Tools.Samples.Logic.Calculator;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all sample tools with the DI container and the ToolRegistry.
    /// Call after AddToolRegistry() in Program.cs.
    /// </summary>
    public static IServiceCollection AddToolSamples(
        this IServiceCollection services)
    {
        services.AddTransient<CalculatorTool>();

        // Named client matches tool FullName ("weather.current") for Polly policy wiring in Phase D
        services.AddHttpClient("weather.current", c =>
        {
            c.BaseAddress = new Uri("https://wttr.in");
            c.DefaultRequestHeaders.Add("Accept", "application/json");
            c.Timeout = TimeSpan.FromSeconds(10);
        });
        services.AddTransient<WeatherTool>();

        services.AddSingleton<
            ToolEngine.Core.Abstractions.Persistence.IReadRepository<User, Guid>,
            InMemoryUserRepository>();
        services.AddTransient<UserLookupTool>();

        services.AddTransient<WeatherReportTool>();

        // Register tools against the real IToolRegistry singleton via a hosted service.
        // Using BuildServiceProvider() here would create a second container whose registry
        // instance is never the one the app uses — all registrations would be lost.
        services.AddHostedService<ToolSampleRegistrationService>();

        return services;
    }
}

internal sealed class ToolSampleRegistrationService : IHostedService
{
    private readonly IToolRegistry _registry;

    public ToolSampleRegistrationService(IToolRegistry registry) =>
        _registry = registry;

    public Task StartAsync(CancellationToken ct)
    {
        // FullName (namespace.name) is auto-derived from each tool's Namespace + Name properties.
        // math.calculate | weather.current | hr.user-lookup | weather.report
        _registry.Register<CalculatorTool>   ("v1");
        _registry.Register<WeatherTool>      ("v1");
        _registry.Register<UserLookupTool>   ("v1");
        _registry.Register<WeatherReportTool>("v1");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}

/// <summary>
/// In-memory stub repository for the UserLookupTool sample.
/// Replace with EF Core or MongoDB repository in production.
/// </summary>
internal sealed class InMemoryUserRepository
    : ToolEngine.Core.Abstractions.Persistence.IReadRepository<User, Guid>
{
    private static readonly List<User> _seed =
    [
        new(Guid.Parse("11111111-0000-0000-0000-000000000001"),
            "alice@onebcg-default-tenant.com", "Alice Smith", "onebcg-default-tenant", true),
        new(Guid.Parse("22222222-0000-0000-0000-000000000002"),
            "bob@onebcg-default-tenant.com", "Bob Jones", "onebcg-default-tenant", true),
    ];

    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_seed.FirstOrDefault(u => u.Id == id));

    public Task<IReadOnlyList<User>> ListAllAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<User>>(_seed);

    public Task<IReadOnlyList<User>> ListAsync(
        ToolEngine.Core.Abstractions.Persistence.ISpecification<User> spec,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<User>>(
            _seed.Where(u => spec.Criteria.Compile()(u)).ToList());

    public Task<int> CountAsync(
        ToolEngine.Core.Abstractions.Persistence.ISpecification<User> spec,
        CancellationToken ct = default) =>
        Task.FromResult(_seed.Count(u => spec.Criteria.Compile()(u)));

    public Task<ToolEngine.Core.Abstractions.Persistence.PagedResult<User>> PagedListAsync(
        ToolEngine.Core.Abstractions.Persistence.ISpecification<User> spec,
        int pageNumber, int pageSize,
        CancellationToken ct = default)
    {
        if (pageNumber < 1) pageNumber = 1;
        if (pageSize   < 1) pageSize   = 10;
        var filtered = _seed.Where(u => spec.Criteria.Compile()(u)).ToList();
        var items    = filtered.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList();
        return Task.FromResult(
            new ToolEngine.Core.Abstractions.Persistence.PagedResult<User>(
                items, filtered.Count, pageNumber, pageSize));
    }
}

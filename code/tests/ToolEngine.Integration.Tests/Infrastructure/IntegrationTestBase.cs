namespace ToolEngine.Integration.Tests.Infrastructure;

using System.Text.Json;
using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using ToolEngine.Application.Behaviors;
using ToolEngine.Application.Commands;
using ToolEngine.Core.Abstractions.Common;
using ToolEngine.Core.Abstractions.Persistence;
using ToolEngine.Core.Domain.Common;
using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Core.Domain.Entities;
using ToolEngine.Core.Domain.Enums;
using ToolEngine.Infrastructure.Cache;
using ToolEngine.Infrastructure.Common;
using ToolEngine.Infrastructure.Persistence;
using ToolEngine.Tools.Abstractions.Interfaces;

/// <summary>
/// Shared base for all integration tests. Wires a real EF Core DbContext backed by
/// SQLite in-memory, the full MediatR pipeline with all production behaviors, and
/// NSubstitute mocks for the external seams (IHumanApprovalGate, IToolDiscovery,
/// ICurrentUser). Each test class gets an isolated in-memory database.
/// </summary>
public abstract class IntegrationTestBase : IAsyncLifetime
{
    // Keep the connection open for the test lifetime so SQLite in-memory DB persists.
    private readonly SqliteConnection _connection;
    private ServiceProvider           _provider = null!;
    private IServiceScope             _scope    = null!;

    protected IMediator          Mediator       { get; private set; } = null!;
    protected AppDbContext        Db             { get; private set; } = null!;
    protected IHumanApprovalGate  GateMock       { get; private set; } = null!;
    protected IToolDiscovery      DiscoveryMock  { get; private set; } = null!;
    protected IServiceProvider    Services       => _scope.ServiceProvider;

    protected IntegrationTestBase()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    public async Task InitializeAsync()
    {
        GateMock      = Substitute.For<IHumanApprovalGate>();
        DiscoveryMock = Substitute.For<IToolDiscovery>();

        // Default stubs — individual tests override as needed.
        GateMock
            .RequestApprovalAsync(Arg.Any<ApprovalContext>(), Arg.Any<string>(),
                Arg.Any<ApprovalRisk>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(ApprovalDecision.Allow("test"));

        DiscoveryMock
            .Resolve(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(Result.Failure<ToolDiscoveryDescriptor>(
                Error.NotFound("tool", "1.0")));

        var services = new ServiceCollection();
        ConfigureServices(services);
        _provider = services.BuildServiceProvider();
        _scope    = _provider.CreateScope();

        Mediator = _scope.ServiceProvider.GetRequiredService<IMediator>();
        Db       = _scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await Db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        // IServiceScope only implements IAsyncDisposable on its underlying implementation;
        // always prefer async disposal to avoid the InvalidOperationException thrown when
        // a registered service (e.g. UnitOfWork) only implements IAsyncDisposable.
        if (_scope is IAsyncDisposable asyncScope)
            await asyncScope.DisposeAsync();
        else
            _scope.Dispose();

        await _provider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    // ── Service registration ──────────────────────────────────────────────────

    private void ConfigureServices(IServiceCollection services)
    {
        // EF Core — SQLite in-memory via the kept-open connection.
        // Register using a factory so the concrete type is TestAppDbContext (which adds
        // DateTimeOffset → long value converters), while the DI service type remains
        // AppDbContext. This makes DateTimeOffset range comparisons translatable by the
        // SQLite EF Core provider (DailyBudgetBehavior: InvokedAt >= startOfDayUtc).
        services.AddDbContext<AppDbContext>(opts =>
            opts.UseSqlite(_connection), ServiceLifetime.Scoped);
        // Override the registration so the concrete instance is TestAppDbContext.
        services.AddScoped<AppDbContext>(sp =>
        {
            var opts = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(_connection)
                .Options;
            return new TestAppDbContext(opts);
        });

        // Persistence
        services.AddScoped<IUnitOfWork,                                  UnitOfWork>();
        services.AddScoped<IRepository<Tenant, string>,                  Repository<Tenant, string>>();
        services.AddScoped<IRepository<ToolInvocationRecord, Guid>,      Repository<ToolInvocationRecord, Guid>>();
        services.AddScoped<IRepository<ToolInvocationEvent, Guid>,       Repository<ToolInvocationEvent, Guid>>();
        services.AddScoped<IRepository<PendingApproval, Guid>,           Repository<PendingApproval, Guid>>();
        services.AddScoped<IReadRepository<Tenant, string>,              CachedTenantReadRepository>();
        services.AddScoped<IReadRepository<ToolInvocationRecord, Guid>,  ReadRepository<ToolInvocationRecord, Guid>>();
        services.AddScoped<IReadRepository<ToolInvocationEvent, Guid>,   ReadRepository<ToolInvocationEvent, Guid>>();
        services.AddScoped<IReadRepository<PendingApproval, Guid>,       ReadRepository<PendingApproval, Guid>>();

        // Infrastructure services
        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
        services.AddMemoryCache();
        services.AddSingleton<ICacheProvider, MemoryCacheProvider>();

        // Identity mock — no HttpContext in tests
        services.AddSingleton(Substitute.For<ICurrentUser>());

        // External seam mocks
        services.AddSingleton(GateMock);
        services.AddSingleton(DiscoveryMock);

        // MediatR — scan this assembly for StubToolHandler
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(IntegrationTestBase).Assembly));

        // Override the generic handler with StubToolHandler
        services.AddTransient<
            IRequestHandler<ExecuteToolCommand<JsonElement, JsonElement>, ToolResponse<JsonElement>>,
            StubToolHandler>();

        // Pipeline behaviors — order matters (registered outermost → innermost):
        // TenantAuth → Validation → TokenBudget → DailyBudget → LoopDetection → Approval → Audit
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TenantAuthorizationBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TokenBudgetBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(DailyBudgetBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoopDetectionBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ApprovalBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(AuditBehavior<,>));

        // Options
        services.AddOptions<LoopDetectionOptions>()
            .Configure(o => o.MaxCallsPerCorrelation = 10);
    }

    // ── Seed helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Seeds a Tenant with the given configuration and saves it to the database.
    /// Returns the persisted Tenant.
    /// </summary>
    protected async Task<Tenant> SeedTenantAsync(
        string    id                = "acme",
        int       dailyBudget       = 10_000,
        int       maxTokens         = 25_000,
        string[]? allowedNamespaces = null)
    {
        var clock  = Services.GetRequiredService<IDateTimeProvider>();
        var result = Tenant.Create(id, $"{id} Corp", "seed", clock);
        result.IsFailure.Should().BeFalse(because: $"Tenant.Create should succeed for id '{id}'");

        var tenant = result.Value;
        tenant.SetLimits(maxTokens, dailyBudget);

        var namespaces = allowedNamespaces ?? ["*"];
        foreach (var ns in namespaces)
            tenant.AllowNamespace(ns);

        var repo = Services.GetRequiredService<IRepository<Tenant, string>>();
        var uow  = Services.GetRequiredService<IUnitOfWork>();
        await repo.AddAsync(tenant);
        await uow.SaveChangesAsync();

        return tenant;
    }

    /// <summary>
    /// Builds a default <see cref="ExecuteToolCommand{TIn,TOut}"/> for use in tests.
    /// All parameters have sensible defaults so tests only specify what they care about.
    /// </summary>
    protected static ExecuteToolCommand<JsonElement, JsonElement> BuildCommand(
        string     tenantId           = "acme",
        string     toolNamespace      = "math",
        string     toolName           = "calculate",
        Guid?      correlationId      = null,
        CallerType callerType         = CallerType.Human,
        string?    governanceMetadata = null,
        string?    idempotencyKey     = null) =>
        new(
            CorrelationId:         correlationId ?? Guid.NewGuid(),
            TenantId:              tenantId,
            UserId:                "user-test",
            ToolName:              toolName,
            ToolVersion:           "1.0",
            Input:                 JsonDocument.Parse("{}").RootElement,
            ToolType:              ToolType.Logic,
            ToolNamespace:         toolNamespace,
            MaxResponseTokens:     1_000,
            IdempotencyKey:        idempotencyKey,
            CallerType:            callerType,
            GovernanceMetadataJson: governanceMetadata);
}

// ── Stub handler ─────────────────────────────────────────────────────────────

/// <summary>
/// Minimal MediatR handler that returns a canned success response.
/// Registered last so it is overridden by any test-specific handler registrations.
/// </summary>
internal sealed class StubToolHandler
    : IRequestHandler<ExecuteToolCommand<JsonElement, JsonElement>, ToolResponse<JsonElement>>
{
    public Task<ToolResponse<JsonElement>> Handle(
        ExecuteToolCommand<JsonElement, JsonElement> request,
        CancellationToken                           ct) =>
        Task.FromResult(
            ToolResponse<JsonElement>.Ok(
                request.CorrelationId,
                JsonDocument.Parse("{\"stub\":true}").RootElement));
}

// ── Test DbContext ────────────────────────────────────────────────────────────

/// <summary>
/// AppDbContext subclass used exclusively in integration tests.
///
/// The SQLite <c>DateTimeOffset → long</c> value converters that were previously
/// applied here have been promoted into <see cref="AppDbContext.OnModelCreating"/>
/// behind a provider guard (<c>Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite"</c>).
///
/// Moving them to the base context fixes the production runtime bugs in
/// <c>NotificationDispatchService</c> (<c>NextRetryAt &lt;= now</c>) and
/// <c>DailyBudgetBehavior</c> (<c>InvokedAt &gt;= startOfDayUtc</c>) where SQLite
/// could not translate <c>DateTimeOffset</c> range comparisons stored as TEXT.
/// </summary>
internal sealed class TestAppDbContext : AppDbContext
{
    public TestAppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
    // No OnModelCreating override needed — base applies SQLite converters automatically.
}

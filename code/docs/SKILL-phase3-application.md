---
name: toolengine-phase3-application
description: >
  Scaffolds Phase 3 of ToolEngine v2026: ToolExecutor (IServiceScopeFactory scoped
  execution with CreateAsyncScope + await using), the full 8-behavior MediatR pipeline
  in correct authorization-first order, and Infrastructure (EF Core provider-agnostic
  repositories, UnitOfWork implementing IAsyncDisposable only, AppDbContext with all
  entities, outbox pattern, idempotency, deny-by-default namespace enforcement).
classification: Confidential - Internal Use Only
---

# Phase 3 — Executor, Application Layer + Infrastructure

## Prerequisites

Phases 1 and 2 complete. `dotnet build` passes with zero warnings on:
- `ToolEngine.Core.Abstractions`
- `ToolEngine.Core.Domain`
- `ToolEngine.Tools.Abstractions`
- `ToolEngine.Tools.Registry`
- `ToolEngine.Tools.Samples`

---

## What this phase produces

```
src/
  Tools/
    ToolEngine.Tools.Executor/     ← IToolExecutor + ToolPlanExecutor
  Application/
    ToolEngine.Application/        ← MediatR commands, 8 pipeline behaviors, handlers
  Infrastructure/
    ToolEngine.Infrastructure/     ← EF Core, repositories, approval gate, outbox, cache
```

---

## Step-by-step scaffold

```bash
dotnet new classlib -n ToolEngine.Tools.Executor \
  -o src/Tools/ToolEngine.Tools.Executor --framework net8.0
dotnet new classlib -n ToolEngine.Application \
  -o src/Application/ToolEngine.Application --framework net8.0
dotnet new classlib -n ToolEngine.Infrastructure \
  -o src/Infrastructure/ToolEngine.Infrastructure --framework net8.0

dotnet sln add src/Tools/ToolEngine.Tools.Executor
dotnet sln add src/Application/ToolEngine.Application
dotnet sln add src/Infrastructure/ToolEngine.Infrastructure

dotnet add src/Tools/ToolEngine.Tools.Executor reference \
  src/Tools/ToolEngine.Tools.Registry \
  src/Core/ToolEngine.Core.Domain
dotnet add src/Application/ToolEngine.Application reference \
  src/Tools/ToolEngine.Tools.Abstractions \
  src/Core/ToolEngine.Core.Domain \
  src/Core/ToolEngine.Core.Abstractions
dotnet add src/Infrastructure/ToolEngine.Infrastructure reference \
  src/Core/ToolEngine.Core.Domain \
  src/Core/ToolEngine.Core.Abstractions
```

---

## NuGet packages

**ToolEngine.Tools.Executor:**
```xml
<PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="8.*" />
```

**ToolEngine.Application:**
```xml
<PackageReference Include="MediatR" Version="12.*" />
<PackageReference Include="FluentValidation" Version="11.*" />
<PackageReference Include="OpenTelemetry" Version="1.*" />
```

**ToolEngine.Infrastructure:**
```xml
<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="8.*" />
<PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="8.*" />
<PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="8.*" />
<PackageReference Include="Microsoft.Extensions.Caching.Memory" Version="8.*" />
<PackageReference Include="Microsoft.Extensions.Caching.StackExchangeRedis" Version="8.*" />
<PackageReference Include="Microsoft.Extensions.Http" Version="8.*" />
```

---

## ToolExecutor — CRITICAL: IServiceScopeFactory pattern

**File:** `src/Tools/ToolEngine.Tools.Executor/ToolExecutor.cs`

### Why IServiceScopeFactory (not IServiceProvider)

Tool handlers may depend on scoped services (`IUnitOfWork`, `AppDbContext`). The root
`IServiceProvider` cannot resolve scoped services — it throws `InvalidOperationException`.
`UnitOfWork` implements only `IAsyncDisposable` (not `IDisposable`), so the scope MUST
be `await using` with `CreateAsyncScope()`. Using synchronous `using` with `CreateScope()`
calls `Dispose()` which throws for `IAsyncDisposable`-only types.

```csharp
namespace ToolEngine.Tools.Executor;

public sealed class ToolExecutor : IToolExecutor
{
    private readonly IToolRegistry       _registry;
    private readonly IServiceScopeFactory _scopeFactory;  // NEVER IServiceProvider

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public ToolExecutor(IToolRegistry registry, IServiceScopeFactory scopeFactory)
    {
        _registry     = registry;
        _scopeFactory = scopeFactory;
    }

    public async Task<ToolResponse<TOutput>> ExecuteAsync<TInput, TOutput>(
        ToolRequest<TInput> request, CancellationToken ct = default)
    {
        var resolve = _registry.Resolve(request.FullName, request.ToolVersion, request.TenantId);
        if (resolve.IsFailure)
            return ToolResponse<TOutput>.Fail(request.CorrelationId,
                ToolError.FromError(resolve.Error, 404));

        // Per-execution async scope — CreateAsyncScope NOT CreateScope
        // await using NOT using — required for IAsyncDisposable services (UnitOfWork)
        await using var scope = _scopeFactory.CreateAsyncScope();
        var handlerObj = scope.ServiceProvider.GetService(resolve.Value.HandlerType);

        if (handlerObj is null)
            return ToolResponse<TOutput>.Fail(request.CorrelationId,
                ToolError.Internal(
                    $"Handler '{request.FullName}@{request.ToolVersion}' not registered in DI."));

        // Fast path: caller's types match handler's generics exactly
        if (handlerObj is IToolHandler<TInput, TOutput> typed)
            return await typed.ExecuteAsync(request, ct);

        // Bridge path: JSON boundary types — used by API and CLI hosts
        if (typeof(TInput) == typeof(JsonElement) && typeof(TOutput) == typeof(JsonElement)
            && request is ToolRequest<JsonElement> jsonRequest)
            return await ExecuteWithJsonBridgeAsync<TOutput>(
                resolve.Value.HandlerType, handlerObj, jsonRequest, ct);

        return ToolResponse<TOutput>.Fail(request.CorrelationId,
            ToolError.Internal($"Type mismatch: handler cannot accept '{typeof(TInput).Name}'."));
    }

    private async Task<ToolResponse<TOutput>> ExecuteWithJsonBridgeAsync<TOutput>(
        Type handlerType, object handler, ToolRequest<JsonElement> jsonRequest, CancellationToken ct)
    {
        // 1. Find handler's concrete IToolHandler<ActualInput, ActualOutput>
        var iface = handlerType.GetInterfaces()
            .First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IToolHandler<,>));
        var actualInput  = iface.GetGenericArguments()[0];
        var actualOutput = iface.GetGenericArguments()[1];

        // 2. Deserialise JsonElement → actual input type
        var input = JsonSerializer.Deserialize(jsonRequest.Input, actualInput, _jsonOptions)
            ?? throw new InvalidOperationException("Input deserialized to null.");

        // 3. Build ToolRequest<ActualInput> with all original fields preserved
        var typedRequest = Activator.CreateInstance(
            typeof(ToolRequest<>).MakeGenericType(actualInput),
            jsonRequest.CorrelationId, jsonRequest.TenantId, jsonRequest.ToolName,
            jsonRequest.ToolVersion, input, jsonRequest.Mode, jsonRequest.Streaming,
            jsonRequest.UserId, jsonRequest.Metadata, jsonRequest.MaxResponseTokens,
            jsonRequest.ResponseFormat, jsonRequest.ToolNamespace);

        // 4. Invoke via reflection
        var method = iface.GetMethod(nameof(IToolHandler<object, object>.ExecuteAsync))!;
        var task   = (Task)method.Invoke(handler, [typedRequest, ct])!;
        await task.ConfigureAwait(false);

        // 5. Unbox result and serialise output back to JsonElement
        var boxed   = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var rType   = boxed.GetType();
        var success = (bool)rType.GetProperty(nameof(ToolResponse<object>.Success))!.GetValue(boxed)!;

        if (!success)
        {
            var err = (ToolError)rType.GetProperty(nameof(ToolResponse<object>.Error))!.GetValue(boxed)!;
            return ToolResponse<TOutput>.Fail(jsonRequest.CorrelationId, err);
        }

        var data    = rType.GetProperty(nameof(ToolResponse<object>.Data))!.GetValue(boxed);
        var metrics = (ToolUsageMetrics)rType.GetProperty(nameof(ToolResponse<object>.Metrics))!.GetValue(boxed)!;
        var json    = JsonSerializer.SerializeToElement(data, _jsonOptions);
        return ToolResponse<TOutput>.Ok(jsonRequest.CorrelationId, (TOutput)(object)json, metrics);
    }
}
```

---

## Application Layer — 8-behavior MediatR pipeline

### Behavior registration order (OUTERMOST → INNERMOST)

```
1. TenantAuthorizationBehavior  ← auth FIRST (OWASP A01, E4 — no info leakage to unauth callers)
2. ValidationBehavior           ← FluentValidation on IExecuteToolCommand
3. TokenBudgetBehavior          ← MaxResponseTokens vs Tenant.MaxResponseTokens
4. DailyBudgetBehavior          ← COUNT(*) today vs DailyToolCallBudget (E5)
5. LoopDetectionBehavior        ← ICacheProvider.IncrementAsync per correlationId+tool (F4)
6. ApprovalBehavior             ← [RequiresApproval] gate — IHumanApprovalGate
7. AuditBehavior                ← ToolInvocationRecord + events (H1/H2/H4/H5 — innermost)
   ↓
   Handler executes here
```

### Registration in AddToolApplication()

```csharp
services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssemblyContaining<ExecuteToolHandler>());

// MediatR wraps behaviors in registration order (first = outermost)
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TenantAuthorizationBehavior<,>));
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TokenBudgetBehavior<,>));
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(DailyBudgetBehavior<,>));
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoopDetectionBehavior<,>));
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ApprovalBehavior<,>));
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(AuditBehavior<,>));
```

---

## File layout — ToolEngine.Application

```
Commands/
  ExecuteToolCommand.cs         — IRequest<ToolResponse<TOutput>>
  AgentChatCommand.cs           — IRequest<AgentChatResponse> (Phase L)
  AgentChatResponse.cs          — reply, toolInvoked, usage, sessionId (Phase L)
Handlers/
  ExecuteToolHandler.cs         — IRequestHandler → delegates to IToolExecutor
  AgentChatHandler.cs           — delegates to IAgentOrchestrator (Phase L)
Behaviors/
  TenantAuthorizationBehavior.cs
  ValidationBehavior.cs
  TokenBudgetBehavior.cs
  DailyBudgetBehavior.cs
  LoopDetectionBehavior.cs
  ApprovalBehavior.cs
  AuditBehavior.cs
Abstractions/
  IExecuteToolCommand.cs        — interface shared by behaviors
Validators/
  ExecuteToolCommandValidator.cs
Telemetry/
  ToolEngineTelemetry.cs        — ActivitySource + Meter constants (G1/G2)
Extensions/
  ServiceCollectionExtensions.cs
```

### IExecuteToolCommand.cs

```csharp
public interface IExecuteToolCommand
{
    Guid    CorrelationId         { get; }
    string  TenantId              { get; }
    string? UserId                { get; }
    string  ToolName              { get; }
    string  ToolVersion           { get; }
    string? ToolNamespace         { get; }
    string  FullName              => string.IsNullOrEmpty(ToolNamespace) ? ToolName : $"{ToolNamespace}.{ToolName}";
    int     MaxResponseTokens     { get; }
    CallerType CallerType         { get; }  // H4
    string? GovernanceMetadataJson { get; } // H5
    string? IdempotencyKey        { get; }  // F8
}
```

---

## Behavior implementations

### TenantAuthorizationBehavior (E4 — auth before validation)

```csharp
// Guards: only applies to IExecuteToolCommand requests
if (request is not IExecuteToolCommand command) return await next();

var tenant = await _tenantRepo.GetByIdAsync(command.TenantId, ct);
if (tenant is null)
    return ToolResponse.Fail(ToolError.FromError(Error.NotFound("Tenant", command.TenantId), 401));
if (!tenant.IsActive)
    return ToolResponse.Fail(ToolError.FromError(Error.Unauthorized("Tenant is inactive."), 403));

// F6 — deny-by-default namespace check
if (command.ToolNamespace is not null && !tenant.IsNamespaceAllowed(command.ToolNamespace))
    return ToolResponse.Fail(
        ToolError.FromError(Error.Unauthorized($"Namespace '{command.ToolNamespace}' not allowed for tenant '{command.TenantId}'."), 403));

return await next();
```

### DailyBudgetBehavior (E5)

```csharp
if (tenant.DailyToolCallBudget <= 0) return await next();  // 0 = no cap

var startOfDayUtc = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime)
    .ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

var todayCount = await _db.Set<ToolInvocationRecord>()
    .CountAsync(r => r.TenantId == command.TenantId
                  && r.InvokedAt >= startOfDayUtc, ct);

if (todayCount >= tenant.DailyToolCallBudget)
    return ToolResponse.Fail(
        ToolError.FromError(Error.Validation("DAILY_BUDGET_EXCEEDED"), 429));

return await next();
```

### LoopDetectionBehavior (F4 — distributed via ICacheProvider)

```csharp
var key   = $"{command.CorrelationId}:{command.FullName}";
var count = await _cache.IncrementAsync(key, 1, expiry: TimeSpan.FromMinutes(10), ct);

if (count > _options.MaxCallsPerCorrelation)  // default 10
{
    await _cache.RemoveAsync(key, ct);  // clean up to prevent unbounded accumulation
    return ToolResponse.Fail(ToolError.FromError(Error.Validation("LOOP_DETECTED"), 429));
}
return await next();
// With Redis ICacheProvider, counter is shared across all API pods (horizontal scale safe)
```

### AuditBehavior (H1 / H2 / H4 / H5 / G1 / G2)

```csharp
// Create record BEFORE handler executes — status = Running
var record = ToolInvocationRecord.Create(
    command.CorrelationId, command.TenantId, command.UserId,
    command.FullName, command.ToolVersion,
    command.CallerType,         // H4
    command.GovernanceMetadataJson,  // H5
    _clock);

await _repo.AddAsync(record, ct);
await _unitOfWork.SaveChangesAsync(ct);
await EmitEventAsync(record, "Invoked", null, ct);  // H1 append-only

// OTel span (G1)
using var activity = ToolEngineTelemetry.ActivitySource.StartActivity("tool.execute");
activity?.SetTag("tool.full_name", command.FullName);
activity?.SetTag("tenant.id", command.TenantId);
activity?.SetTag("caller.type", command.CallerType.ToString());

var sw = Stopwatch.StartNew();
TResponse response;
try { response = await next(); }
catch (Exception ex)
{
    sw.Stop();
    record.MarkFailed(ToolError.Internal(ex.Message));
    await EmitEventAsync(record, "Failed", null, ct);
    await _unitOfWork.SaveChangesAsync(ct);
    throw;
}
sw.Stop();

if (response is IToolResponse tr && tr.IsSuspended)
{
    await EmitEventAsync(record, "Suspended", null, ct);
}
else if (response is IToolResponse { Success: true })
{
    record.MarkSucceeded(new ToolUsageMetrics(sw.ElapsedMilliseconds, 0));
    await EmitEventAsync(record, "Succeeded", sw.ElapsedMilliseconds, ct);
}
else if (response is IToolResponse { Error: { } err })
{
    record.MarkFailed(err);
    await EmitEventAsync(record, "Failed", null, ct);
}

await _unitOfWork.SaveChangesAsync(ct);

// G2 metrics
ToolEngineTelemetry.InvocationDuration.Record(sw.ElapsedMilliseconds,
    new("tool", command.FullName), new("status", record.Status.ToString()));
ToolEngineTelemetry.InvocationCount.Add(1,
    new("tool", command.FullName), new("status", record.Status.ToString()));

return response;
```

### EmitEventAsync helper (H1 — append-only event)

```csharp
private async Task EmitEventAsync(ToolInvocationRecord record, string eventType,
    long? durationMs, CancellationToken ct)
{
    var evt = ToolInvocationEvent.Create(
        record.Id, eventType,
        record.CallerType,            // H4 on every event row
        record.GovernanceMetadataJson, // H5 on every event row
        durationMs, _clock);
    await _eventRepo.AddAsync(evt, ct);
    // Note: SaveChangesAsync called by caller after all events are queued
}
```

---

## Infrastructure Layer

### AppDbContext — all 5 entity DbSets

```csharp
public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Tenant>               Tenants               => Set<Tenant>();
    public DbSet<ToolInvocationRecord> ToolInvocationRecords => Set<ToolInvocationRecord>();
    public DbSet<ToolInvocationEvent>  ToolInvocationEvents  => Set<ToolInvocationEvent>();
    public DbSet<PendingApproval>      PendingApprovals      => Set<PendingApproval>();
    public DbSet<OutboxMessage>        OutboxMessages        => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        // H2: index for O(log n) GDPR retention sweep
        mb.Entity<ToolInvocationRecord>()
          .HasIndex(r => new { r.RetainUntil, r.IsAnonymized });
        // F8: unique index for idempotency lookup
        mb.Entity<PendingApproval>()
          .HasIndex(p => new { p.IdempotencyKey, p.TenantId })
          .IsUnique(false);
    }
}
```

### UnitOfWork — IAsyncDisposable ONLY

```csharp
// CRITICAL: UnitOfWork implements ONLY IAsyncDisposable.
// Using synchronous using/Dispose() throws InvalidOperationException at runtime.
// All callers must use: await using var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
public sealed class UnitOfWork : IUnitOfWork  // IUnitOfWork extends IAsyncDisposable
{
    private readonly AppDbContext _db;
    public UnitOfWork(AppDbContext db) => _db = db;
    public Task<int> SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
    public ValueTask DisposeAsync() => _db.DisposeAsync();
    // NO Dispose() method — intentional
}
```

### CachedTenantReadRepository (F5 — eliminates double DB read per request)

```csharp
// Scoped lifetime — cache lives for duration of one HTTP request.
// TenantAuthorizationBehavior loads the tenant; TokenBudgetBehavior re-uses cached value.
public sealed class CachedTenantReadRepository : IReadRepository<Tenant, string>
{
    private readonly Dictionary<string, Tenant?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly IReadRepository<Tenant, string> _inner;

    public CachedTenantReadRepository(AppDbContext db)
        => _inner = new EfReadRepository<Tenant, string>(db);

    public async Task<Tenant?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(id, out var hit)) return hit;
        var tenant = await _inner.GetByIdAsync(id, ct);
        return _cache[id] = tenant;
    }
    // PagedListAsync, ListAsync delegate to _inner
}
```

### ICacheProvider implementations (F3)

**MemoryCacheProvider** — wraps `IMemoryCache` — dev/single-node:
```csharp
public async Task<long> IncrementAsync(string key, long delta = 1, TimeSpan? expiry = null, CancellationToken ct = default)
{
    var current = _cache.GetOrCreate<long>(key, e => {
        if (expiry.HasValue) e.AbsoluteExpirationRelativeToNow = expiry;
        return 0;
    });
    var next = current + delta;
    _cache.Set(key, next, expiry ?? TimeSpan.FromMinutes(10));
    return next;
}
```

**DistributedCacheProvider** — wraps `IDistributedCache` — Redis in production:
- `IncrementAsync`: get → parse → increment → set (optimistic; Phase I replaces with atomic Redis INCR+EXPIRE script)

### AddToolInfrastructure extension

```csharp
public static IServiceCollection AddToolInfrastructure(
    this IServiceCollection services,
    Action<DbContextOptionsBuilder> dbOptions)
{
    services.AddDbContext<AppDbContext>(dbOptions, ServiceLifetime.Scoped);
    services.AddScoped<IUnitOfWork, UnitOfWork>();
    services.AddScoped(typeof(IRepository<,>), typeof(EfRepository<,>));
    services.AddScoped<IReadRepository<Tenant, string>, CachedTenantReadRepository>(); // F5
    services.AddScoped(typeof(IReadRepository<,>), typeof(EfReadRepository<,>));

    // F3: register memory cache if no ICacheProvider registered yet (Redis wins if registered first)
    if (!services.Any(s => s.ServiceType == typeof(ICacheProvider)))
        services.AddMemoryCache().AddSingleton<ICacheProvider, MemoryCacheProvider>();

    services.Configure<ApprovalOptions>(/* ... */);
    services.AddScoped<IAsyncApprovalGate, AsyncApprovalGate>();
    services.AddHostedService<NotificationDispatchService>(); // F7 outbox delivery

    services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
    services.AddSingleton<IEmailSender, LoggingEmailSender>();       // replace for production
    services.AddSingleton<ISecretVault, NullSecretVault>();          // replace for production
    services.AddHttpClient();

    return services;
}

// Extension for Redis ICacheProvider (call BEFORE AddToolInfrastructure)
public static IServiceCollection AddDistributedCacheProvider(this IServiceCollection services)
    => services.AddSingleton<ICacheProvider, DistributedCacheProvider>();
```

### AsyncApprovalGate — key patterns (F7 / F8 / H3)

```csharp
// F8: idempotency — return existing pending approval if same key+tenant
var existing = await FindByIdempotencyKeyAsync(context.IdempotencyKey, context.TenantId, ct);
if (existing?.Status == ApprovalStatus.Pending && existing.ExpiresAt > _clock.UtcNow)
    return ApprovalDecision.Suspended(existing.Id);

var pending = PendingApproval.Create(
    context.TenantId, context.ToolFullName,
    context.Risk, context.Channel, context.IdempotencyKey, _clock);

// H3: EU AI Act acknowledgement for High/Critical tools
if (context.Risk >= ApprovalRisk.High)
{
    var ack = new AcknowledgementStatement(
        RegBasis:          "EU AI Act Article 14 §4",
        RiskLevel:         context.Risk.ToString(),
        ToolFullName:      context.ToolFullName,
        OperatorStatement: $"Approver acknowledges this is a {context.Risk}-risk AI-assisted action.",
        IssuedAt:          _clock.UtcNow);
    pending.SetAcknowledgement(JsonSerializer.Serialize(ack));
}

// F7: outbox — write approval + notification atomically in one SaveChangesAsync
var outbox = new OutboxMessage { MessageType = "approval.notify", Payload = JsonSerializer.Serialize(context), CreatedAt = _clock.UtcNow };
await _db.Set<PendingApproval>().AddAsync(pending, ct);
await _db.Set<OutboxMessage>().AddAsync(outbox, ct);
await _db.SaveChangesAsync(ct);  // single transaction

return ApprovalDecision.Suspended(pending.Id);
```

### NotificationDispatchService (F7 — outbox delivery with retry)

```csharp
// Polls every 15 seconds for unsent outbox messages
// Retries with exponential backoff: 30s → 2m → 8m → 32m → 2h
// Abandons after RetryCount >= 5 (sets Error, stops retrying)
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    while (!stoppingToken.IsCancellationRequested)
    {
        await DeliverPendingAsync(stoppingToken);
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
    }
}
```

### ToolEngineTelemetry.cs (G1 / G2)

```csharp
public static class ToolEngineTelemetry
{
    public const string ServiceName    = "ToolEngine";
    public const string ServiceVersion = "2026.1.0";

    public static readonly ActivitySource ActivitySource = new(ServiceName);
    public static readonly Meter          Meter          = new(ServiceName);

    // 6 metric instruments:
    public static readonly Histogram<long>     InvocationDuration    = Meter.CreateHistogram<long>("tool.invocation.duration", "ms");
    public static readonly Counter<long>       InvocationCount       = Meter.CreateCounter<long>("tool.invocation.count");
    public static readonly UpDownCounter<long> PendingApprovals      = Meter.CreateUpDownCounter<long>("tool.approval.pending.count");
    public static readonly Histogram<long>     ApprovalWaitDuration  = Meter.CreateHistogram<long>("tool.approval.wait.duration", "ms");
    public static readonly Counter<long>       LoopDetections        = Meter.CreateCounter<long>("tool.loop.detection.triggers");
    public static readonly Counter<long>       DailyBudgetExceeded   = Meter.CreateCounter<long>("tool.daily.budget.exceeded");
}
```

---

## Phase 3 completion checklist

- [ ] `ToolExecutor` constructor accepts `IServiceScopeFactory`, NOT `IServiceProvider`
- [ ] `ToolExecutor` uses `CreateAsyncScope()` + `await using`, NOT `CreateScope()` + `using`
- [ ] `UnitOfWork` has NO synchronous `Dispose()` method — only `DisposeAsync()`
- [ ] `IUnitOfWork` interface extends `IAsyncDisposable` (defined in Phase 1)
- [ ] Behavior registration order: TenantAuth → Validation → TokenBudget → DailyBudget → LoopDetection → Approval → Audit
- [ ] `TenantAuthorizationBehavior` is first registration (outermost — E4)
- [ ] Namespace deny-by-default: `Tenant.IsNamespaceAllowed()` returns `false` when `AllowedNamespaces.Count == 0` (F6)
- [ ] `DailyBudgetBehavior` uses UTC midnight boundary (not current time) (E5)
- [ ] `LoopDetectionBehavior` uses `ICacheProvider.IncrementAsync` (not static dictionary) (F4)
- [ ] `AuditBehavior` emits `ToolInvocationEvent` rows via `EmitEventAsync` — never updates them (H1)
- [ ] `AuditBehavior` sets `CallerType` and `GovernanceMetadataJson` on both record and events (H4 + H5)
- [ ] `AsyncApprovalGate` writes `PendingApproval` + `OutboxMessage` in ONE `SaveChangesAsync` (F7)
- [ ] Idempotency check in `AsyncApprovalGate` before creating new `PendingApproval` (F8)
- [ ] `AcknowledgementStatement` generated for `Risk.High` and `Risk.Critical` only (H3)
- [ ] `AppDbContext` has `DbSet` for all 5 entity types including `OutboxMessages`
- [ ] `(RetainUntil, IsAnonymized)` composite index in `OnModelCreating` (H2)
- [ ] `CachedTenantReadRepository` registered as `Scoped` (F5)
- [ ] `ToolEngineTelemetry.ActivitySource` started in `AuditBehavior` per invocation (G1)
- [ ] All 6 metric instruments defined in `ToolEngineTelemetry` (G2)

---

*Confidential — Internal Use Only | ONE BCG | ToolEngine v2026*

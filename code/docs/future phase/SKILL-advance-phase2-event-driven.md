---
name: toolengine-advance-phase2-event-driven
description: >
  Evolves ToolEngine v2026 from a request-response platform to a fully
  event-driven architecture. Covers: domain events on AggregateRoot with
  MediatR INotification dispatch, IEventBus abstraction with in-process
  (dev) and Azure Service Bus / AWS SQS (prod) implementations, CQRS
  read-side projections with separate ReadDbContext, durable approval
  workflow using Azure Durable Functions or Temporal.io, WebSocket / SSE
  push for real-time invocation status, and event sourcing for the
  ToolInvocationRecord aggregate.
classification: Confidential - Internal Use Only
---

# Advancement Phase 2 — Event-Driven Architecture & Durable Execution

## Prerequisites

Phase A1 (Security & Resilience) complete.
Azure Service Bus namespace (or AWS SQS) provisioned for production use.
PostgreSQL configured as the production database.

---

## Overview

| Item | Description | Pattern |
|------|-------------|---------|
| A2.1 | Domain events on AggregateRoot | DDD Domain Events |
| A2.2 | IEventBus abstraction | Event-Driven Architecture |
| A2.3 | CQRS read-side projections | CQRS read model separation |
| A2.4 | Durable approval workflow | Saga / Process Manager |
| A2.5 | WebSocket / SSE push | Reactive real-time UX |
| A2.6 | Event sourcing for invocation records | Event Sourcing |

---

## A2.1 — Domain Events on AggregateRoot

### Why

The current MediatR pipeline dispatches commands to handlers. Side effects
(notifications, projections, cache invalidation) are wired directly into
handlers, creating tight coupling. Domain events decouple the aggregate's
state change from its downstream consequences — the aggregate raises the event;
consumers subscribe independently.

### Domain event base — `ToolEngine.Core.Domain/Common/DomainEvent.cs`

Already defined in Phase 1. Ensure it is:

```csharp
namespace ToolEngine.Core.Domain.Common;

// Sealed record hierarchy — INotification wires it to MediatR
public abstract record DomainEvent(Guid Id, DateTimeOffset OccurredAt)
    : INotification
{
    protected DomainEvent() : this(Guid.NewGuid(), DateTimeOffset.UtcNow) { }
}
```

### Concrete domain events — `ToolEngine.Core.Domain/Events/`

```csharp
// ToolInvokedEvent — fires before handler executes
public sealed record ToolInvokedEvent(
    Guid   CorrelationId,
    string TenantId,
    string ToolFullName,
    string ToolVersion,
    CallerType CallerType) : DomainEvent;

// ToolCompletedEvent — fires on success or failure
public sealed record ToolCompletedEvent(
    Guid       CorrelationId,
    string     TenantId,
    string     ToolFullName,
    ToolStatus Status,
    long       DurationMs,
    int        TokensUsed) : DomainEvent;

// ApprovalRequestedEvent — fires when execution is suspended
public sealed record ApprovalRequestedEvent(
    Guid         ApprovalId,
    string       TenantId,
    string       ToolFullName,
    ApprovalRisk Risk,
    ApprovalChannel Channel,
    DateTimeOffset ExpiresAt) : DomainEvent;

// ApprovalDecidedEvent — fires when approver acts
public sealed record ApprovalDecidedEvent(
    Guid           ApprovalId,
    string         TenantId,
    ApprovalStatus Decision,
    string?        DecidedBy,
    DateTimeOffset DecidedAt) : DomainEvent;

// AgentSessionStartedEvent
public sealed record AgentSessionStartedEvent(
    string SessionId,
    string TenantId,
    CallerType CallerType,
    DateTimeOffset StartedAt) : DomainEvent;
```

### Raise events in aggregates — `AggregateRoot.cs`

```csharp
public abstract class AggregateRoot<TId> : Entity<TId>
{
    private readonly List<DomainEvent> _domainEvents = new();
    public IReadOnlyList<DomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void RaiseDomainEvent(DomainEvent evt) => _domainEvents.Add(evt);
    public void ClearDomainEvents() => _domainEvents.Clear();
}

// In ToolInvocationRecord.Create():
RaiseDomainEvent(new ToolInvokedEvent(
    Id, TenantId, ToolFullName, ToolVersion, CallerType));

// In ToolInvocationRecord.MarkSucceeded():
RaiseDomainEvent(new ToolCompletedEvent(
    Id, TenantId, ToolFullName, ToolStatus.Succeeded,
    metrics.DurationMs, metrics.TokensUsed));
```

### Dispatch domain events after `SaveChangesAsync` — `AppDbContext.cs`

```csharp
public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
{
    var aggregates = ChangeTracker.Entries<AggregateRoot<Guid>>()
        .Where(e => e.Entity.DomainEvents.Any())
        .Select(e => e.Entity)
        .ToList();

    var result = await base.SaveChangesAsync(ct);

    // Dispatch AFTER persist — events are facts about what happened
    foreach (var aggregate in aggregates)
    {
        foreach (var domainEvent in aggregate.DomainEvents)
            await _publisher.Publish(domainEvent, ct);
        aggregate.ClearDomainEvents();
    }
    return result;
}
```

---

## A2.2 — IEventBus Abstraction

### Why

Domain events dispatched via MediatR `IPublisher` are in-process only — if the
process crashes before the handler runs, the event is lost. `IEventBus` provides
a durable, out-of-process event delivery channel for cross-service communication
and replay capability.

### Interface — `ToolEngine.Core.Abstractions/Events/IEventBus.cs`

```csharp
namespace ToolEngine.Core.Abstractions.Events;

public interface IEventBus
{
    Task PublishAsync<TEvent>(TEvent evt, CancellationToken ct = default)
        where TEvent : DomainEvent;

    Task SubscribeAsync<TEvent>(
        Func<TEvent, CancellationToken, Task> handler,
        CancellationToken ct = default)
        where TEvent : DomainEvent;
}
```

### In-process implementation (dev) — `InProcessEventBus.cs`

```csharp
namespace ToolEngine.Infrastructure.Events;

// Single-process, no persistence — development and test only
public sealed class InProcessEventBus : IEventBus
{
    private readonly IMediator _mediator;

    public InProcessEventBus(IMediator mediator) => _mediator = mediator;

    public Task PublishAsync<TEvent>(TEvent evt, CancellationToken ct = default)
        where TEvent : DomainEvent =>
        _mediator.Publish(evt, ct);

    public Task SubscribeAsync<TEvent>(
        Func<TEvent, CancellationToken, Task> handler,
        CancellationToken ct = default)
        where TEvent : DomainEvent => Task.CompletedTask;
        // MediatR INotificationHandler<TEvent> serves as subscriber in-process
}
```

### Azure Service Bus implementation (prod) — `ServiceBusEventBus.cs`

```csharp
namespace ToolEngine.Infrastructure.Events;

public sealed class ServiceBusEventBus : IEventBus, IAsyncDisposable
{
    private readonly ServiceBusClient _client;
    private readonly Dictionary<string, ServiceBusSender> _senders = new();

    public ServiceBusEventBus(ServiceBusClient client) => _client = client;

    public async Task PublishAsync<TEvent>(TEvent evt, CancellationToken ct = default)
        where TEvent : DomainEvent
    {
        var topic    = typeof(TEvent).Name.ToLowerInvariant().Replace("event", "");
        var sender   = _senders.GetOrAdd(topic, t => _client.CreateSender(t));
        var payload  = JsonSerializer.SerializeToUtf8Bytes(evt);
        var message  = new ServiceBusMessage(payload)
        {
            MessageId        = evt.Id.ToString(),
            ContentType      = "application/json",
            Subject          = typeof(TEvent).Name,
            ApplicationProperties =
            {
                ["EventType"]  = typeof(TEvent).AssemblyQualifiedName,
                ["OccurredAt"] = evt.OccurredAt.ToString("O")
            }
        };
        await sender.SendMessageAsync(message, ct);
    }

    public async Task SubscribeAsync<TEvent>(
        Func<TEvent, CancellationToken, Task> handler,
        CancellationToken ct = default)
        where TEvent : DomainEvent
    {
        var topic      = typeof(TEvent).Name.ToLowerInvariant().Replace("event", "");
        var processor  = _client.CreateProcessor(topic, new ServiceBusProcessorOptions
        {
            MaxConcurrentCalls = 4,
            AutoCompleteMessages = false
        });
        processor.ProcessMessageAsync += async args =>
        {
            var evt = JsonSerializer.Deserialize<TEvent>(args.Message.Body)!;
            await handler(evt, args.CancellationToken);
            await args.CompleteMessageAsync(args.Message);
        };
        processor.ProcessErrorAsync += args =>
        {
            // Log error; message will be dead-lettered after max delivery count
            return Task.CompletedTask;
        };
        await processor.StartProcessingAsync(ct);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var sender in _senders.Values)
            await sender.DisposeAsync();
        await _client.DisposeAsync();
    }
}
```

### Registration — `Program.cs`

```json
{ "EventBus": { "Provider": "servicebus" }, "ConnectionStrings": { "ServiceBus": "..." } }
```

```csharp
var ebProvider = config["EventBus:Provider"];
if (ebProvider == "servicebus")
{
    builder.Services.AddSingleton(
        new ServiceBusClient(config.GetConnectionString("ServiceBus")));
    builder.Services.AddSingleton<IEventBus, ServiceBusEventBus>();
}
else
{
    builder.Services.AddSingleton<IEventBus, InProcessEventBus>();
}
```

---

## A2.3 — CQRS Read-Side Projections

### Why

All reads currently hit the write database. For dashboards, approval queues,
invocation history, and tenant analytics, this creates lock contention and
prevents independent scaling of reads vs. writes. Read projections maintain
denormalized views optimized for query patterns.

### Separate `ReadDbContext` — `ToolEngine.Infrastructure/Persistence/`

```csharp
namespace ToolEngine.Infrastructure.Persistence;

// Read-only EF Core context — maps to projections / materialized views
// Uses a read replica connection string in production
public sealed class ReadDbContext : DbContext
{
    public ReadDbContext(DbContextOptions<ReadDbContext> opts) : base(opts) { }

    public DbSet<InvocationSummaryProjection> InvocationSummaries => Set<InvocationSummaryProjection>();
    public DbSet<PendingApprovalSummaryProjection> PendingApprovals => Set<PendingApprovalSummaryProjection>();
    public DbSet<TenantUsageProjection> TenantUsage => Set<TenantUsageProjection>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<InvocationSummaryProjection>().ToTable("vw_InvocationSummaries")
            .HasNoKey();
        mb.Entity<PendingApprovalSummaryProjection>().ToTable("vw_PendingApprovals")
            .HasNoKey();
        mb.Entity<TenantUsageProjection>().ToTable("vw_TenantUsage")
            .HasNoKey();
    }
}
```

### Projection models — `ToolEngine.Core.Domain/Projections/`

```csharp
// Flat, denormalized — optimized for the approval dashboard query
public sealed class PendingApprovalSummaryProjection
{
    public Guid          ApprovalId    { get; init; }
    public string        TenantId      { get; init; } = default!;
    public string        ToolFullName  { get; init; } = default!;
    public ApprovalRisk  Risk          { get; init; }
    public ApprovalChannel Channel     { get; init; }
    public DateTimeOffset RequestedAt  { get; init; }
    public DateTimeOffset ExpiresAt    { get; init; }
    public string?       RequestedBy   { get; init; }
    public int           MinutesRemaining { get; init; }
}

// Per-tenant daily stats — drives the usage dashboard
public sealed class TenantUsageProjection
{
    public string TenantId           { get; init; } = default!;
    public DateOnly Date             { get; init; }
    public int    TotalInvocations   { get; init; }
    public int    SucceededCount     { get; init; }
    public int    FailedCount        { get; init; }
    public int    SuspendedCount     { get; init; }
    public long   TotalTokensUsed    { get; init; }
    public long   AvgDurationMs      { get; init; }
}
```

### Projection updater — `INotificationHandler` implementations

```csharp
// Updates projection tables when domain events arrive via MediatR
public sealed class TenantUsageProjectionUpdater
    : INotificationHandler<ToolCompletedEvent>
{
    private readonly AppDbContext _db;

    public async Task Handle(
        ToolCompletedEvent evt, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date);

        // Upsert — increment counters for today's row
        await _db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "TenantUsageDailyStats"
                ("TenantId", "Date", "TotalInvocations", ...)
            VALUES ({evt.TenantId}, {today}, 1, ...)
            ON CONFLICT ("TenantId", "Date")
            DO UPDATE SET
                "TotalInvocations" = "TenantUsageDailyStats"."TotalInvocations" + 1
            """, ct);
    }
}
```

---

## A2.4 — Durable Approval Workflow

### Why

The current approval lifecycle is implemented via DB-polling background jobs.
This is fragile: if the worker crashes between writing `PendingApproval` and
sending the notification, the approver is never contacted. Durable workflow
persists every state transition and resumes automatically after failures,
restarts, or scaling events.

### Option A — Azure Durable Functions

```csharp
// Orchestrator — approval saga
[FunctionName("ApprovalWorkflow")]
public static async Task RunOrchestrator(
    [OrchestrationTrigger] IDurableOrchestrationContext ctx,
    ILogger logger)
{
    var input = ctx.GetInput<ApprovalWorkflowInput>();

    // Step 1: Send notification (retried automatically by Durable Functions)
    await ctx.CallActivityAsync("SendApprovalNotification", input);

    // Step 2: Wait for approver decision or timeout (whichever comes first)
    var timeout  = ctx.CurrentUtcDateTime.AddMinutes(input.TimeoutMinutes);
    var decision = ctx.WaitForExternalEvent<string>("ApprovalDecision");
    var expired  = ctx.CreateTimer(timeout, CancellationToken.None);

    var winner = await Task.WhenAny(decision, expired);

    if (winner == expired)
    {
        // Escalate — send to escalation email
        await ctx.CallActivityAsync("SendEscalationNotification", input);
        await ctx.CallActivityAsync("ExpireApproval", input.ApprovalId);
        return;
    }

    // Step 3: Re-execute the stored command
    if (decision.Result == "Approve")
        await ctx.CallActivityAsync("ReExecuteTool", input.ApprovalId);
}
```

### Option B — Temporal.io (cloud-agnostic)

```csharp
// Workflow definition — Temporal C# SDK
[Workflow]
public class ApprovalWorkflow
{
    private string? _decision;

    [WorkflowRun]
    public async Task RunAsync(ApprovalWorkflowInput input)
    {
        // Activity: Send notification (Temporal retries with backoff)
        await Workflow.ExecuteActivityAsync(
            (IApprovalActivities a) => a.SendNotificationAsync(input),
            new ActivityOptions { StartToCloseTimeout = TimeSpan.FromMinutes(5) });

        // Wait for signal from API or timeout
        var received = await Workflow.WaitConditionAsync(
            () => _decision is not null,
            timeout: TimeSpan.FromMinutes(input.TimeoutMinutes));

        if (!received)
        {
            await Workflow.ExecuteActivityAsync(
                (IApprovalActivities a) => a.EscalateAsync(input.ApprovalId));
            return;
        }

        if (_decision == "Approve")
            await Workflow.ExecuteActivityAsync(
                (IApprovalActivities a) => a.ReExecuteToolAsync(input.ApprovalId));
    }

    [WorkflowSignal]
    public Task ReceiveDecisionAsync(string decision)
    {
        _decision = decision;
        return Task.CompletedTask;
    }
}
```

### Workflow trigger from `AsyncApprovalGate.cs`

```csharp
// Replace outbox polling with workflow start
var workflowInput = new ApprovalWorkflowInput(
    ApprovalId:     pending.Id,
    TenantId:       pending.TenantId,
    ToolFullName:   pending.ToolFullName,
    TimeoutMinutes: _opts.TokenExpiryMinutes);

await _workflowClient.StartWorkflowAsync(
    workflowInput, pending.Id.ToString());
```

---

## A2.5 — WebSocket / SSE Push for Invocation Status

### Why

The client currently polls `GET /invocations/{id}/status` every N seconds.
This creates unnecessary load and adds latency between approval decision and
client notification. SSE (Server-Sent Events) is simpler than WebSocket for
unidirectional push and works through proxies and load balancers.

### SSE endpoint — `ToolEngine.Api/Endpoints/InvocationEndpoints.cs`

```csharp
// GET /invocations/{id}/stream — SSE push
app.MapGet("/invocations/{id}/stream", async (
    Guid id, HttpContext ctx, AppDbContext db,
    CancellationToken ct) =>
{
    ctx.Response.Headers["Content-Type"]  = "text/event-stream";
    ctx.Response.Headers["Cache-Control"] = "no-cache";
    ctx.Response.Headers["Connection"]    = "keep-alive";

    while (!ct.IsCancellationRequested)
    {
        var record = await db.Set<ToolInvocationRecord>()
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, ct);

        if (record is null) break;

        var data = JsonSerializer.Serialize(new
        {
            status      = record.Status.ToString(),
            completedAt = record.CompletedAt,
            durationMs  = record.DurationMs,
            errorCode   = record.ErrorCode
        });

        await ctx.Response.WriteAsync($"data: {data}\n\n", ct);
        await ctx.Response.Body.FlushAsync(ct);

        // Stop streaming when terminal state reached
        if (record.Status is ToolStatus.Succeeded or
                             ToolStatus.Failed or
                             ToolStatus.Suspended)
            break;

        await Task.Delay(TimeSpan.FromSeconds(2), ct);
    }
})
.RequireAuthorization();
```

### React frontend — `useInvocationStatus.ts`

```typescript
export function useInvocationStatus(invocationId: string) {
  const [status, setStatus] = useState<InvocationStatus | null>(null);

  useEffect(() => {
    const es = new EventSource(`/invocations/${invocationId}/stream`, {
      withCredentials: true
    });

    es.onmessage = (event) => {
      const data: InvocationStatus = JSON.parse(event.data);
      setStatus(data);
      // Close stream on terminal state
      if (['Succeeded', 'Failed', 'Suspended'].includes(data.status)) {
        es.close();
      }
    };

    es.onerror = () => es.close();
    return () => es.close();
  }, [invocationId]);

  return status;
}
```

---

## A2.6 — Event Sourcing for ToolInvocationRecord

### Why

The current `ToolInvocationRecord` is a mutable read-side convenience table.
With event sourcing, the `ToolInvocationEvent` log becomes the source of truth —
the current state is derived by replaying events. This enables: time-travel
debugging, full audit replay for regulatory reports, and rebuilding projections
from scratch.

### Event store interface — `ToolEngine.Core.Abstractions/Events/IEventStore.cs`

```csharp
namespace ToolEngine.Core.Abstractions.Events;

public interface IEventStore
{
    Task AppendAsync<TAggregate>(
        Guid aggregateId,
        IEnumerable<DomainEvent> events,
        int expectedVersion,
        CancellationToken ct = default);

    Task<IReadOnlyList<DomainEvent>> LoadAsync(
        Guid aggregateId,
        int fromVersion = 0,
        CancellationToken ct = default);

    // Replay events to rebuild a projection from scratch
    IAsyncEnumerable<DomainEvent> StreamAllAsync(
        DateTimeOffset? from = null,
        CancellationToken ct = default);
}
```

### Aggregate reconstitution — `ToolInvocationRecord.cs`

```csharp
// Factory: reconstitute aggregate from event stream
public static ToolInvocationRecord Reconstitute(
    IEnumerable<DomainEvent> events)
{
    var record = new ToolInvocationRecord();
    foreach (var evt in events)
        record.Apply(evt);
    return record;
}

// Apply domain events to update state
private void Apply(DomainEvent evt)
{
    switch (evt)
    {
        case ToolInvokedEvent e:
            Id           = e.CorrelationId;
            TenantId     = e.TenantId;
            ToolFullName = e.ToolFullName;
            ToolVersion  = e.ToolVersion;
            Status       = ToolStatus.Running;
            InvokedAt    = e.OccurredAt;
            CallerType   = e.CallerType;
            break;

        case ToolCompletedEvent e:
            Status      = e.Status;
            CompletedAt = e.OccurredAt;
            DurationMs  = e.DurationMs;
            TokensUsed  = e.TokensUsed;
            break;
    }
}
```

---

## Phase A2 Completion Checklist

### A2.1 — Domain Events
- [ ] `DomainEvent` base record implements `INotification`
- [ ] All 5 event types defined in `Core.Domain/Events/`
- [ ] `AggregateRoot.RaiseDomainEvent` and `ClearDomainEvents` present
- [ ] `AppDbContext.SaveChangesAsync` dispatches events AFTER `base.SaveChangesAsync`
- [ ] `ToolInvocationRecord.Create` raises `ToolInvokedEvent`
- [ ] `ToolInvocationRecord.MarkSucceeded` raises `ToolCompletedEvent`
- [ ] `PendingApproval.Create` raises `ApprovalRequestedEvent`

### A2.2 — IEventBus
- [ ] `IEventBus` interface in `Core.Abstractions/Events/`
- [ ] `InProcessEventBus` delegates to `IMediator.Publish`
- [ ] `ServiceBusEventBus` uses `ServiceBusSender` with `MessageId = evt.Id`
- [ ] Registration switches on `EventBus:Provider` config key
- [ ] `ServiceBusEventBus` implements `IAsyncDisposable`

### A2.3 — CQRS Projections
- [ ] `ReadDbContext` separate from `AppDbContext` — read replica connection string
- [ ] 3 projection models: `InvocationSummary`, `PendingApprovalSummary`, `TenantUsage`
- [ ] Projection updaters are `INotificationHandler<DomainEvent>` implementations
- [ ] Read endpoints use `ReadDbContext`, never `AppDbContext`
- [ ] `ReadDbContext` registered as `Scoped` with read-replica connection string

### A2.4 — Durable Workflow
- [ ] One of: Azure Durable Functions OR Temporal.io SDK installed and wired
- [ ] Orchestrator implements: notify → wait-for-signal → escalate-on-timeout → re-execute-on-approve
- [ ] Workflow started from `AsyncApprovalGate` (replaces outbox polling)
- [ ] Signal endpoint at `PUT /approvals/{id}/decide` sends signal to workflow
- [ ] Escalation fires at 50% of timeout window (not only at expiry)

### A2.5 — SSE Push
- [ ] `GET /invocations/{id}/stream` returns `text/event-stream`
- [ ] SSE closes automatically on terminal status
- [ ] `useInvocationStatus` React hook consumes SSE stream
- [ ] Frontend stops polling `GET /invocations/{id}/status` (remove old polling loop)

### A2.6 — Event Sourcing
- [ ] `IEventStore` interface in `Core.Abstractions/Events/`
- [ ] `ToolInvocationRecord.Reconstitute(events)` factory method
- [ ] `Apply(DomainEvent)` handles all relevant event types
- [ ] `IEventStore.StreamAllAsync` enables full replay for projection rebuilding
- [ ] Optimistic concurrency: `expectedVersion` prevents lost updates

---

*Confidential — Internal Use Only | ONE BCG | ToolEngine v2026*

# ToolEngine v2026 POC — Developer FAQ

**ONE BCG** · Internal Technical Reference  
Confidential - Internal Use Only

---

## Table of Contents

1. [What does each layer and project in the solution do?](#1-what-does-each-layer-and-project-in-the-solution-do)
2. [How do I create a new tool handler?](#2-how-do-i-create-a-new-tool-handler)
3. [How do I create a new scenario (multi-step workflow)?](#3-how-do-i-create-a-new-scenario-multi-step-workflow)
4. [How do I register a new tool so the engine can discover and execute it?](#4-how-do-i-register-a-new-tool-so-the-engine-can-discover-and-execute-it)
5. [What are the rules and pitfalls when building a tool handler?](#5-what-are-the-rules-and-pitfalls-when-building-a-tool-handler)
6. [How do Logic, API, and Database tool types differ?](#6-how-do-logic-api-and-database-tool-types-differ)
7. [How are tool input and output payloads defined and validated?](#7-how-are-tool-input-and-output-payloads-defined-and-validated)
8. [In how many ways can a tool be called?](#8-in-how-many-ways-can-a-tool-be-called)
9. [How do dependent tools share data — how does output from one tool feed into the next?](#9-how-do-dependent-tools-share-data--how-does-output-from-one-tool-feed-into-the-next)
10. [How do I read and understand an existing payment tool?](#10-how-do-i-read-and-understand-an-existing-payment-tool)

---

## 1. What does each layer and project in the solution do?

The solution is organised in a strict bottom-to-top dependency order. A lower layer never references a higher one.

```
src/
├── Core/
│   ├── ToolEngine.Core.Abstractions       ← interfaces only, zero NuGet
│   └── ToolEngine.Core.Domain             ← Result<T>, shared entities, enums
│
├── Tools/
│   ├── ToolEngine.Tools.Abstractions      ← what a "tool" is
│   ├── ToolEngine.Tools.Registry          ← tool lookup + namespace security
│   └── ToolEngine.Tools.Executor          ← runs tools in a fresh DI scope
│
├── Modules/
│   └── Payment/
│       ├── ToolEngine.Payment.Domain      ← PaymentInstruction aggregate + value objects
│       ├── ToolEngine.Payment.Tools       ← 8 stage handlers (Stages 0–7)
│       ├── ToolEngine.Payment.Application ← MediatR commands for payment flows
│       ├── ToolEngine.Payment.Infrastructure ← EF configurations for payment entities
│       └── ToolEngine.Payment.Api         ← payment-specific controllers
│
├── Application/
│   └── ToolEngine.Application             ← MediatR pipeline (4 behaviors)
│
├── Infrastructure/
│   └── ToolEngine.Infrastructure          ← EF Core, UnitOfWork, AppDbContext, DbSeeder
│
└── Hosts/
    ├── ToolEngine.Api                     ← HTTP host, JWT, Swagger, rate limiting
    ├── ToolEngine.Cli                     ← local runner / tooling
    └── ToolEngine.Ui                      ← frontend host
```

### Layer responsibilities

| Layer | What it owns | What it must NOT reference |
|---|---|---|
| `Core.Abstractions` | `IRepository`, `IUnitOfWork`, `ISecretVault`, `ICacheProvider` | Anything. No NuGet, no other projects. |
| `Core.Domain` | `Result<T>`, entities, enums | Infrastructure, EF, HTTP |
| `Tools.Abstractions` | `IToolHandler`, base classes, `ToolSchema`, `ToolDescriptor` | Domain entities, EF |
| `Tools.Registry` | `ConcurrentDictionary` of `ToolDescriptor`; deny-by-default namespace policy | Handlers, EF |
| `Tools.Executor` | `IServiceScopeFactory.CreateAsyncScope()` per execution | Handler implementations |
| `Payment.Domain` | `PaymentInstruction` aggregate, value objects | EF, HTTP, Tools |
| `Payment.Tools` | Stage 0–7 handlers; all `Transient` | MediatR, HTTP |
| `Application` | 4 MediatR behaviors (Validation, LoopDetection, Approval, Audit); `ExecuteToolCommand`; `ScenarioRunner`; `ToolPlanOrchestrator` | Domain entities directly |
| `Infrastructure` | `AppDbContext`, migrations, `UnitOfWork`, `AsyncApprovalGate` | HTTP, MediatR |
| `Hosts/*` | Entry points only — DI wiring, JWT, routing | Business logic |

**Key rule:** every call path eventually converges on `ISender.Send(ExecuteToolCommand)`. HTTP controllers, the CLI, scenario runners, and LLM agents all go through the same 4-behavior MediatR pipeline and get the same governance: ValidationBehavior → LoopDetectionBehavior → ApprovalBehavior → AuditBehavior.

---

## 2. How do I create a new tool handler?

A tool is a class that inherits one of the four base classes, declares its identity and schema as properties, and implements `HandleAsync`. Nothing else is required.

### Step 1 — Define input and output records

```csharp
// Keep records immutable (sealed record) and flat.
// No nested complex objects in the input — JSON Schema generation is flat.
public sealed record SendReminderInput(Guid PaymentId, string RecipientEmail);

public sealed record SendReminderOutput(bool Sent, string Message);
```

### Step 2 — Choose the right base class

| Base class | When to use |
|---|---|
| `LogicToolBase<TIn, TOut>` | Pure computation — no I/O, no database, no HTTP |
| `ApiToolBase<TIn, TOut>` | Outbound HTTP calls to external services |
| `DatabaseToolBase<TIn, TOut>` | Read/write via `IUnitOfWork` and `AppDbContext` |
| `CompositeToolBase<TIn, TOut>` | Orchestrates other tools via `IToolExecutor` |

### Step 3 — Implement the handler

```csharp
// File: src/Modules/Payment/ToolEngine.Payment.Tools/Notifications/SendReminderHandler.cs

public sealed class SendReminderHandler
    : ApiToolBase<SendReminderInput, SendReminderOutput>
{
    public SendReminderHandler(IHttpClientFactory httpClientFactory)
        : base(httpClientFactory) { }

    // Identity — must be unique across the registry
    public override string Namespace => "payment";
    public override string Name      => "send-reminder";
    public override string Version   => "v1";

    // Schema — describes the tool for humans AND LLM orchestrators
    public override ToolSchema Schema => new(
        Description:  "Sends a payment reminder email to the specified recipient.",
        WhenToUse:    "Call after Stage 7 reconciliation when a payment is overdue.",
        WhenNotToUse: "Do not use for bulk notifications — this tool sends one email per call.",
        Examples:     ["Send reminder for overdue payment PRID-XYZ to finance@acme.com"],
        InputSchema:  BuildJsonSchema<SendReminderInput>(),
        OutputSchema: BuildJsonSchema<SendReminderOutput>());

    protected override async Task<ToolResponse<SendReminderOutput>> HandleAsync(
        ToolRequest<SendReminderInput> request, CancellationToken ct)
    {
        var inp    = request.Input;
        var client = CreateClient("notifications");

        // ... call external API ...

        return ToolResponse<SendReminderOutput>.Ok(
            request.CorrelationId,
            new SendReminderOutput(Sent: true, Message: $"Reminder sent to {inp.RecipientEmail}."));
    }
}
```

### Step 4 — Register the tool (see Q4)

---

## 3. How do I create a new scenario (multi-step workflow)?

A scenario is a named, versioned class that declares a `ToolPlan` — an ordered list of steps with dependencies and output mappings. The scenario itself does not execute; it only builds the plan. Execution is delegated to `IToolPlanOrchestrator`.

### Minimal scenario example

```csharp
// File: src/Modules/Payment/ToolEngine.Payment.Tools/Scenarios/ReminderScenario.cs

public sealed class ReminderScenario : IScenarioDefinition
{
    public string Name        => "payment.send-reminder";
    public string Version     => "v1";
    public string Description => "Reconcile an overdue payment and send a reminder.";

    public JsonElement InputSchema => ToJson(new
    {
        type       = "object",
        required   = new[] { "paymentId", "recipientEmail" },
        properties = new
        {
            paymentId      = new { type = "string" },
            recipientEmail = new { type = "string" },
        }
    });

    public ToolPlan Build(JsonElement input)
    {
        var paymentId      = input.GetProperty("paymentId").GetGuid();
        var recipientEmail = input.GetProperty("recipientEmail").GetString()!;

        return new ToolPlan(
            PlanId: Guid.NewGuid(),
            Mode:   ExecutionMode.Sequential,
            Steps:
            [
                new ToolStep(
                    StepId:    "step-1-reconcile",
                    Namespace: "payment",
                    ToolName:  "reconcile",
                    Version:   "v1",
                    Input:     ToJson(new { paymentId }),
                    DependsOn: []),

                new ToolStep(
                    StepId:    "step-2-reminder",
                    Namespace: "payment",
                    ToolName:  "send-reminder",
                    Version:   "v1",
                    Input:     ToJson(new { paymentId, recipientEmail }),
                    DependsOn: ["step-1-reconcile"]),   // only runs if step-1 succeeds
            ]);
    }

    private static JsonElement ToJson(object obj) =>
        JsonSerializer.SerializeToElement(obj,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
}
```

### Scenario with `IRequiresSetup`

Implement `IRequiresSetup` when the scenario must create a database record before the plan runs and inject the generated ID into the input. The `ScenarioRunner` calls `SetupAsync` before `Build`.

```csharp
public sealed class MyScenario : IScenarioDefinition, IRequiresSetup
{
    public async Task<JsonElement> SetupAsync(
        JsonElement input, IServiceProvider services, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var record = MyEntity.Create(...);
        db.Set<MyEntity>().Add(record);
        await db.SaveChangesAsync(ct);

        // Inject the generated ID back into input before Build() is called
        var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(input)!;
        dict["entityId"] = JsonSerializer.SerializeToElement(record.Id);
        return JsonSerializer.SerializeToElement(dict);
    }

    public ToolPlan Build(JsonElement input) { ... }
}
```

### Register the scenario

```csharp
// Step 1 — In AddPaymentTools(): register the scenario class as Transient
services.AddTransient<ReminderScenario>();

// Step 2 — In RegisterPaymentScenarios() called post-build from Program.cs:
registry.Register(sp.GetRequiredService<ReminderScenario>());
```

Scenarios are not auto-discovered. They must be explicitly registered in `RegisterPaymentScenarios(provider, registry)`, which is called from `Program.cs` after the container is built. See Q4 for the full registration pattern.

---

## 4. How do I register a new tool so the engine can discover and execute it?

Registration is a two-step process: (1) register the handler with DI in `AddPaymentTools()`, and (2) register its descriptor with `IToolRegistry` after the container is built. This split is necessary because descriptor registration requires resolving the handler from DI to read its `Schema`, `Namespace`, `Name`, and `Version` properties.

### Step 1 — Register the handler as Transient (at DI setup time)

```csharp
// File: src/Modules/Payment/ToolEngine.Payment.Tools/Extensions/ServiceCollectionExtensions.cs

public static IServiceCollection AddPaymentTools(this IServiceCollection services)
{
    // Existing handlers
    services.AddTransient<VerifyPayeeHandler>();
    // ... other handlers ...

    // Add your new handler here
    services.AddTransient<SendReminderHandler>();

    // Add the scenario if applicable
    services.AddTransient<ReminderScenario>();

    return services;
}
```

### Step 2 — Register the descriptor with the registry (after container is built)

Add an entry to `RegisterPaymentToolDescriptors`, which is called from `Program.cs` after `app.Build()`:

```csharp
public static async Task RegisterPaymentToolDescriptors(IServiceProvider provider)
{
    var registry = provider.GetRequiredService<IToolRegistry>();

    // Existing registrations ...
    await RegisterToolAsync<VerifyPayeeHandler>(provider, registry, ToolType.Database);

    // Add your new handler
    await RegisterToolAsync<SendReminderHandler>(provider, registry, ToolType.Api);
}

// The helper — resolves the handler from DI in a fresh async scope,
// reads its metadata, and registers the descriptor.
private static async Task RegisterToolAsync<THandler>(
    IServiceProvider provider, IToolRegistry registry, ToolType toolType)
    where THandler : ToolHandlerBase
{
    await using var scope   = provider.CreateAsyncScope();
    var             handler = scope.ServiceProvider.GetRequiredService<THandler>();
    registry.Register(handler.ToDescriptor(toolType, typeof(THandler)));
}
```

### Step 3 — Register scenario definitions (if applicable)

```csharp
public static void RegisterPaymentScenarios(
    IServiceProvider provider, IScenarioRegistry registry)
{
    using var scope = provider.CreateScope();
    var sp = scope.ServiceProvider;

    // Existing scenarios ...
    registry.Register(sp.GetRequiredService<PaymentComplianceScenario>());

    // Add your new scenario
    registry.Register(sp.GetRequiredService<ReminderScenario>());
}
```

### How it all wires together at startup

`Program.cs` calls two things after `app.Build()`:

```csharp
// 1. Registers DI services (handlers, scenarios, MediatR, EF)
builder.Services.AddPaymentModule();

// 2. After build — populates IToolRegistry and IScenarioRegistry
await RegisterPaymentModuleAsync(app.Services, scenarioRegistry);
// This calls RegisterPaymentToolDescriptors + RegisterPaymentScenarios internally
```

Once registered, the tool is:
- Discoverable via `GET /api/v1/tools`
- Callable via `ExecuteToolCommand` by name + version
- Referenceable in scenario `ToolStep` declarations

> **Note on security:** The registry only resolves tools that have been explicitly registered. Any call to an unregistered tool name returns `Result.Failure(Error.NotFound)` — the registry does not execute unknown tools.

---

## 5. What are the rules and pitfalls when building a tool handler?

### Mandatory rules

**Always register as `Transient`.**  
Tool handlers must be `Transient`. The `IToolExecutor` creates a new DI scope per execution. If a handler is `Singleton` or `Scoped`, it will capture the `Scoped` `IUnitOfWork` from the outer scope — causing data corruption across requests (captive dependency).

```csharp
// CORRECT
services.AddTransient<MyToolHandler>();

// WRONG — handler outlives the UnitOfWork scope
services.AddScoped<MyToolHandler>();
services.AddSingleton<MyToolHandler>();
```

**Never throw exceptions as flow control.**  
Return `ToolResponse.Fail(...)` for every expected failure. Reserve exceptions for truly unexpected states (null references, infrastructure failures).

```csharp
// CORRECT
if (record is null)
    return ToolResponse<MyOutput>.Fail(request.CorrelationId,
        ToolError.NotFound("Record not found."));

// WRONG
if (record is null)
    throw new KeyNotFoundException("Record not found.");
```

**Always update payment status on block.**  
Any stage that blocks a payment must call `payment.Block(status, reason)` and `SaveChangesAsync` before returning `Fail`. Leaving the instruction in the wrong state causes silent cascade failures in later stages.

**Keep input/output records flat.**  
`BuildJsonSchema<T>()` reflects the CLR type. Nested complex objects produce `"type": "object"` with no inner schema. If nesting is required, write the JSON Schema manually instead of using `BuildJsonSchema`.

**`IUnitOfWork` is `IAsyncDisposable` only — no `.Dispose()`.**  
Never call the synchronous `Dispose()`. Always `await using` or let the DI scope dispose it.

### Common pitfalls

| Pitfall | Consequence | Fix |
|---|---|---|
| Registering handler as `Scoped` | Captive dependency with `UnitOfWork`; data bleeds between requests | Use `Transient` |
| Not blocking the payment status before returning `Fail` | Payment stuck in wrong state; Stage 6/7 may try to execute | Always call `payment.Block()` before `Fail` |
| Using `Guid.NewGuid()` for security tokens | 122-bit, version bits leak entropy | Use `RandomNumberGenerator.GetBytes(32)` |
| Using `.Dispose()` on `IUnitOfWork` | Runtime exception — sync dispose not implemented | Use `await using` |
| Mutable output records | Serialisation side-effects | Always `sealed record` |

---

## 6. How do Logic, API, and Database tool types differ?

All three inherit `ToolHandlerBase` and implement `IToolHandler<TIn, TOut>`. The difference is what they inject and what I/O they perform.

```
                        ToolHandlerBase
                       (Namespace, Name, Version, Schema, BuildJsonSchema)
                              │
          ┌───────────────────┼──────────────────────┬────────────────────┐
          │                   │                      │                    │
   LogicToolBase       ApiToolBase           DatabaseToolBase     CompositeToolBase
   (no injections)     (IHttpClientFactory)  (IUnitOfWork)        (IToolExecutor)
   pure computation    outbound HTTP         DB read/write         calls other tools
```

### `LogicToolBase` — pure computation

No dependencies injected. Deterministic: same input always produces same output.

```csharp
// Stage 3 — WHT calculation is pure arithmetic
public sealed class CalculateWhtHandler
    : LogicToolBase<CalculateWhtInput, CalculateWhtOutput>
{
    // No constructor needed — nothing to inject

    protected override Task<ToolResponse<CalculateWhtOutput>> HandleAsync(
        ToolRequest<CalculateWhtInput> request, CancellationToken ct)
    {
        var rate   = LookupTreatyRate(request.Input.PayerJurisdiction, request.Input.PayeeJurisdiction);
        var amount = request.Input.GrossAmount * rate;

        return Task.FromResult(ToolResponse<CalculateWhtOutput>.Ok(
            request.CorrelationId,
            new CalculateWhtOutput(WhtRatePct: rate, WhtAmount: amount, ...)));
    }
}
```

### `ApiToolBase` — outbound HTTP

Receives `IHttpClientFactory`. Use named clients configured at startup.

```csharp
// Stage 4 — KYC screening calls an external API
public sealed class KycScreenHandler
    : ApiToolBase<KycScreenInput, KycScreenOutput>
{
    public KycScreenHandler(IHttpClientFactory httpClientFactory)
        : base(httpClientFactory) { }

    protected override async Task<ToolResponse<KycScreenOutput>> HandleAsync(
        ToolRequest<KycScreenInput> request, CancellationToken ct)
    {
        var client   = CreateClient("kyc-provider");  // named client from DI
        var response = await client.PostAsJsonAsync("/screen", request.Input, ct);
        // ...
    }
}
```

### `DatabaseToolBase` — read/write via UnitOfWork

Receives `IUnitOfWork`. Also typically takes `AppDbContext` directly for queries (EF's `DbContext` is `Scoped` — safe to inject when handler is `Transient` resolved inside a scope).

```csharp
// Stage 1 — Payee verification hits the database
public sealed class VerifyPayeeHandler
    : DatabaseToolBase<VerifyPayeeInput, VerifyPayeeOutput>
{
    private readonly AppDbContext _db;

    public VerifyPayeeHandler(IUnitOfWork unitOfWork, AppDbContext db)
        : base(unitOfWork) => _db = db;

    protected override async Task<ToolResponse<VerifyPayeeOutput>> HandleAsync(
        ToolRequest<VerifyPayeeInput> request, CancellationToken ct)
    {
        var payee = await _db.Set<PayeeRecord>()
            .FirstOrDefaultAsync(p => p.Id == request.Input.PayeeId, ct);

        if (payee is null)
            return ToolResponse<VerifyPayeeOutput>.Fail(...);

        // Persist state change
        payment.AttachVerifiedPayee(payee.Id);
        await UnitOfWork.SaveChangesAsync(ct);

        return ToolResponse<VerifyPayeeOutput>.Ok(...);
    }
}
```

### `CompositeToolBase` — orchestrates other tools

Receives `IToolExecutor`. Calls other registered tools as sub-steps. Stage 5 uses this to assemble the dossier from the outputs of earlier stages.

```csharp
public sealed class CompileApprovalDossierHandler
    : CompositeToolBase<CompileApprovalDossierInput, CompileApprovalDossierOutput>
{
    public CompileApprovalDossierHandler(IToolExecutor toolExecutor, AppDbContext db)
        : base(toolExecutor) => _db = db;

    protected override async Task<ToolResponse<CompileApprovalDossierOutput>> HandleAsync(
        ToolRequest<CompileApprovalDossierInput> request, CancellationToken ct)
    {
        // Can call other tools internally
        var sub = await ToolExecutor.ExecuteAsync<SubInput, SubOutput>(
            new ToolRequest<SubInput>(...), ct);
        // ...
    }
}
```

---

## 7. How are tool input and output payloads defined and validated?

### Definition

Input and output are plain C# `sealed record` types. `BuildJsonSchema<T>()` in `ToolHandlerBase` reflects these types at startup and produces a JSON Schema object automatically.

```csharp
public sealed record VerifyPayeeInput(Guid PaymentId, string PayeeRef);

// BuildJsonSchema<VerifyPayeeInput>() produces:
// {
//   "type": "object",
//   "properties": {
//     "paymentId": { "type": "string" },
//     "payeeRef":  { "type": "string" }
//   },
//   "required": ["paymentId", "payeeRef"]
// }
```

CLR type → JSON Schema mapping:

| CLR type | JSON Schema type |
|---|---|
| `string`, `Guid` | `"string"` |
| `decimal`, `double`, `float` | `"number"` |
| `int`, `long` | `"integer"` |
| `bool` | `"boolean"` |
| `T[]`, `List<T>` | `"array"` |
| `T?` (nullable) | same as `T` |
| any other class | `"object"` (no inner schema) |

### Validation

Input validation is handled by `ValidationBehavior` (behavior 1 in the MediatR pipeline) using FluentValidation. Add a validator class for any command that needs it:

```csharp
public sealed class ExecuteToolCommandValidator
    : AbstractValidator<ExecuteToolCommand>
{
    public ExecuteToolCommandValidator()
    {
        RuleFor(x => x.ToolNamespace).NotEmpty();
        RuleFor(x => x.ToolName).NotEmpty();
        RuleFor(x => x.ToolVersion).NotEmpty();
        RuleFor(x => x.Input).Must(e => e.ValueKind != JsonValueKind.Undefined)
            .WithMessage("Input must be a valid JSON element.");
    }
}
```

### Inside the handler — domain validation

Structural validation (required fields, types) happens at the pipeline level. Business rule validation happens inside `HandleAsync` and returns `ToolResponse.Fail` with a typed error:

```csharp
// ToolError factory methods
ToolError.NotFound("Payee not found.")          // 404 equivalent
ToolError.Validation("Payee is inactive.")      // 422 equivalent
ToolError.Unauthorized("Insufficient role.")    // 403 equivalent
ToolError.Conflict("Duplicate payment ref.")    // 409 equivalent
```

---

## 8. In how many ways can a tool be called?

All call paths eventually converge on `ISender.Send(ExecuteToolCommand)` and pass through the full MediatR behavior pipeline.

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         Callers                                             │
├─────────────┬────────────────┬────────────────┬──────────────┬─────────────┤
│  HTTP API   │ ScenarioRunner │  LLM Agent /   │    CLI       │  Internal   │
│  Controller │ (Orchestrator) │  ChatService   │  (ToolEngine │  Composite  │
│             │                │                │   .Cli)      │  Tool       │
└──────┬──────┴────────┬───────┴────────┬───────┴──────┬───────┴──────┬──────┘
       │               │                │              │              │
       └───────────────┴────────────────┴──────────────┴──────────────┘
                                        │
                          ISender.Send(ExecuteToolCommand)
                                        │
                    ┌───────────────────▼──────────────────────┐
                    │         MediatR Behavior Pipeline         │
                    │  1. ValidationBehavior  (input gates)     │
                    │  2. LoopDetectionBehavior  (loop guard)   │
                    │  3. ApprovalBehavior  (human gate)        │
                    │  4. AuditBehavior  (SOC 2 audit record)   │
                    └───────────────────┬──────────────────────┘
                                        │
                             IToolExecutor.ExecuteAsync
                                        │
                               IToolRegistry.Resolve
                                        │
                         IServiceScopeFactory.CreateAsyncScope
                                        │
                              Handler.HandleAsync
```

### Call path 1 — Direct HTTP

```
POST /api/v1/tools/execute
Body: { "toolNamespace": "payment", "toolName": "verify-payee", "toolVersion": "v1",
        "input": { "paymentId": "...", "payeeRef": "Acme" } }
```

### Call path 2 — Payment pipeline command

```csharp
// ProcessPaymentCommandHandler calls each stage via MediatR
await _mediator.Send(new ExecuteToolCommand(
    ToolNamespace: "payment",
    ToolName:      "verify-payee",
    ToolVersion:   "v1",
    Input:         ToJson(new VerifyPayeeInput(prid, payeeRef)),
    ...));
```

### Call path 3 — Scenario / ToolPlan

```csharp
// IToolPlanOrchestrator executes the full plan, wiring outputs between steps
await _orchestrator.ExecuteAsync(plan, userId, CallerType.System, ct: ct);
```

### Call path 4 — Composite tool (tool calling another tool)

```csharp
// Inside CompositeToolBase.HandleAsync
var result = await ToolExecutor.ExecuteAsync<SubInput, SubOutput>(
    new ToolRequest<SubInput>(Guid.NewGuid(), "sub-tool", "v1", input, "payment"), ct);
```

### Call path 5 — CLI

```bash
toolengine run payment.verify-payee@v1 --input '{"paymentId":"...","payeeRef":"Acme"}'
```

### Call path 6 — LLM / Chat agent

The `ChatService` (Phase L) calls `IToolPlanOrchestrator` with a plan built from LLM tool selection. The `WhenToUse` / `WhenNotToUse` fields on `ToolSchema` are embedded in the LLM system prompt to guide selection.

---

## 9. How do dependent tools share data — how does output from one tool feed into the next?

### The three parts

```
ToolStep.DependsOn      — declares ordering / prerequisite
ToolStep.OutputMappings — declares which prior output fields to inject, and where
StepContext             — the shared dictionary that holds all completed step outputs
```

### How it works step by step

```
Plan executes step-1-verify-payee
    ↓
step-1 returns:  { payeeId: "abc-123", legalName: "Acme", jurisdiction: "GB", ... }
    ↓
StepContext.SetStepOutput("step-1-verify-payee", output)
    ↓
step-2-ppm-check is ready (DependsOn: ["step-1-verify-payee"] — satisfied)
    ↓
StepContext.ResolveInput(step2.Input, step2.OutputMappings)
    Static input:   { paymentId: "...", ppmId: "PPM-001", verifiedPayeeId: "00000000-...", ... }
    Mappings:       { "verifiedPayeeId": "step-1-verify-payee.payeeId" }
    Resolved input: { paymentId: "...", ppmId: "PPM-001", verifiedPayeeId: "abc-123", ... }
                                                                               ↑
                                                         Guid.Empty placeholder replaced
    ↓
step-2 handler receives the resolved input with the real payeeId
```

### Output mapping syntax

```
"targetField": "sourceStepId.fieldPath"

Examples:
  "verifiedPayeeId"  → "step-1-verify-payee.payeeId"
  "payeeJurisdiction"→ "step-1-verify-payee.jurisdiction"
  "deepField"        → "step-1-verify-payee.nested.inner.value"   ← dot-separated path

The mapping OVERWRITES any placeholder in the static input.
```

### Scenario: compliance check (4 steps, all pulling from step-1)

```
step-1: verify-payee
        output → { payeeId, legalName, jurisdiction, entityType, ... }
           │
           ├──── step-2: ppm-check
           │             mapping: verifiedPayeeId ← step-1.payeeId
           │
           ├──── step-3: calculate-wht
           │             mapping: payeeJurisdiction ← step-1.jurisdiction
           │             (runs in parallel with step-2 in DAG mode)
           │
           └──── step-4: kyc-screen
                         mapping: payeeId           ← step-1.payeeId
                         mapping: payeeLegalName    ← step-1.legalName
                         mapping: payeeJurisdiction ← step-1.jurisdiction
                         mapping: entityType        ← step-1.entityType
                         DependsOn: [step-1, step-3]  ← waits for both
```

### Execution modes

| Mode | Behaviour |
|---|---|
| `Sequential` | Steps run one at a time in declaration order. Stop on first failure. |
| `Parallel` | All steps run concurrently via `Task.WhenAll`. No dependency enforcement — caller is responsible for ordering. |
| `Dag` | Topological sort via `DependsOn`. Each iteration runs all steps whose dependencies have completed successfully, as a parallel batch. Unresolvable steps (dependency failed) receive a synthetic failure result. |

### Suspend and resume across a plan

When a step triggers the approval gate (e.g. Stage 5), the orchestrator:
1. Returns `OrchestratorResult.Suspended` with the `PendingApprovalId` and `SuspendedAtStepId`
2. Serialises `StepContext` (all prior step outputs) to JSON and persists it on `ScenarioExecution`
3. On resume, deserialises `StepContext` and starts execution at `suspendedIndex + 1` — the suspended step is not re-run (it already submitted the approval request)

---

## 10. How do I read and understand an existing payment tool?

Every payment tool follows the same structure. Read it top to bottom:

```
Stage N handler file
│
├── 1. Input record      ← what the caller must provide
├── 2. Output record     ← what the handler returns on success
├── 3. Class declaration ← which base type, what it injects
├── 4. Identity props    ← Namespace, Name, Version → registry key
├── 5. Schema property   ← Description, WhenToUse, WhenNotToUse, Examples
└── 6. HandleAsync body
        ├── Barrier checks (return Fail if blocked)
        ├── Business logic
        ├── State mutation (payment.SomeMethod + SaveChangesAsync)
        └── return Ok(output)
```

### Walk-through: Stage 1 `VerifyPayeeHandler`

```csharp
// ① What goes in
public sealed record VerifyPayeeInput(Guid PaymentId, string PayeeRef);

// ② What comes out on success
public sealed record VerifyPayeeOutput(
    Guid PayeeId, string LegalName, string Jurisdiction,
    string EntityType, string Status, bool HasCompleteBankDetails,
    string? SwiftBic, string? Iban, string Message);

// ③ Base type = DatabaseToolBase → needs IUnitOfWork + AppDbContext
public sealed class VerifyPayeeHandler
    : DatabaseToolBase<VerifyPayeeInput, VerifyPayeeOutput>

// ④ Registry key → "payment.verify-payee@v1"
public override string Namespace => "payment";
public override string Name      => "verify-payee";
public override string Version   => "v1";

// ⑤ Schema tells you WHEN to call this and what it does
public override ToolSchema Schema => new(
    Description:  "Verifies the payee exists and is eligible to receive payment.",
    WhenToUse:    "Call immediately after payment.initiate.",
    WhenNotToUse: "Not for payee onboarding or KYC screening.",
    ...);

// ⑥ HandleAsync: four barrier checks, then success path
//    Barrier 1 — NOT FOUND      → Block(BlockedUnknownPayee) + Fail
//    Barrier 2 — INACTIVE       → Block(BlockedInactivePayee) + Fail
//    Barrier 3 — PENDING_REVIEW → Block(ExceptionQueue) + Fail
//    Barrier 4 — INCOMPLETE BANK DETAILS → Block(ExceptionQueue) + Fail
//    Pass: payment.AttachVerifiedPayee(payee.Id) + SaveChanges + Ok
```

### Payment pipeline — all 8 stages at a glance

```
Stage 0 │ payment.initiate@v1       │ LogicToolBase    │ Validates and creates PaymentInstruction
Stage 1 │ payment.verify-payee@v1   │ DatabaseToolBase │ Confirms payee is known, active, has bank details
Stage 2 │ payment.ppm-check@v1      │ DatabaseToolBase │ Validates PPM contract covers this service + amount
Stage 3 │ payment.calculate-wht@v1  │ LogicToolBase    │ Computes withholding tax (stub: 0%)
Stage 4 │ payment.kyc-screen@v1     │ ApiToolBase      │ Screens payee against sanctions/AML lists (stub)
Stage 5 │ payment.compile-dossier@v1│ CompositeToolBase│ Assembles dossier + triggers approval gate (HTTP 202)
Stage 6 │ payment.execute-payment@v1│ ApiToolBase      │ Sends payment instruction to bank (stub)
Stage 7 │ payment.reconcile@v1      │ DatabaseToolBase │ Writes final reconciliation record + marks Complete
```

### What "stub" means

Three handlers (Stages 3, 4, 6) are marked stub. They implement the full interface and return a valid response, but the internal logic is simplified:

- Stage 3 always returns 0% WHT
- Stage 4 always returns `CONFIRMED_MATCH` (no block)
- Stage 6 returns a mock bank transaction ID

The contracts, schema, and pipeline wiring are production-ready. Only the underlying calculation or API call is not yet connected to a real provider.

### Status flow through the pipeline

```
Created
  └─ Stage 0 PASS → InProgress
       └─ Stage 1 PASS → PayeeVerified
            └─ Stage 1 FAIL → BlockedUnknownPayee / BlockedInactivePayee / ExceptionQueue
       └─ Stage 2 PASS → ContractVerified
            └─ Stage 2 FAIL → BlockedNoContract / BlockedContractExpired / ExceptionQueue
       └─ Stage 3 PASS → TaxCalculated
            └─ Stage 3 REVIEW → HeldTaxReview
       └─ Stage 4 PASS → KycCleared
            └─ Stage 4 FAIL → BlockedKyc
       └─ Stage 5 → PendingApproval (HTTP 202)
            └─ Approved
                 └─ Stage 6 PASS → PaymentDispatched
                      └─ Stage 7 PASS → Reconciled / Complete
            └─ Denied → ApprovalDenied
```

---

*Document maintained by ONE BCG Engineering. Update alongside code changes in `Payment.Tools`.*

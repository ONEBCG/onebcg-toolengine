---
name: toolengine-phase2-tools
description: >
  Scaffolds Phase 2 of ToolEngine v2026: tool abstractions (ITool, IToolHandler<,>,
  base classes, ToolSchema with WhenToUse/WhenNotToUse/Examples), the versioned
  ToolRegistry with namespace-aware resolution, and four sample tools covering all
  ToolTypes. Defines ToolSummaryResponse flat DTO for API serialisation and the
  LlmProviderAttribute for per-tool LLM routing (Phase L).
classification: Confidential - Internal Use Only
---

# Phase 2 — Tool Abstractions, Registry + Samples

## Prerequisites

Phase 1 complete. `dotnet build` passes with zero warnings on:
- `ToolEngine.Core.Abstractions`
- `ToolEngine.Core.Domain`

---

## What this phase produces

```
src/
  Tools/
    ToolEngine.Tools.Abstractions/   ← ITool, IToolHandler<,>, base classes, attributes
    ToolEngine.Tools.Registry/       ← versioned registry, namespace-aware resolution
    ToolEngine.Tools.Samples/        ← 4 sample tools, one per ToolType
```

---

## Step-by-step scaffold

```bash
dotnet new classlib -n ToolEngine.Tools.Abstractions \
  -o src/Tools/ToolEngine.Tools.Abstractions --framework net8.0
dotnet new classlib -n ToolEngine.Tools.Registry \
  -o src/Tools/ToolEngine.Tools.Registry --framework net8.0
dotnet new classlib -n ToolEngine.Tools.Samples \
  -o src/Tools/ToolEngine.Tools.Samples --framework net8.0

dotnet sln add src/Tools/ToolEngine.Tools.Abstractions
dotnet sln add src/Tools/ToolEngine.Tools.Registry
dotnet sln add src/Tools/ToolEngine.Tools.Samples

# References
dotnet add src/Tools/ToolEngine.Tools.Abstractions reference \
  src/Core/ToolEngine.Core.Abstractions \
  src/Core/ToolEngine.Core.Domain
dotnet add src/Tools/ToolEngine.Tools.Registry reference \
  src/Tools/ToolEngine.Tools.Abstractions \
  src/Core/ToolEngine.Core.Domain
dotnet add src/Tools/ToolEngine.Tools.Samples reference \
  src/Tools/ToolEngine.Tools.Abstractions \
  src/Tools/ToolEngine.Tools.Registry \
  src/Core/ToolEngine.Core.Domain
```

---

## NuGet packages

**Tools.Abstractions:** no packages — BCL only.

**Tools.Registry:**
```xml
<PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="8.*" />
```

**Tools.Samples:**
```xml
<PackageReference Include="System.Net.Http.Json" Version="8.*" />
```

---

## File layout — ToolEngine.Tools.Abstractions

```
Interfaces/
  IToolHandler.cs         — ExecuteAsync(ToolRequest<TInput>, ct) → ToolResponse<TOutput>
  IToolExecutor.cs        — ExecuteAsync<TInput,TOutput>(ToolRequest<TInput>, ct)
  IToolPlanExecutor.cs    — ExecuteAsync(ToolPlan, tenantId, ct) → IReadOnlyList<ToolPlanResult>
  IHumanApprovalGate.cs   — RequestApprovalAsync(ApprovalContext, ct) → ApprovalDecision
  IToolRegistry.cs        — Register, Resolve, ListAll, ListForTenant
  IToolDiscovery.cs       — DiscoverAsync(ct) → IReadOnlyList<ToolDescriptor>
Base/
  LogicToolBase.cs        — abstract base for ToolType.Logic (pure computation, no I/O)
  ApiToolBase.cs          — abstract base for ToolType.Api (injects IHttpClientFactory)
  DatabaseToolBase.cs     — abstract base for ToolType.Database (injects IUnitOfWork)
  CompositeToolBase.cs    — abstract base for ToolType.Composite (injects IToolExecutor)
Models/
  ToolSchema.cs           — Description, WhenToUse, WhenNotToUse, Examples[], InputSchema, OutputSchema
  ToolDescriptor.cs       — FullName, Namespace, Name, Version, Type, IsEnabled, TenantId, HandlerType, Schema
  ToolSummaryResponse.cs  — FLAT DTO returned by GET /tools (NO nested sub-object)
  ToolPlan.cs             — PlanId, Mode(ExecutionMode), Steps
  ToolStep.cs             — StepId, Namespace, ToolName, Version, Input(JsonElement), DependsOn(string[])
  ToolPlanResult.cs       — StepId, Success, Data(JsonElement?), Error(ToolError?)
  ApprovalContext.cs      — all approval context fields including IdempotencyKey, CallerType
  ApprovalDecision.cs     — Allowed/Denied/Suspended discriminated union + InvocationId
Attributes/
  RequiresApprovalAttribute.cs — Risk, Channel, Reason
  LlmProviderAttribute.cs      — Provider string (per-tool LLM routing override, Phase L)
```

---

## Key models

### ToolSchema.cs

```csharp
namespace ToolEngine.Tools.Abstractions.Models;

// WhenToUse and WhenNotToUse are embedded into LLM tool descriptions by
// ToolSchemaConverter (Phase L) to improve tool selection accuracy ~30%
public sealed record ToolSchema(
    string      Description,
    string      WhenToUse,
    string      WhenNotToUse,
    string[]    Examples,
    JsonElement InputSchema,
    JsonElement OutputSchema);
```

### ToolSummaryResponse.cs — FLAT shape (critical)

```csharp
namespace ToolEngine.Tools.Abstractions.Models;

// FLAT record — matches the shape returned by GET /tools.
// NO nested metadata sub-object. The frontend TypeScript interface must mirror this exactly.
// Contract drift (e.g. adding a metadata wrapper) breaks the UI silently:
// tool.metadata.name returns undefined, buttons render blank.
public sealed record ToolSummaryResponse(
    string      FullName,
    string      Namespace,
    string      Name,
    string      Version,
    string      Description,
    int         Type,        // int, not enum, for JSON serialisation simplicity
    bool        IsEnabled,
    string?     TenantId,
    JsonElement InputSchema,
    JsonElement OutputSchema);
```

### ToolDescriptor.cs

```csharp
namespace ToolEngine.Tools.Abstractions.Models;

public sealed class ToolDescriptor
{
    public string    Namespace   { get; init; } = default!;
    public string    Name        { get; init; } = default!;
    public string    Version     { get; init; } = default!;
    public ToolType  Type        { get; init; }
    public bool      IsEnabled   { get; init; } = true;
    public string?   TenantId    { get; init; }
    public Type      HandlerType { get; init; } = default!;
    public ToolSchema Schema     { get; init; } = default!;

    // Always "namespace.name" — used as registry lookup key
    public string FullName => string.IsNullOrEmpty(Namespace) ? Name : $"{Namespace}.{Name}";
}
```

### ApprovalContext.cs

```csharp
namespace ToolEngine.Tools.Abstractions.Models;

public sealed record ApprovalContext(
    string         ToolFullName,
    ApprovalRisk   Risk,
    ApprovalChannel Channel,
    string         TenantId,
    string?        UserId,
    string?        Reason,
    string?        IdempotencyKey,   // F8 — passed to AsyncApprovalGate for dedup
    CallerType     CallerType,       // H4 — persisted on PendingApproval
    AcknowledgementStatement? AcknowledgementStatement); // H3
```

### Attributes

```csharp
// RequiresApprovalAttribute.cs
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RequiresApprovalAttribute(
    ApprovalRisk    Risk    = ApprovalRisk.Medium,
    ApprovalChannel Channel = ApprovalChannel.Dashboard,
    string          Reason  = "This action requires human approval.") : Attribute;

// LlmProviderAttribute.cs — Phase L per-tool routing override
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class LlmProviderAttribute(string Provider) : Attribute;
// Usage: [LlmProvider("ollama")] on a handler class forces all LLM selections
// of that tool to route to the specified provider, regardless of tenant config
// or global default. Used for data-residency constraints.
```

---

## File layout — ToolEngine.Tools.Registry

```
ToolRegistry.cs                     — ConcurrentDictionary keyed by "ns.name@version"
ToolDiscovery.cs                    — scans DI for IToolHandler<,> registrations
Extensions/
  ServiceCollectionExtensions.cs    — AddToolRegistry()
```

### ToolRegistry.cs — namespace-aware resolution

```csharp
namespace ToolEngine.Tools.Registry;

public sealed class ToolRegistry : IToolRegistry
{
    // Key format: "namespace.name@version" (all lowercase)
    private readonly ConcurrentDictionary<string, ToolDescriptor> _tools = new();

    public void Register(ToolDescriptor descriptor) =>
        _tools[MakeKey(descriptor.FullName, descriptor.Version)] = descriptor;

    public Result<ToolDescriptor> Resolve(string fullName, string version, string tenantId)
    {
        var key = MakeKey(fullName, version);
        return _tools.TryGetValue(key, out var descriptor)
            ? Result<ToolDescriptor>.Success(descriptor)
            : Result<ToolDescriptor>.Failure(Error.NotFound("Tool", fullName));
    }

    public IReadOnlyList<ToolDescriptor> ListForTenant(string tenantId) =>
        _tools.Values
            .Where(d => d.IsEnabled && (d.TenantId is null || d.TenantId == tenantId))
            .ToList();

    private static string MakeKey(string fullName, string version) =>
        $"{fullName.ToLowerInvariant()}@{version.ToLowerInvariant()}";
}
```

### ServiceCollectionExtensions (Registry)

```csharp
public static IServiceCollection AddToolRegistry(this IServiceCollection services)
{
    services.AddSingleton<IToolRegistry, ToolRegistry>();
    services.AddSingleton<IToolDiscovery, ToolDiscovery>();
    return services;
}
```

---

## File layout — ToolEngine.Tools.Samples

```
Math/
  CalculateHandler.cs     — LogicToolBase; ns=math; name=calculate; v1
  CalculateInput.cs       — A(double), B(double), Operator(string: add/subtract/multiply/divide)
  CalculateOutput.cs      — Result(double)
Weather/
  WeatherHandler.cs       — ApiToolBase; ns=weather; name=current; v1
  WeatherInput.cs         — City(string)
  WeatherOutput.cs        — City, TempC(double), Condition(string)
UserLookup/
  UserLookupHandler.cs    — DatabaseToolBase; ns=hr; name=user-lookup; v1; uses IUnitOfWork
  UserLookupInput.cs      — UserId(string)
  UserLookupOutput.cs     — UserId, Name, Email, Department
Report/
  ReportHandler.cs        — CompositeToolBase; ns=report; name=generate; v1; [RequiresApproval(High)]
  ReportInput.cs          — Title(string), Sections(string[])
  ReportOutput.cs         — ReportId(Guid), GeneratedAt
Extensions/
  ServiceCollectionExtensions.cs — AddToolSamples()
```

### CalculateHandler.cs (LogicToolBase example)

```csharp
namespace ToolEngine.Tools.Samples.Math;

public sealed class CalculateHandler : LogicToolBase<CalculateInput, CalculateOutput>
{
    public override string Namespace => "math";
    public override string Name      => "calculate";
    public override string Version   => "v1";

    public override ToolSchema Schema => new(
        Description:  "Performs basic arithmetic operations on two numbers.",
        WhenToUse:    "Use when the user asks to add, subtract, multiply, or divide two numbers.",
        WhenNotToUse: "Do not use for complex expressions, algebra, or non-numeric inputs.",
        Examples:     ["calculate 10 + 5", "what is 37 times 48?"],
        InputSchema:  BuildJsonSchema<CalculateInput>(),
        OutputSchema: BuildJsonSchema<CalculateOutput>());

    protected override Task<ToolResponse<CalculateOutput>> HandleAsync(
        ToolRequest<CalculateInput> request, CancellationToken ct)
    {
        var (a, b, op) = (request.Input.A, request.Input.B, request.Input.Operator.ToLower());
        var result = op switch {
            "add"      => a + b,
            "subtract" => a - b,
            "multiply" => a * b,
            "divide"   => b == 0
                ? throw new ArgumentException("Division by zero.")
                : a / b,
            _ => throw new ArgumentException($"Unknown operator: {op}")
        };
        return Task.FromResult(ToolResponse<CalculateOutput>.Ok(
            request.CorrelationId, new CalculateOutput(result)));
    }
}
```

### UserLookupHandler.cs (DatabaseToolBase — tests Phase 3 DI scope)

```csharp
namespace ToolEngine.Tools.Samples.UserLookup;

// IMPORTANT: UserLookupHandler depends on IUnitOfWork (scoped service).
// This validates the ToolExecutor IServiceScopeFactory pattern (Phase 3 / Phase M1).
// If ToolExecutor resolves from root IServiceProvider, this handler throws at runtime.
public sealed class UserLookupHandler : DatabaseToolBase<UserLookupInput, UserLookupOutput>
{
    private readonly IUnitOfWork _unitOfWork;

    public UserLookupHandler(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public override string Namespace => "hr";
    public override string Name      => "user-lookup";
    public override string Version   => "v1";

    // ... Schema, HandleAsync
}
```

### ReportHandler.cs ([RequiresApproval] example — H3 EU AI Act path)

```csharp
namespace ToolEngine.Tools.Samples.Report;

[RequiresApproval(
    Risk:    ApprovalRisk.High,
    Channel: ApprovalChannel.Dashboard,
    Reason:  "Report generation triggers downstream data exports and cannot be reversed.")]
public sealed class ReportHandler : CompositeToolBase<ReportInput, ReportOutput>
{
    // ...
}
```

### ServiceCollectionExtensions (Samples)

```csharp
public static IServiceCollection AddToolSamples(this IServiceCollection services)
{
    // Register as Transient — handlers may depend on scoped services (IUnitOfWork)
    // Singleton or Scoped would cause captive dependency issues
    services.AddTransient<CalculateHandler>();
    services.AddTransient<WeatherHandler>();
    services.AddTransient<UserLookupHandler>();
    services.AddTransient<ReportHandler>();
    return services;
}
```

---

## Frontend TypeScript contract

The following TypeScript interface in `src/frontend/src/types.ts` MUST match `ToolSummaryResponse` exactly:

```typescript
export interface ToolDescriptor {
  fullName:    string   // "math.calculate"
  namespace:   string   // "math"
  name:        string   // "calculate"
  version:     string   // "v1"
  description: string
  type:        number   // int (not enum)
  isEnabled:   boolean
  tenantId:    string | null
  inputSchema: Record<string, unknown>
  outputSchema: Record<string, unknown>
}
```

All component references must use `tool.name`, `tool.namespace`, `tool.version` directly.
Never use `tool.metadata.name` — there is no metadata sub-object.

---

## Phase 2 completion checklist

- [ ] `dotnet build` zero warnings on all 3 new projects
- [ ] `ToolSummaryResponse` is a flat record — NO nested metadata sub-object
- [ ] `ToolDescriptor.FullName` = `"namespace.name"` (dot separator, all lowercase)
- [ ] `WhenToUse` and `WhenNotToUse` present in `ToolSchema` (required by Phase L `ToolSchemaConverter`)
- [ ] `LlmProviderAttribute` present in `Attributes/` (required by Phase L provider routing)
- [ ] `RequiresApprovalAttribute` applied to `ReportHandler` (tests approval pipeline)
- [ ] `UserLookupHandler` constructor accepts `IUnitOfWork` (validates Phase 3 DI scope fix)
- [ ] `AddToolSamples()` registers handlers as `Transient` (not Singleton — scoped dep safety)
- [ ] `ToolSummaryResponse.Type` is `int` (not enum) for JSON serialisation
- [ ] `IHumanApprovalGate` interface present in `Interfaces/` (implementations in Phase 3 + Phase 4)
- [ ] `ApprovalContext` includes `IdempotencyKey` (F8) and `CallerType` (H4) fields
- [ ] `IToolRegistry.ListForTenant` returns only enabled tools scoped to the tenant

---

*Confidential — Internal Use Only | ONE BCG | ToolEngine v2026*

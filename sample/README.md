# ToolEngine v2026 — B2B Payment Processing POC

**ONE BCG** · .NET 8 · Modular Monolith · Clean Architecture

A fully working proof-of-concept demonstrating an AI-ready, governance-first B2B payment processing engine. Built on a tool registry, MediatR behavior pipeline, scenario orchestration layer, and provider-agnostic infrastructure.

---

## What This Demonstrates

| Capability | Implementation |
|------------|---------------|
| 8-stage governed payment pipeline | MediatR + tool handlers per stage |
| Human-in-the-loop approval gate | `[RequiresApproval]` → HTTP 202 → resume |
| 5 UI execution modes | Pipeline · Tool Plan · LLM Chat · Single Tool · Scenarios |
| Named scenario orchestration | Declarative multi-step flows with inter-step data flow |
| Inter-step output mapping | `StepContext` — step A's output feeds step B's input |
| LLM agent tool use | Provider-agnostic `ILlmProvider` — Claude and OpenAI supported |
| Provider-agnostic infrastructure | DB: SQLite/SQL Server/Postgres · Cache: Memory/Redis · LLM: Claude/OpenAI |
| Full audit trail | `PaymentAuditLog` per stage, 7-year retention |
| EU AI Act Article 14 compliance | `AcknowledgementStatement` for High/Critical risk tools |

---

## Solution Structure

```
src/
├── Core/
│   ├── ToolEngine.Core.Abstractions   # BCL only. ICacheProvider, ILlmProvider, ISecretVault, IUnitOfWork
│   └── ToolEngine.Core.Domain         # Result<T>, entities, enums, contracts
│
├── Tools/
│   ├── ToolEngine.Tools.Abstractions  # IToolHandler, base classes, ToolSchema, IScenarioDefinition,
│   │                                  # IToolPlanOrchestrator, StepContext, OrchestratorResult
│   ├── ToolEngine.Tools.Registry      # In-memory IToolRegistry
│   └── ToolEngine.Tools.Executor      # IToolExecutor (reflection + scoped DI), IToolPlanExecutor
│
├── Application/
│   └── ToolEngine.Application         # MediatR behaviors + ExecuteToolCommand +
│                                      # ScenarioRunner, ScenarioRegistry, ToolPlanOrchestrator
│
├── Infrastructure/
│   └── ToolEngine.Infrastructure      # EF Core, provider selection (DB/Cache/LLM),
│                                      # ClaudeProvider, OpenAiProvider, RedisCacheProvider,
│                                      # AsyncApprovalGate, migrations
│
├── Modules/
│   └── Payment/
│       ├── ToolEngine.Payment.Domain         # PaymentInstruction, PayeeRecord, PpmContract…
│       ├── ToolEngine.Payment.Tools          # 8 stage handlers + 4 scenario definitions
│       ├── ToolEngine.Payment.Application    # ProcessPaymentCommand, ResumePaymentCommand, queries
│       ├── ToolEngine.Payment.Infrastructure # EF configs, PaymentModuleSeeder
│       └── ToolEngine.Payment.Api            # PaymentsController + AddPaymentModule() entry point
│
└── Hosts/
    ├── ToolEngine.Api   # Compositor: engine + modules. JWT, CORS, Swagger. Port 5000
    ├── ToolEngine.Ui    # Standalone demo UI (static files + /config endpoint). Port 5001
    └── ToolEngine.Cli   # Interactive console demo client
```

---

## Payment Pipeline — 8 Stages

| Stage | Tool | Type | Notes |
|-------|------|------|-------|
| 0 | `payment.initiate` | Logic | Input validation |
| 1 | `payment.verify-payee` | Database | Payee lookup, status, bank details check |
| 2 | `payment.ppm-check` | Database | PPM contract, service type, amount, currency caps |
| 3 | `payment.calculate-wht` | Logic | **Stub** — returns 0% WHT |
| 4 | `payment.kyc-screen` | API | **Stub** — `NoMatch` unless payee name contains "Risq" |
| 5 | `payment.compile-dossier` | Composite | `[RequiresApproval(High)]` — triggers approval gate → HTTP 202 |
| 6 | `payment.execute-payment` | API | **Stub** — mock bank transaction |
| 7 | `payment.reconcile` | Database | Marks payment settled |

---

## MediatR Behavior Pipeline

Every tool invocation — regardless of execution mode — passes through these four behaviors in order:

```
1. ValidationBehavior       ← FluentValidation input gates
2. LoopDetectionBehavior    ← Distributed loop guard (5 calls / 5 min per correlation)
3. ApprovalBehavior         ← [RequiresApproval] gate — returns HTTP 202 Suspended
4. AuditBehavior            ← SOC 2 append-only ToolInvocationRecord per invocation
```

---

## Quick Start

### Prerequisites
- .NET 8 SDK
- No database server — SQLite file created automatically at `src/Hosts/ToolEngine.Api/toolengine.db`

### Terminal 1 — API (port 5000)
```bash
cd src/Hosts/ToolEngine.Api
dotnet run
```

### Terminal 2 — Demo UI (port 5001)
```bash
cd src/Hosts/ToolEngine.Ui
dotnet run
# Open http://localhost:5001
```

### Terminal 3 (optional) — CLI
```bash
cd src/Hosts/ToolEngine.Cli
dotnet run
# Connects to http://localhost:5000 by default
```

On first API startup: database is created, migrations applied, test data seeded automatically.

| URL | Description |
|-----|-------------|
| `http://localhost:5001` | Demo UI — 5 execution mode tabs |
| `http://localhost:5000/swagger` | Swagger / OpenAPI |
| `http://localhost:5000/health` | Health check |

---

## Provider Configuration

All infrastructure providers are selected from `appsettings.json` — no code changes needed to switch.

### Database
```json
"Database": {
  "Provider": "sqlite",
  "ConnectionString": "Data Source=toolengine.db"
}
```
| Value | Description |
|-------|-------------|
| `sqlite` | Local file — default for development |
| `sqlserver` | Microsoft SQL Server |
| `postgres` | PostgreSQL via Npgsql |

### Cache
```json
"Cache": {
  "Provider": "memory",
  "Redis": { "ConnectionString": "localhost:6379", "InstanceName": "toolengine:" }
}
```
| Value | Description |
|-------|-------------|
| `memory` | In-process memory cache — default for development |
| `redis` | StackExchange.Redis distributed cache |

### LLM
```json
"LLM": {
  "Provider": "claude",
  "Claude": { "ApiKey": "", "Model": "claude-sonnet-4-5" },
  "OpenAI": { "ApiKey": "", "Model": "gpt-4o" }
}
```
| Value | Description |
|-------|-------------|
| `claude` | Anthropic Claude via Messages API |
| `openai` | OpenAI via Chat Completions API |

API keys can be set in appsettings **or** via environment variables:
- `ANTHROPIC_API_KEY` — for Claude
- `OPENAI_API_KEY` — for OpenAI

Without an API key, the LLM Chat tab returns a "not configured" message. All other execution modes work without an LLM key.

---

## API Endpoints

### Payments
| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/api/v1/payments` | Initiate payment (Stages 0–5). Returns 200, 202, or 422 |
| `GET`  | `/api/v1/payments/{prid}` | Payment status |
| `GET`  | `/api/v1/payments/{prid}/audit` | Full stage-by-stage audit trail |
| `POST` | `/api/v1/payments/{prid}/resume` | Resume after approval (runs Stages 6–7) |

### Approvals
| Method | Path | Description |
|--------|------|-------------|
| `GET`  | `/api/v1/approvals` | List pending approvals |
| `GET`  | `/api/v1/approvals/{id}` | Approval details |
| `POST` | `/api/v1/approvals/{id}/approve` | Approve with token |
| `POST` | `/api/v1/approvals/{id}/deny` | Deny with reason |
| `GET`  | `/api/v1/approvals/{id}/token` | **[DEV ONLY]** Raw token for testing |

### Tools
| Method | Path | Description |
|--------|------|-------------|
| `GET`  | `/api/v1/tools` | List all registered tools with schemas |
| `GET`  | `/api/v1/tools/{ns}/{name}/{version}` | Single tool detail |
| `POST` | `/api/v1/tools/invoke` | Invoke any tool through full MediatR pipeline |

### Plans
| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/api/v1/plans/execute` | Execute a ToolPlan (Sequential / Parallel / DAG) |

### Scenarios
| Method | Path | Description |
|--------|------|-------------|
| `GET`  | `/api/v1/scenarios` | List all registered scenario definitions |
| `POST` | `/api/v1/scenarios/{name}/run` | Run a named scenario |
| `POST` | `/api/v1/scenarios/{id}/resume` | Resume a suspended scenario |

### Chat
| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/api/v1/chat` | Natural language → LLM agent → tool calls → response |

### Dev / Health
| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/dev/token` | Generate JWT (Development only) |
| `GET`  | `/health` | Health check |

---

## Seeded Test Data

| Entity | ID / Reference | Description |
|--------|---------------|-------------|
| Payee 1 | `11111111-...` | **Acme Consulting Ltd** — GB, Active, IBAN + SWIFT |
| Payee 2 | `22222222-...` | **Horizon Advisory GmbH** — DE, Active |
| Payee 3 | `33333333-...` | **Risq Capital Ltd** — US, Active — triggers KYC block |
| PPM-001 | `PPM-001` | Active · Acme · GBP/USD/EUR · £250k single-tx cap |
| PPM-002 | `PPM-002` | **Expired** — triggers Stage 2 block |
| PPM-003 | `PPM-003` | Active · Risq Capital · USD/GBP · $100k cap |

---

## Demo Scenarios

### UI Tab 1 — Pipeline (Hardcoded Handler)

| Scenario | Payee | PPM | Amount | Expected |
|----------|-------|-----|--------|---------|
| Happy path | Acme Consulting | PPM-001 | GBP 5,000 | HTTP 202 — suspended at Stage 5 |
| Expired PPM | Horizon Advisory | PPM-002 | USD 10,000 | HTTP 422 — Stage 2 `CONTRACT_INACTIVE` |
| KYC block | Risq Capital | PPM-003 | USD 1,000 | HTTP 422 — Stage 4 `KYC_CONFIRMED_MATCH` |
| Over limit | Acme Consulting | PPM-001 | GBP 300,000 | HTTP 422 — Stage 2 `CONTRACT_AMOUNT_EXCEEDED` |

### UI Tab 5 — Scenarios (Orchestrated)

| Scenario name | Steps | Demonstrates |
|---------------|-------|-------------|
| `payment.compliance-check` | 1→2→3→4 | Full output mapping: payeeId, jurisdiction, entityType flow from Stage 1 into Stages 2, 3, 4 |
| `payment.expired-ppm` | 1→2 | Blocked at Stage 2 (expired PPM) |
| `payment.kyc-block` | 1→2→3→4 | Blocked at Stage 4 (KYC match) |
| `payment.over-limit` | 1→2 | Blocked at Stage 2 (transaction cap) |

---

## Complete API Walk-through (Happy Path)

### 1. Get a dev JWT
```bash
curl -X POST http://localhost:5000/dev/token \
  -H "Content-Type: application/json" \
  -d '{"userId":"demo-user","userName":"Demo User"}'
```
Copy the `token` value. Use `Bearer {token}` in all subsequent calls.

### 2. Initiate payment
```bash
curl -X POST http://localhost:5000/api/v1/payments \
  -H "Authorization: Bearer {token}" \
  -H "Content-Type: application/json" \
  -d '{
    "payerName": "ONE BCG UK Ltd", "payerJurisdiction": "GB",
    "payerEntityId": "PAYER-ONEBCG-001", "payeeRef": "Acme Consulting",
    "grossAmount": 5000.00, "currency": "GBP", "serviceType": 0, "ppmId": "PPM-001"
  }'
```
Returns **HTTP 202** with `prid` and `pendingApprovalId`.

### 3. Get approval token (dev only)
```bash
curl http://localhost:5000/api/v1/approvals/{pendingApprovalId}/token \
  -H "Authorization: Bearer {token}"
```

### 4. Approve
```bash
curl -X POST http://localhost:5000/api/v1/approvals/{pendingApprovalId}/approve \
  -H "Authorization: Bearer {token}" \
  -H "Content-Type: application/json" \
  -d '{"approvalToken": "{token-from-step-3}"}'
```

### 5. Resume (runs Stages 6 + 7)
```bash
curl -X POST http://localhost:5000/api/v1/payments/{prid}/resume \
  -H "Authorization: Bearer {token}"
```
Returns **HTTP 200** with `status: "SETTLED"` and `bankTransactionId`.

---

## Adding a New Tool

Three steps:

**Step 1 — Input/Output types** in the tool's namespace:
```csharp
public sealed record MyToolInput(Guid PaymentId, string SomeParam);
public sealed record MyToolOutput(string Result, bool IsValid);
```

**Step 2 — Handler** inheriting from the appropriate base class:
```csharp
[RequiresApproval(ApprovalRisk.Low)]  // optional
public sealed class MyToolHandler : LogicToolBase<MyToolInput, MyToolOutput>
{
    public override string Namespace => "payment";
    public override string Name      => "my-tool";
    public override string Version   => "v1";

    // Schema is an abstract property — not a method
    public override ToolSchema Schema => new(
        Description:  "What this tool does.",
        WhenToUse:    "Use when X.",
        WhenNotToUse: "Do not use when Y.",
        Examples:     ["example prompt"],
        InputSchema:  BuildJsonSchema<MyToolInput>(),
        OutputSchema: BuildJsonSchema<MyToolOutput>());

    protected override async Task<ToolResponse<MyToolOutput>> HandleAsync(
        ToolRequest<MyToolInput> request, CancellationToken ct)
    {
        // ...your logic...
        return ToolResponse<MyToolOutput>.Ok(request.CorrelationId, new MyToolOutput(...));
    }
}
```

**Step 3 — Register** in `Payment.Tools/Extensions/ServiceCollectionExtensions.cs`:
```csharp
services.AddTransient<MyToolHandler>();
// In RegisterPaymentToolDescriptors:
await RegisterToolAsync<MyToolHandler>(provider, registry, ToolType.Logic);
```

The tool is then immediately available across all 5 UI tabs, the CLI, and as an LLM tool.

---

## Adding a New Scenario

```csharp
public sealed class MyScenario : IScenarioDefinition, IRequiresSetup
{
    public string Name        => "payment.my-scenario";
    public string Version     => "v1";
    public string Description => "Describe what this scenario does.";
    public JsonElement InputSchema => BuildSchema();

    // Create domain entities before the plan runs (inject PRID, etc.)
    public async Task<JsonElement> SetupAsync(
        JsonElement input, IServiceProvider services, CancellationToken ct)
    {
        // Create PaymentInstruction, inject prid into input...
        return inputWithPrid;
    }

    public ToolPlan Build(JsonElement input)
    {
        var prid = input.GetProperty("prid").GetGuid();
        return new ToolPlan(Guid.NewGuid(), ExecutionMode.Sequential,
        [
            new ToolStep("step-1-verify-payee", "payment", "verify-payee", "v1",
                ToJson(new { paymentId = prid, payeeRef = "Acme Consulting" }),
                DependsOn: []),

            new ToolStep("step-2-ppm-check", "payment", "ppm-check", "v1",
                ToJson(new { paymentId = prid, ppmId = "PPM-001",
                             verifiedPayeeId = Guid.Empty, serviceType = 0,
                             grossAmount = 5000m, currency = "GBP" }),
                DependsOn: ["step-1-verify-payee"],
                OutputMappings: new Dictionary<string, string>
                {
                    // Syntax: "targetField" = "sourceStepId.fieldName"
                    ["verifiedPayeeId"] = "step-1-verify-payee.payeeId"
                }),
        ]);
    }
}
```

Register in `Payment.Api/Extensions/ServiceCollectionExtensions.cs`:
```csharp
services.AddTransient<MyScenario>();
// In RegisterPaymentModuleAsync → RegisterPaymentScenarios:
registry.Register(sp.GetRequiredService<MyScenario>());
```

---

## Adding a New Module

The host composes modules via a single method call:
```csharp
// Program.cs — add one line per new domain
builder.Services.AddPaymentModule();
builder.Services.AddProcurementModule();   // future
builder.Services.AddHrModule();            // future
```

A module is a vertical slice: Domain → Tools → Application → Infrastructure → Api.

**Module project structure** (example: Procurement):
```
src/Modules/Procurement/
  ToolEngine.Procurement.Domain          # Entities, enums
  ToolEngine.Procurement.Tools           # Tool handlers + scenarios
  ToolEngine.Procurement.Application     # MediatR commands + queries
  ToolEngine.Procurement.Infrastructure  # EF configs + seeder
  ToolEngine.Procurement.Api             # Controllers + AddProcurementModule()
```

The module entry point (`AddProcurementModule`) wires everything:
```csharp
public static IServiceCollection AddProcurementModule(this IServiceCollection services)
{
    services.AddProcurementTools();               // handlers + scenarios
    services.AddProcurementApplicationServices(); // MediatR commands + validators
    services.AddProcurementInfrastructure();      // EF configs + seeder
    return services;
}
```

Tools and scenarios registered in this module are automatically available across all 5 UI tabs, the CLI, and to the LLM agent.

---

## Stub Expansion Guide

### WHT Engine (Stage 3)
1. Inject `IWhtRateRepository` querying the `WhtRateEntries` table
2. Replace stub body in `CalculateWhtHandler.HandleAsync`
3. `CalculateWhtInput` / `CalculateWhtOutput` are production-ready — no contract change

### KYC Screening (Stage 4)
1. Inject `ISecretVault` to retrieve WorldCheck / ComplyAdvantage API key
2. Replace stub logic in `KycScreenHandler.HandleAsync`
3. `KycScreeningRecord` persistence and `PaymentInstruction.ApplyKycResult` work as-is

### Bank Execution (Stage 6)
1. Inject `ISecretVault` for bank API credentials
2. Construct ISO 20022 / MT103 message per `PaymentRail` enum
3. Return real `BankTransactionId` — Stage 7 uses it for matching

### LLM (Chat)
Set `ANTHROPIC_API_KEY` or `OPENAI_API_KEY` in `launchSettings.json` (local) or environment. Switch provider with `LLM:Provider` in appsettings.

---

## Project Conventions

| Convention | Value |
|-----------|-------|
| Tool namespace | `payment` |
| Tool version | `v1` |
| Stage tool name constants | `PaymentPipeline.Stage.*` |
| Error code constants | `PaymentErrorCodes.*` |
| Scenario naming | `{domain}.{kebab-case}` |
| Step ID convention | `step-{n}-{tool-name}` |
| Error code format | `SCREAMING_SNAKE_CASE` |

---

*ONE BCG ToolEngine v2026 — B2B Payment Processing POC*
*Confidential – Internal Use Only*

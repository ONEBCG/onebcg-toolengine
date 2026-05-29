# ToolEngine v2026 POC— Architecture Guide

**ONE BCG** · Internal Technical Reference

---

## System Overview

ToolEngine POC is built on .NET 8. The engine layer is domain-agnostic; business capabilities are delivered as self-contained modules. All execution paths — regardless of entry point (HTTP controller, scenario runner, LLM agent, CLI) — converge on the same MediatR behavior pipeline, ensuring consistent governance.

```
┌─────────────────────────────────────────────────────────────────────┐
│                         Entry Points                                │
│  HTTP Controller  │  ScenarioRunner  │  ChatService  │  CLI         │
└──────────────────────────┬──────────────────────────────────────────┘
                           │  ISender.Send(ExecuteToolCommand)
                           ▼
┌─────────────────────────────────────────────────────────────────────┐
│                  MediatR Behavior Pipeline                          │
│  1. ValidationBehavior      — FluentValidation input gates         │
│  2. LoopDetectionBehavior   — Distributed loop guard               │
│  3. ApprovalBehavior        — [RequiresApproval] suspension gate    │
│  4. AuditBehavior           — SOC 2 append-only invocation record  │
└──────────────────────────┬──────────────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────────────┐
│                     Tool Execution                                  │
│  IToolExecutor → IToolRegistry.Resolve → ToolHandlerBase.HandleAsync│
└─────────────────────────────────────────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────────────┐
│                   Infrastructure                                    │
│  AppDbContext (EF Core)  │  ICacheProvider  │  ILlmProvider         │
│  SQLite/SqlServer/Postgres│  Memory/Redis    │  Claude/OpenAI/None   │
└─────────────────────────────────────────────────────────────────────┘
```

---

## Project Dependency Graph

```
Core.Abstractions  (BCL only — ILlmProvider, ICacheProvider, IUnitOfWork)
       ↑
Core.Domain        (Result<T>, entities, enums, contracts)
       ↑
Tools.Abstractions (IToolHandler, base classes, IScenarioDefinition, StepContext)
       ↑         ↑
Tools.Registry  Tools.Executor
       ↑
Application        (MediatR behaviors + ExecuteToolCommand + ScenarioRunner)
       ↑
Infrastructure     (EF Core, provider selection, ClaudeProvider, OpenAiProvider)
       ↑
Modules/Payment/   (Domain → Tools → Application → Infrastructure → Api)
       ↑
Hosts/Api          (Compositor — wires engine + modules, exposes HTTP surface)
       ↑
Hosts/Ui           (Standalone static file server — consumes API via HTTP)
```

---

## Information Flow — Single Tool Invocation

```
POST /api/v1/tools/invoke
  │
  ▼ ToolsController.InvokeTool()
  │   Constructs ExecuteToolCommand from request body
  │
  ▼ ISender.Send(ExecuteToolCommand)
  │
  ▼ ValidationBehavior
  │   Runs all IValidator<ExecuteToolCommand> via FluentValidation
  │   Returns 422 VALIDATION_ERROR if any fail
  │
  ▼ LoopDetectionBehavior
  │   ICacheProvider.IncrementAsync(key: "tool.{name}:{correlationId}")
  │   Returns 429 LOOP_DETECTED if count > 5 within 5 minutes
  │
  ▼ ApprovalBehavior
  │   Reads [RequiresApproval] attribute from handler type
  │   If present: IHumanApprovalGate.RequestApprovalAsync(context)
  │     → AsyncApprovalGate creates PendingApproval record
  │     → Returns ApprovalDecision.Suspended(invocationId)
  │     → Behavior returns ToolResponse.Suspended → HTTP 202
  │   If absent: passes through
  │
  ▼ AuditBehavior
  │   Creates ToolInvocationRecord (Status = STARTED)
  │   Calls next()
  │   Updates ToolInvocationRecord (Status = COMPLETED/FAILED)
  │
  ▼ ExecuteToolCommandHandler
  │   IToolExecutor.ExecuteAsync<JsonElement, JsonElement>(request)
  │
  ▼ ToolExecutor
  │   IToolRegistry.Resolve(fullName, version)
  │   IServiceScopeFactory.CreateAsyncScope()     ← M1: scoped handler safe
  │   Deserialise JsonElement → handler's TInput via reflection
  │   Invoke handler.ExecuteAsync(typedRequest, ct)
  │
  ▼ Tool Handler (e.g. VerifyPayeeHandler)
      Business logic executes
      Returns ToolResponse<TOutput>
```

---

## Information Flow — Payment Pipeline

```
POST /api/v1/payments
  │
  ▼ PaymentsController.InitiatePayment()
  │
  ▼ ProcessPaymentCommandHandler.Handle()
  │
  ├─ Stage 0: RunStageAsync<InitiatePaymentOutput>   → validates input
  │   Create PaymentInstruction → DB (gets PRID)
  │
  ├─ Stage 1: RunStageAsync<VerifyPayeeOutput>        → DB payee lookup
  │   PaymentInstruction.AttachVerifiedPayee(payeeId) → DB
  │
  ├─ Stage 2: RunStageAsync<PpmCheckOutput>           → contract validation
  │   PaymentInstruction.MarkPpmChecked()             → DB
  │
  ├─ Stage 3: RunStageAsync<CalculateWhtOutput>       → tax calculation (stub)
  │   PaymentInstruction.ApplyWhtCalculation(...)     → DB
  │
  ├─ Stage 4: RunStageAsync<KycScreenOutput>          → KYC screening (stub)
  │   KycScreeningRecord.Create()                     → DB
  │
  └─ Stage 5: ISender.Send(ExecuteToolCommand "compile-dossier")
      │   [RequiresApproval(High)] on handler
      │
      ▼ ApprovalBehavior
          AsyncApprovalGate.RequestApprovalAsync()
            PendingApproval.Create() → DB
            OutboxMessage.Create()   → DB
          Returns ToolResponse.Suspended
      │
      ▼ ProcessPaymentCommandHandler detects IsSuspended
          PaymentInstruction.MarkPendingApproval(pendingId)
          PaymentAuditLog.Create("ApprovalGate", "PENDING")
          Returns ProcessPaymentResult(Status=PENDING_APPROVAL)
      │
      ▼ HTTP 202 Accepted { prid, pendingApprovalId, resumeUrl }

[After approval granted]

POST /api/v1/payments/{prid}/resume
  │
  ▼ ResumePaymentCommandHandler.Handle()
  │   Load PaymentInstruction + PendingApproval from DB
  │   Verify PendingApproval.Status == Approved
  │   PaymentInstruction.MarkApprovalGranted()
  │
  ├─ Stage 6: RunStageAsync<ExecutePaymentOutput>     → bank submission (stub)
  │   PaymentAuditLog "PaymentExecution SUBMITTED"
  │
  └─ Stage 7: RunStageAsync<ReconcilePaymentOutput>   → reconciliation
      PaymentAuditLog "Reconciliation SETTLED"
      Returns ResumePaymentResult(Status=SETTLED)

HTTP 200 { status: "SETTLED", bankTransactionId }
```

---

## Information Flow — Scenario Orchestration

```
POST /api/v1/scenarios/payment.compliance-check/run
  │
  ▼ ScenariosController.RunScenario()
  │
  ▼ ScenarioRunner.RunAsync(name, version, input, userId, callerType)
  │
  ├─ IScenarioRegistry.Resolve("payment.compliance-check", "v1")
  │   Returns PaymentComplianceScenario
  │
  ├─ IRequiresSetup.SetupAsync(input, services, ct)      ← optional hook
  │   Creates PaymentInstruction → DB
  │   Injects prid into input
  │
  ├─ IScenarioDefinition.Build(input)
  │   Returns ToolPlan with 4 steps + OutputMappings
  │
  ├─ ScenarioExecution.Start() → DB                     ← durability checkpoint
  │
  └─ IToolPlanOrchestrator.ExecuteAsync(plan, userId, callerType)
      │
      For each step (in dependency order):
      │
      ├─ StepContext.ResolveInput(staticInput, outputMappings)
      │   Merges static JSON with mapped values from prior step outputs
      │   Example: "verifiedPayeeId" ← "step-1-verify-payee.payeeId"
      │
      ├─ ISender.Send(ExecuteToolCommand)    ← FULL MediatR pipeline fires
      │   Validation → LoopDetection → Approval → Audit
      │   Tool handler executes
      │
      ├─ StepContext.SetStepOutput(stepId, responseData)
      │   Stores output for downstream steps
      │
      └─ If IsSuspended:
             ScenarioExecution.Suspend(stepId, approvalId, context)
             Return HTTP 202 { executionId, pendingApprovalId, resumeUrl }

[After approval]

POST /api/v1/scenarios/{executionId}/resume
  │
  ▼ ScenarioRunner.ResumeAsync(executionId)
      Load ScenarioExecution from DB
      Deserialise StepContext (restores all prior step outputs)
      Rebuild ToolPlan from original InputJson
      ToolPlanOrchestrator.ExecuteAsync(plan, resumeFromStepId+1)
```

---

## Information Flow — LLM Chat

```
POST /api/v1/chat  { message: "Process a payment to Acme..." }
  │
  ▼ ChatController.Chat()
  │
  ▼ ChatService.SendAsync(userMessage, ct)
  │
  ├─ Build IReadOnlyList<LlmTool> from IToolRegistry.ListTools()
  │   Each tool: { Name, Description, InputSchema }
  │
  ▼ ILlmProvider.ChatAsync(message, tools, executeTool, systemPrompt, ct)
  │
  │   [ClaudeProvider or OpenAiProvider — same interface, different wire format]
  │
  │   ┌─ Agentic loop ──────────────────────────────────────────────────────┐
  │   │                                                                     │
  │   │  Send messages to LLM API with tool schemas                        │
  │   │         ↓                                                           │
  │   │  Parse response: text blocks + tool_use/tool_calls                 │
  │   │         ↓                                                           │
  │   │  If stop_reason = end_turn (no tool calls) → return text           │
  │   │         ↓                                                           │
  │   │  For each tool call:                                                │
  │   │    executeTool(toolName, input)                                     │
  │   │      → ChatService.ExecuteToolAsync()                              │
  │   │        → ISender.Send(ExecuteToolCommand)  ← full pipeline         │
  │   │        → Returns JSON string result                                 │
  │   │    Append tool result to conversation history                       │
  │   │         ↓                                                           │
  │   │  Continue loop with updated conversation                           │
  │   └─────────────────────────────────────────────────────────────────────┘
  │
  ▼ LlmChatResponse { Reply, ToolCalls[] }
  │
  ▼ ChatResponse mapped to HTTP 200
```

---

## Provider Architecture

All three provider categories follow the Strategy pattern. Selection happens once at startup in `AddToolInfrastructure()`.

```
ILlmProvider ───────────┬── ClaudeProvider   (LLM:Provider = "claude")
                        ├── OpenAiProvider   (LLM:Provider = "openai")
                        └── NullLlmProvider  (no API key configured)

ICacheProvider ─────────┬── MemoryCacheProvider  (Cache:Provider = "memory")
                        └── RedisCacheProvider    (Cache:Provider = "redis")

AppDbContext ───────────┬── UseSqlite(...)   (Database:Provider = "sqlite")
                        ├── UseSqlServer(...)  (Database:Provider = "sqlserver")
                        └── UseNpgsql(...)   (Database:Provider = "postgres")
```

**API key resolution** (LLM providers):
1. Read `LLM:Claude:ApiKey` from appsettings
2. If empty, fall back to `ANTHROPIC_API_KEY` environment variable
3. If still empty, register `NullLlmProvider`

---

## Modular Structure

The engine knows nothing about payment (or any other domain). Payment is a self-contained module that registers itself into the engine.

```
AddPaymentModule() entry point in Payment.Api:
  ├── AddPaymentTools()               ← registers 8 stage handlers + 4 scenarios
  ├── AddPaymentApplicationServices() ← MediatR handlers + FluentValidation
  └── AddPaymentInfrastructure()
          ├── IModuleEntityConfiguration → PaymentModuleEntityConfiguration
          └── IModuleSeeder             → PaymentModuleSeeder

AppDbContext.OnModelCreating():
  ├── Engine tables (always): ToolInvocationRecord, PendingApproval, ScenarioExecution…
  └── Module tables (injected): foreach IModuleEntityConfiguration → config.Apply(mb)

Program.cs startup seeding:
  ├── db.Database.MigrateAsync()
  └── foreach IModuleSeeder → seeder.SeedAsync(db, logger)
```

---

## Data Model

| Table | Entity | Layer | Description |
|-------|--------|-------|-------------|
| `ToolInvocationRecords` | `ToolInvocationRecord` | Engine | One row per tool call — status, duration, error |
| `ToolInvocationEvents` | `ToolInvocationEvent` | Engine | STARTED / COMPLETED events per invocation |
| `PendingApprovals` | `PendingApproval` | Engine | Approval requests — token, status, channel |
| `OutboxMessages` | `OutboxMessage` | Engine | Transactional outbox for async notifications |
| `ScenarioExecutions` | `ScenarioExecution` | Engine | Durable scenario state for suspend/resume |
| `PaymentInstructions` | `PaymentInstruction` | Payment | Payment lifecycle and all stage outcomes |
| `PayeeRecords` | `PayeeRecord` | Payment | Registered payees with bank details |
| `PpmContracts` | `PpmContract` | Payment | Payment permission agreements |
| `WhtRateEntries` | `WhtRateEntry` | Payment | WHT rate table by jurisdiction/service |
| `KycScreeningRecords` | `KycScreeningRecord` | Payment | KYC screening results |
| `PaymentAuditLogs` | `PaymentAuditLog` | Payment | Per-stage payment pipeline audit trail |

---

## Tool Handler Base Classes

| Base Class | Use When | Additional DI |
|------------|----------|--------------|
| `LogicToolBase<TIn, TOut>` | Pure computation, no I/O | None |
| `DatabaseToolBase<TIn, TOut>` | DB reads/writes | `IUnitOfWork`, `AppDbContext` |
| `ApiToolBase<TIn, TOut>` | External HTTP calls | `IHttpClientFactory` |
| `CompositeToolBase<TIn, TOut>` | Calls other tools | `IToolExecutor` |

All handlers are registered as **Transient** — mandatory to avoid captive dependency issues with scoped `DbContext`.

---

## Approval Gate Mechanics

```
[RequiresApproval(ApprovalRisk.High, ApprovalChannel.Dashboard)]
  ↓
ApprovalBehavior (behavior 3 of 4):
  1. Resolve handler type from IToolRegistry
  2. Read [RequiresApproval] attribute
  3. Build AcknowledgementStatement for High/Critical (EU AI Act §14)
  4. AsyncApprovalGate.RequestApprovalAsync(context)
       a. Idempotency check: existing PendingApproval with same key?
       b. PendingApproval.Create() — 256-bit CSPRNG token (E1)
       c. OutboxMessage → async notification channel
       d. Return ApprovalDecision.Suspended(invocationId)
  5. Return ToolResponse.Suspended → HTTP 202

Approval token verification (ApprovalsController):
  - CryptographicOperations.FixedTimeEquals(expected, provided)  ← E1 timing-safe
```

---

## Security Constraints

| Ref | Constraint | Implementation |
|-----|-----------|----------------|
| E1 | Approval token strength | 256-bit CSPRNG hex |
| E1 | Constant-time comparison | `CryptographicOperations.FixedTimeEquals` |
| E2 | Brute-force lockout | `FailedOtpAttempts` counter on `PendingApproval` |
| F4 | Loop detection | `ICacheProvider.IncrementAsync` — 5 calls / 5 min per (tool, correlation) |
| F8 | Idempotency | `IdempotencyKey` on `ExecuteToolCommand` → `AsyncApprovalGate` deduplication |
| H3 | Acknowledgement | `AcknowledgementStatement` for High/Critical risk tools (EU AI Act §14) |
| H4 | Caller identity | `CallerType` (Human/AiAgent/SystemService) persisted on every audit record |

---

*ONE BCG ToolEngine v2026 — Architecture Guide*
*Confidential – Internal Use Only*

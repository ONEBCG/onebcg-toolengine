# ToolEngine v2026

**ONE BCG** — Multi-tenant AI Tool Invocation Platform

ToolEngine is the core execution backbone for AI-agent and human-operator workloads at ONE BCG. It provides a structured, audited, compliance-ready pipeline for invoking discrete typed operations — called *tools* — across multi-tenant environments. Built for use as both a standalone service and a base template for every new application built on the ONE BCG platform.

---

## Contents

1. [Architecture](#1-architecture)
2. [Design Patterns](#2-design-patterns)
3. [Features](#3-features)
4. [Pipeline Guards](#4-pipeline-guards)
5. [Approval Workflow](#5-approval-workflow)
6. [LLM Agent Layer](#6-llm-agent-layer)
7. [Data Flow](#7-data-flow)
8. [Compliance](#8-compliance)
9. [Configuration Reference](#9-configuration-reference)
10. [Getting Started](#10-getting-started)
11. [Creating a New Tool](#11-creating-a-new-tool)
12. [Using as a Project Template](#12-using-as-a-project-template)
13. [API Reference](#13-api-reference)

---

## 1. Architecture

### Solution Structure

```
onebcg-toolengine/
└── code/
    └── src/
        ├── Core/
        │   ├── ToolEngine.Core.Abstractions        Pure interfaces — no implementations, no NuGet deps
        │   └── ToolEngine.Core.Domain              Entities, Result<T>, Error, enums, constants
        │
        ├── Tools/
        │   ├── ToolEngine.Tools.Abstractions       ITool, IToolHandler, base classes, [RequiresApproval]
        │   ├── ToolEngine.Tools.Registry           IToolRegistry, IToolDiscovery, namespace routing
        │   ├── ToolEngine.Tools.Executor           ToolExecutor, ToolPlanExecutor (Sequential/Parallel/DAG)
        │   └── ToolEngine.Tools.Samples            Reference implementations: math.calculate, weather.current
        │
        ├── Application/
        │   └── ToolEngine.Application              CQRS commands, MediatR pipeline, six behaviors
        │
        ├── Llm/
        │   └── ToolEngine.Llm                      LLM providers, AgentOrchestrator, session store, prompts
        │
        ├── Infrastructure/
        │   └── ToolEngine.Infrastructure           EF Core, repositories, approval channels, outbox
        │
        └── Hosts/
            ├── ToolEngine.Api                      ASP.NET Core Minimal API, JWT auth, endpoints
            └── ToolEngine.Cli                      Spectre.Console REPL
```

### Layer Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│  Hosts  (Api / Cli)                                              │
│  JWT auth · Minimal API endpoints · SSE streaming · Dev token   │
└───────────────────────┬─────────────────────────────────────────┘
                        │
┌───────────────────────▼─────────────────────────────────────────┐
│  Application                                                     │
│  CQRS · MediatR pipeline · Six behaviors (in order):            │
│  TenantAuth → Validation → TokenBudget → DailyBudget →          │
│  LoopDetection → Approval → Audit                               │
└───────────┬───────────────────────┬─────────────────────────────┘
            │                       │
┌───────────▼──────────┐  ┌─────────▼───────────────────────────┐
│  Llm                 │  │  Tools                               │
│  AgentOrchestrator   │  │  Registry · Executor · PlanExecutor  │
│  Providers           │  │  Abstractions · Samples              │
│  ScopeGuard          │  └─────────────────────────────────────┘
│  ToolGuard           │
│  Prompts             │
└───────────┬──────────┘
            │
┌───────────▼─────────────────────────────────────────────────────┐
│  Infrastructure                                                  │
│  EF Core (SQLite/PostgreSQL/SQL Server) · Repositories           │
│  ApprovalGate · Outbox · EmailOtpChannel · NotificationDispatch │
└─────────────────────────────────────────────────────────────────┘
            │
┌───────────▼─────────────────────────────────────────────────────┐
│  Core.Domain                                                     │
│  Entities · Result<T> · Error · Contracts · Enums · Constants   │
└─────────────────────────────────────────────────────────────────┘
            │
┌───────────▼─────────────────────────────────────────────────────┐
│  Core.Abstractions                                               │
│  IRepository · ICurrentUser · ISecretVault · ICacheProvider     │
└─────────────────────────────────────────────────────────────────┘
```

### Dependency Rules

| Rule | Enforced by |
|---|---|
| `Core.Abstractions` has zero dependencies | No project references in `.csproj` |
| `Core.Domain` depends only on `Core.Abstractions` | Project reference only |
| `Infrastructure` does not depend on `Application` or `Llm` | Composition root wires them |
| `Tools.Abstractions` does not reference `Infrastructure` or `Llm` | Tool authors stay framework-free |
| `Llm` does not reference `Infrastructure` | Uses `ICacheProvider` from Abstractions for sessions |

---

## 2. Design Patterns

| Pattern | Purpose | Where Used |
|---|---|---|
| **Clean Architecture** | Layer isolation — inner layers have no knowledge of outer layers | Entire solution |
| **CQRS** | Separate command intent from query | `ExecuteToolCommand`, `AgentChatCommand` in `Application` |
| **MediatR Pipeline** | Cross-cutting concerns injected without modifying business logic | `Behaviors/` — all six guards |
| **Railway-Oriented Programming** | Explicit success/failure flow without exceptions for expected errors | `Result<T>` and `Error` in `Core.Domain.Common` |
| **Repository + Unit of Work** | Abstract persistence, batch saves | `IRepository<T,TId>`, `IUnitOfWork` in `Core.Abstractions` |
| **Strategy** | Swap LLM providers, DB providers, cache providers, and approval channels at runtime | `ILlmProvider`, `ProviderRouter`, `ApprovalChannelSelector` |
| **Outbox Pattern** | Guarantee notification delivery without distributed transactions | `OutboxMessage` + `NotificationDispatchService` in `Infrastructure` |
| **Specification Pattern** | Composable query predicates without leaking EF into domain | `LambdaSpecification<T>` used in all repository queries |
| **Factory Method** | Enforce invariants at creation time | `Tenant.Create()`, `PendingApproval.Create()`, `ToolInvocationRecord.Create()` |
| **Decorator / Chain of Responsibility** | Ordered pipeline of independent guards | MediatR `IPipelineBehavior<TRequest,TResponse>` |
| **Aggregate Root** | Consistency boundary — all mutations through root | `Tenant`, `PendingApproval`, `ToolInvocationRecord` extend `AggregateRoot` |
| **Domain Events** | Decouple side effects from domain mutations | `AggregateRoot.AddDomainEvent()` / `ClearDomainEvents()` |
| **Template Method** | Base class enforces lifecycle; subclass provides implementation | `ToolHandlerBase<TInput,TOutput>` |
| **Async Request-Reply (202)** | Non-blocking approval — client polls status after suspend | `PendingApproval` + `/invocations/{id}/status` |
| **Idempotency Key** | Safe retries — duplicate requests return existing state | `IdempotencyKey` on `ApprovalContext` + `AsyncApprovalGate` |
| **Prompt Externalisation** | LLM behavioural rules tunable without code recompile | `IPromptStore` / `JsonPromptStore` / `prompts.json` in `Llm.Prompts` |
| **Constant Registry** | Prevent magic strings drifting between layers | `Core.Domain.Constants.*` |

---

## 3. Features

### Multi-Tenancy

Every invocation is scoped to a tenant identified by a lowercase string slug. Each tenant independently configures:

- **Namespace allowlist** — deny-by-default; `"*"` grants unrestricted access
- **MaxResponseTokens** — per-request token cap
- **DailyToolCallBudget** — total daily invocations before 429
- **Approval channel** — Dashboard, EmailMagicLink, EmailOtp, or Webhook

Tenants are provisioned via the domain entity `Tenant.Create()` which enforces lowercase normalisation. New tenants start with an empty allowlist and cannot invoke any tool until namespaces are explicitly granted.

### Tool Registry

Tools are registered at startup via `AddToolSamples()` or custom `IToolRegistry.Register()` calls. Each tool carries:

- A unique `namespace.name@version` identifier
- A typed `ToolSchema` (JSON Schema for inputs and outputs)
- `WhenToUse` / `WhenNotToUse` guidance used by the LLM scope classifier
- Optional `[RequiresApproval(Risk, Reason)]` attribute

Discovery via `IToolDiscovery.Resolve()` supports exact match and semantic search. Listing via `IToolRegistry.ListAll(tenantId)` returns only tools the tenant can access.

### Six-Behavior MediatR Pipeline

Every tool invocation passes through six ordered behaviors before reaching the handler. Each behavior is independent and replaceable:

```
TenantAuthorizationBehavior  →  ValidationBehavior  →  TokenBudgetBehavior
→  DailyBudgetBehavior  →  LoopDetectionBehavior  →  ApprovalBehavior  →  AuditBehavior
→  [Tool Handler]
```

See [Pipeline Guards](#4-pipeline-guards) for full details.

### Human-in-the-Loop Approval Gate

Risk-tiered approval for any tool marked `[RequiresApproval]`. Supports four channels, four risk tiers, idempotent retries, OTP for critical operations, and structured EU AI Act acknowledgement statements. Full state machine documented in [Approval Workflow](#5-approval-workflow).

### LLM Agent Layer

A complete agentic loop with pre-flight scope classification, response grounding enforcement, tool guard (pre- and post-selection), session token budgets, and iteration limits. Supports Anthropic Claude, OpenAI GPT, and Ollama with runtime provider routing and fallback chains. See [LLM Agent Layer](#6-llm-agent-layer).

### Plan Executor

`ToolPlanExecutor` executes multi-step `ToolPlan` objects:

- **Sequential** — steps run in declaration order
- **Parallel** — independent steps run concurrently via `Task.WhenAll`
- **DAG** — dependency-aware execution; a step only runs when all declared predecessors have succeeded

Each step result is typed. Any step failure stops dependent steps. Failed or skipped steps are reported in the plan result.

### Audit Trail

Every invocation generates a `ToolInvocationRecord` and one `ToolInvocationEvent` row per lifecycle state:

| Event | Trigger |
|---|---|
| `Invoked` | Before handler runs |
| `Running` | After initial save |
| `Succeeded` | Handler returned success |
| `Failed` | Handler returned error or exception thrown |
| `Suspended` | Approval gate requested human decision |

The events table is append-only by database grant. Records carry `CallerType` (Human / AiAgent / SystemService), `GovernanceMetadataJson`, and `RetainUntil` (90-day default).

### Observability

- **Traces** — OpenTelemetry `ActivitySource` spans: `tool.execute`, `tool.approval.gate`. W3C `traceparent` propagated automatically from inbound HTTP headers.
- **Metrics** — `tool.invocation.duration` (histogram), `tool.invocation.count` (counter), `daily_budget_exceeded` (counter), `loop_detection_triggers` (counter), `pending_approval_count` (gauge). All tagged with `tool.fullName` and `tenant.id`.
- **Logs** — Serilog structured logging. Email addresses masked in all log properties (GDPR). EF Core SQL logged in Development only.
- **Export** — configure `Otlp:Endpoint` to ship traces and metrics to any OpenTelemetry-compatible collector (Jaeger, Grafana Tempo, Datadog, Azure Monitor).

### Dual Host Surface

| Host | Use case |
|---|---|
| `ToolEngine.Api` | Production REST API, CI pipelines, agent SDK integration |
| `ToolEngine.Cli` | Developer REPL, local testing, offline tool exploration |

### SSE Streaming

`POST /agent/chat/stream` emits Server-Sent Events in real time:

```
event: status        → { "status": "processing" }
event: tool_selected → { "tool": "math.calculate" }
event: tool_result   → { ... tool output ... }
event: reply         → { "text": "...", "sessionId": "..." }   (success)
event: out_of_scope  → { "message": "..." }                    (scope boundary)
event: pending_approval → { "invocationId": "...", "pollUrl": "..." }
event: error         → { "message": "..." }
```

### Frontend Developer Console

React 18 / TypeScript / Vite SPA at `src/frontend`. Connects to the API for tool listing, invocation, and approval status polling. Swagger link embedded for schema exploration.

---

## 4. Pipeline Guards

Every `ExecuteToolCommand` passes through the following behaviors in strict order. A behavior that returns early (error or suspend) prevents all subsequent behaviors from running.

### Order and Responsibility

```
1. TenantAuthorizationBehavior   ←── outermost
2. ValidationBehavior
3. TokenBudgetBehavior
4. DailyBudgetBehavior
5. LoopDetectionBehavior
6. ApprovalBehavior
7. AuditBehavior                 ←── innermost (wraps handler)
   └── [Tool Handler]
```

---

### 1. TenantAuthorizationBehavior

**File:** `Application/Behaviors/TenantAuthorizationBehavior.cs`

Runs first — auth precedes all business logic (OWASP A01:2025).

| Check | Condition | Error |
|---|---|---|
| Tenant exists | `GetByIdAsync` returns null | `UNAUTHORIZED` 401 |
| Tenant active | `tenant.IsActive == false` | `UNAUTHORIZED` 403 |
| Namespace allowed | Tool namespace not in allowlist and no `"*"` wildcard | `UNAUTHORIZED` 403 |

**Bypass:** Commands that do not implement `IExecuteToolCommand` pass through without checks.

---

### 2. ValidationBehavior

**File:** `Application/Behaviors/ValidationBehavior.cs`

Runs FluentValidation validators registered for the command type. If any validator is registered and returns failures, the pipeline stops with `VALIDATION_ERROR` and the full failure list.

**Bypass:** If no validator is registered for the command type, passes through silently.

---

### 3. TokenBudgetBehavior

**File:** `Application/Behaviors/TokenBudgetBehavior.cs`

Enforces `Tenant.MaxResponseTokens`. Rejects before execution so token cost is never incurred on an over-budget request.

| Check | Condition | Error |
|---|---|---|
| Per-request token cap | `cmd.MaxResponseTokens > tenant.MaxResponseTokens` | `TOKEN_BUDGET_EXCEEDED` 400 |

**Bypass:** `MaxResponseTokens == 0` on the tenant means no cap is configured.

---

### 4. DailyBudgetBehavior

**File:** `Application/Behaviors/DailyBudgetBehavior.cs`

Counts `ToolInvocationRecords` created today (UTC midnight boundary) for the tenant. Issues a `COUNT(*)` query per request; Phase F replaces this with a Redis INCR counter.

| Check | Condition | Error |
|---|---|---|
| Daily call quota | `todayCount >= tenant.DailyToolCallBudget` | `DAILY_BUDGET_EXCEEDED` 429 |

**Bypass:** `DailyToolCallBudget == 0` means no cap.

**Metric emitted:** `daily_budget_exceeded` tagged with `tenant.id`.

---

### 5. LoopDetectionBehavior

**File:** `Application/Behaviors/LoopDetectionBehavior.cs`

Detects agent-driven loops within a single correlation context. Uses `ICacheProvider` (memory or Redis) keyed on `loop:{correlationId}:{namespace}.{name}` with a 10-minute TTL.

| Check | Condition | Error |
|---|---|---|
| Call count per correlation | `count > MaxCallsPerCorrelation` | `AGENT_LOOP_DETECTED` 429 |

Default `MaxCallsPerCorrelation`: 10 (configurable in `appsettings.json` → `LoopDetection.MaxCallsPerCorrelation`).

**Metric emitted:** `loop_detection_triggers` tagged with `tool.fullName` and `tenant.id`.

---

### 6. ApprovalBehavior

**File:** `Application/Behaviors/ApprovalBehavior.cs`

Intercepts tools marked `[RequiresApproval]` and routes to `IHumanApprovalGate`.

| Outcome | Condition | Response |
|---|---|---|
| Pass through | Tool not found or `NeedsApproval == false` | Continues to AuditBehavior |
| Suspend (202) | Gate returns `Pending = true` | `ToolResponse.Suspended(pendingInvocationId)` |
| Deny | Gate returns `Approved = false` | `APPROVAL_DENIED` 403 |
| Allow | Gate returns `Approved = true` | Continues to AuditBehavior |

OTel span `tool.approval.gate` is started as a child of `tool.execute`. Tagged with `tool.fullName`, `tenant.id`, `approval.risk`, `approval.decision`.

**Metric emitted:** `pending_approval_count` on suspend, tagged with `tenant.id` and `approval.risk`.

---

### 7. AuditBehavior

**File:** `Application/Behaviors/AuditBehavior.cs`

Innermost behavior — wraps the handler execution. Creates the `ToolInvocationRecord` before the handler runs and updates it after.

| Lifecycle | Action |
|---|---|
| Before handler | Creates record, emits `Invoked` event, calls `MarkRunning()`, saves |
| Handler succeeds | Calls `MarkSucceeded()`, emits `Succeeded` event |
| Handler fails (error response) | Calls `MarkFailed()`, emits `Failed` event with error code |
| Handler suspended | Calls `MarkSuspended()`, emits `Suspended` event |
| Handler throws exception | Calls `MarkFailed()`, emits `Failed` event with `EXCEPTION` code, re-throws |

OTel span `tool.execute` wraps the entire handler call. Metrics: `tool.invocation.duration` and `tool.invocation.count`.

---

## 5. Approval Workflow

### Risk Tiers

| Risk | Auto-approve | Channel | OTP Forced | EU AI Act Acknowledgement |
|---|---|---|---|---|
| `Low` | Yes | None (audit only) | No | No |
| `Medium` | No | Tenant default | No | No |
| `High` | No | Tenant default | No | Yes |
| `Critical` | No | EmailOtp | Yes | Yes |

### Approval Channels

| Channel | Mechanism | When to Use |
|---|---|---|
| `Dashboard` | Approver polls `GET /approvals/pending` | Internal teams with dashboard access |
| `EmailMagicLink` | One-click URL emailed to approver | External approvers or non-technical stakeholders |
| `EmailOtp` | 6-digit OTP emailed, verified at `POST /approvals/otp/verify` | Critical operations requiring explicit code entry |
| `Webhook` | POST payload sent to `Approval.WebhookUrl` | External systems, ticketing integrations |

### Approval State Machine

```
                    [RequiresApproval tool invoked]
                              │
                    ┌─────────▼──────────┐
                    │  ApprovalBehavior  │
                    └─────────┬──────────┘
                              │
               ┌──────────────┼──────────────┐
               ▼              ▼              ▼
          Risk = Low     Risk = Medium/High  Risk = Critical
               │              │              │
          Auto-Approve   Suspend + Notify   Suspend + OTP email
               │              │              │
               ▼              ▼              ▼
          Continue       HTTP 202          HTTP 202
          pipeline     pollUrl returned   pollUrl returned
                              │
                    ┌─────────▼──────────────────┐
                    │  PendingApproval (Pending)  │
                    └─────────┬──────────────────┘
                              │
           ┌──────────────────┼──────────────────┐
           ▼                  ▼                  ▼
     Approve decision    Deny decision      Timeout expires
     (magic-link / OTP)  (magic-link)       (ApprovalTimeoutMinutes)
           │                  │                  │
           ▼                  ▼                  ▼
      status=Approved    status=Denied      status=Expired
           │
           ▼
   Client polls /invocations/{id}/status
   → re-invokes tool → pipeline continues
```

### Outbox Pattern for Notification Delivery

When a `PendingApproval` is created, `AsyncApprovalGate` atomically writes:
- One `PendingApproval` row
- One `OutboxMessage` row

Both are committed in a single `SaveChangesAsync`. `NotificationDispatchService` reads the outbox asynchronously, calls `channel.SendAsync()`, and marks the message delivered. If the process crashes between commit and send, the outbox ensures the notification is retried on next startup — no notification is lost.

### Idempotency

If a caller submits the same `IdempotencyKey` + `TenantId` + `ToolFullName` combination when a matching `PendingApproval` is still open, the gate returns the existing `PendingInvocationId` without creating a duplicate. Safe for at-least-once delivery retries.

---

## 6. LLM Agent Layer

### Overview

`POST /agent/chat` routes through `AgentChatCommand` → `AgentChatHandler` → `AgentOrchestrator`. The orchestrator runs a governed agentic loop:

```
[User text]
     │
     ▼
┌────────────────────────────────────────────────┐
│  AgentScopeClassifier (pre-flight LLM call)    │
│  Temperature = 0.0, MaxTokens = 512            │
│  Returns structured JSON:                      │
│    isFullyOutOfScope / inScopePortion /        │
│    outOfScopeParts / refusalMessage            │
└──────────────────┬─────────────────────────────┘
                   │
       ┌───────────┼──────────────┐
       ▼           ▼              ▼
  Out of scope  Mixed request  Fully in scope
  Return        Forward only   Forward unchanged
  refusal       in-scope part
                   │
                   ▼
┌────────────────────────────────────────────────┐
│  AgentScopeEnforcer (system prompt injection)  │
│  Injected once per session:                    │
│  - Tool list with WhenToUse / Avoid guidance   │
│  - RULE 1: missing parameter handling          │
│  - RULE 2: response grounding (no leakage)     │
└──────────────────┬─────────────────────────────┘
                   │
    ┌──────────────▼──────────────────────────────┐
    │  Agentic Loop (max MaxIterations)           │
    │                                             │
    │  ┌─ Token budget check ──────────────────┐  │
    │  │  if TokensUsed ≥ MaxTokensPerSession  │  │
    │  │  → BudgetExceeded (429)               │  │
    │  └───────────────────────────────────────┘  │
    │                                             │
    │  LLM call → StopReason?                     │
    │    EndTurn   → extract text, return reply   │
    │    ToolUse   → validate via ToolGuardFilter │
    │               → ExecuteToolCommand (MediatR)│
    │               → inject GroundingReminder    │
    │               → loop                        │
    │    Error     → return failure               │
    └─────────────────────────────────────────────┘
```

### Scope Guard

The `AgentScopeClassifier` makes a **dedicated pre-flight LLM call** at temperature 0.0 before the main loop. This is more reliable than system-prompt-only scope enforcement because capable LLMs (Claude, GPT-4o) override system prompt restrictions using their training instinct to be helpful. A classification call returns deterministic structured JSON.

Configuration: `Llm.ScopeGuard.Enabled` (default: `true`)

**Fail-open:** if classification fails or returns invalid JSON, the request passes through and the system prompt rules in `AgentScopeEnforcer` remain as a secondary control.

### Tool Guard

Two enforcement points protect against prompt-injection attacks:

| Point | When | What |
|---|---|---|
| Pre-LLM schema filter | Before provider call | Strips blocked tools from schema — model never sees them |
| Post-selection validation | After LLM chooses a tool | Re-validates name before MediatR execution |

Configuration: `Llm.ToolGuard.Enabled`, `AllowedTools`, `DeniedTools`

Pattern syntax: `"math.calculate"` (exact), `"math.*"` (namespace wildcard), `"*"` (all).

**DeniedTools overrides AllowedTools.** A tool in both lists is always blocked.

### Response Grounding

After each tool result, a `GroundingReminder` is injected as a User message immediately before the LLM's summary generation step. This is more effective than a system prompt rule because it appears in the live context window directly before generation.

Content loaded from `prompts.json` key `agent.grounding-reminder`. Modify without recompile.

A grounding observability check logs a warning when the reply is more than 5× longer than the tool result, indicating potential knowledge leakage beyond the tool output.

### Prompt Externalisation

All LLM system prompts and behavioural rules are stored in `src/Llm/ToolEngine.Llm/Prompts/prompts.json`. Use `IPromptStore.Get(key)` to retrieve them. Keys are defined as constants in `PromptKeys`.

| Key | Content |
|---|---|
| `agent.grounding-reminder` | Injected after each tool result |
| `agent.fallback-system` | Fallback system prompt when no session messages exist |
| `agent.scope.default-refusal` | Returned when request is fully out of scope |
| `agent.scope.no-tools-message` | Returned when no tools are registered |
| `scope-enforcer.intro` | Opening paragraph of the main system prompt |
| `scope-enforcer.behavioural-rules` | RULE 1 (missing params) + RULE 2 (grounding) |
| `scope-classifier.output-format` | Output format instruction at top of classifier prompt |
| `scope-classifier.field-rules` | JSON field definitions and examples |

### LLM Provider Routing

```
X-Llm-Provider header  →  explicit per-request override
       ↓ (absent)
Tenant provider config →  per-tenant default
       ↓ (absent)
Llm.DefaultProvider    →  global default (e.g. "anthropic")
       ↓ (failure)
Llm.Routing.FallbackChain →  ["openai", "ollama"] tried in order
```

### Session Management

Multi-turn sessions are stored in `ICacheProvider` (memory dev / Redis production). Each session holds the full message history, token usage, and a per-session semaphore lock to prevent concurrent requests from clobbering message history.

Single-turn requests (no `SessionId`) create ephemeral sessions that are not saved after the turn.

---

## 7. Data Flow

### Standard Tool Invocation

```
Client
  │  POST /tools/{ns}/{name}/{version}/invoke
  │  Authorization: Bearer {jwt}
  │
  ▼
AgentEndpoints / ToolEndpoints
  │  Extract: correlationId, tenantId, userId from headers + JWT claims
  │  Dispatch: ExecuteToolCommand via IMediator
  │
  ▼
TenantAuthorizationBehavior ─── tenant active? namespace allowed?
  ▼
ValidationBehavior ─────────── FluentValidation passes?
  ▼
TokenBudgetBehavior ─────────── within per-request token cap?
  ▼
DailyBudgetBehavior ─────────── within daily call budget?
  ▼
LoopDetectionBehavior ───────── call count for this correlation < max?
  ▼
ApprovalBehavior ────────────── approval required? → gate decision
  │                                                     │
  │ (approved / not required)                 (suspend) ▼
  │                                           HTTP 202 + pollUrl
  ▼
AuditBehavior ─── create ToolInvocationRecord (Pending → Running)
  ▼
[Tool Handler] ─── execute business logic
  ▼
AuditBehavior ─── update record (Succeeded / Failed / Suspended)
  ▼
HTTP 200 { correlationId, success, data, metrics }
```

### Agent Chat Flow

```
Client
  │  POST /agent/chat
  │  { "text": "...", "sessionId": "..." }
  │
  ▼
AgentChatCommand → AgentChatHandler → AgentOrchestrator
  │
  ├─ AcquireSessionLockAsync (prevents concurrent session writes)
  ├─ ToolGuardFilter.Filter (strips blocked tools from schema)
  ├─ AgentScopeClassifier.ClassifyAsync (pre-flight LLM call)
  │    ├─ Fully out of scope → return refusal (no loop entered)
  │    ├─ Mixed → trim to in-scope portion
  │    └─ In scope → continue
  ├─ AgentScopeEnforcer.BuildSystemPrompt (inject once per session)
  └─ Loop (max MaxIterations):
       ├─ Token budget check
       ├─ LLM call via ILlmProvider
       ├─ EndTurn → append grounding note → return reply
       └─ ToolUse:
            ├─ ToolGuardFilter.IsPermitted (post-selection check)
            ├─ ExecuteToolCommand (full MediatR pipeline)
            ├─ Pending approval → return 202
            ├─ Inject GroundingReminder
            └─ Continue loop
```

---

## 8. Compliance

| Standard | Coverage | Implementation |
|---|---|---|
| **EU AI Act Article 14** | Human oversight for high-risk AI | `[RequiresApproval(Risk.High)]`, `AcknowledgementStatement` serialised to `PendingApproval.AcknowledgementJson` for every High/Critical approval |
| **EU AI Act Article 9** | Risk management documentation | Risk tiers (Low/Medium/High/Critical) on every tool; reason captured in `ApprovalReason` |
| **NIST AI RMF** | Pre-action approval outside agent reasoning loop | `ApprovalBehavior` intercepts before execution, not after; `CallerType` claim distinguishes AI from human callers |
| **NIST AI Agent Identity** | Agent identity in audit records | `CallerType` (Human/AiAgent/SystemService) sourced from JWT `caller_type` claim, propagated to `ToolInvocationRecord` and every `ToolInvocationEvent` |
| **ISO 42001** | AI governance metadata | `GovernanceMetadataJson` (provider, model, sessionId) propagated from `AgentOrchestrator` through `ExecuteToolCommand` to all audit records |
| **SOC 2 CC6** | Logical access controls | JWT auth, tenant namespace allowlists, deny-by-default, OTP for critical actions |
| **SOC 2 CC7** | System monitoring | OpenTelemetry traces + metrics, Serilog structured logs, anomaly metrics (loop detection, budget exceeded) |
| **GDPR Art. 5(1)(f)** | Data integrity and confidentiality | Email PII masked in all log properties via Serilog destructuring; `RetainUntil` on invocation records for erasure |
| **GDPR Art. 17** | Right to erasure | `RetainUntil` = `InvokedAt + 90 days` on every record; `IsAnonymized` flag for erasure sweep |
| **OWASP A01:2025** | Broken access control | `TenantAuthorizationBehavior` runs outermost; auth before all business logic |
| **OWASP MFA Cheat Sheet** | OTP brute-force protection | IP-level rate limiting (10 attempts/10 min), per-entity `FailedOtpAttempts` counter with lockout after 5 |
| **OWASP Secrets Management** | API key handling | Keys read from environment variables only (`ApiKeyEnvVar`); never in config files |
| **W3C TraceContext** | Distributed tracing interop | `traceparent`/`tracestate` propagated by ASP.NET Core OTel instrumentation |

---

## 9. Configuration Reference

All settings live in `appsettings.json` (base) and `appsettings.Development.json` (overrides). In production, sensitive values must be supplied via environment variables.

### JWT

| Key | Type | Default | Description |
|---|---|---|---|
| `Jwt:Issuer` | string | `"toolengine"` | JWT issuer claim (`iss`) validated on every request |
| `Jwt:Audience` | string | `"toolengine-clients"` | JWT audience claim (`aud`) validated on every request |
| `Jwt:Secret` | string | — | HMAC-SHA256 signing secret. **Minimum 32 bytes.** Set via `JWT__SECRET` env var in production |

### Database

| Key | Type | Default | Description |
|---|---|---|---|
| `Database:Provider` | string | `"sqlite"` | DB engine: `"sqlite"` \| `"postgresql"` \| `"sqlserver"` |
| `ConnectionStrings:Default` | string | `"Data Source=toolengine-dev.db"` | Connection string for the selected provider |
| `ConnectionStrings:Redis` | string | `"localhost:6379"` | Redis connection string (required when `Cache:Provider = "redis"`) |

### Cache

| Key | Type | Default | Description |
|---|---|---|---|
| `Cache:Provider` | string | `"memory"` | Cache engine: `"memory"` (single-node) \| `"redis"` (multi-pod production) |

### Approval Engine

| Key | Type | Default | Description |
|---|---|---|---|
| `Approval:DefaultChannel` | string | `"Dashboard"` | Default channel for Medium/High risk when tenant has no override |
| `Approval:ApprovalTimeoutMinutes` | int | `60` | Minutes before a pending approval expires automatically |
| `Approval:BaseUrl` | string | — | Base URL for magic-link generation. Must be `https://` in non-development |
| `Approval:WebhookUrl` | string | `null` | Webhook target for the Webhook channel. Required if any tenant uses Webhook |
| `Approval:TenantChannelOverrides` | object | `{}` | Per-tenant channel override map: `{ "tenant-slug": "EmailOtp" }` |

### Loop Detection

| Key | Type | Default | Description |
|---|---|---|---|
| `LoopDetection:MaxCallsPerCorrelation` | int | `10` | Max invocations of the same tool within one correlation before 429 |

### OpenTelemetry

| Key | Type | Default | Description |
|---|---|---|---|
| `Otlp:Endpoint` | string | `null` | OTLP gRPC endpoint, e.g. `"http://otel-collector:4317"`. When absent, instrumentation is active but not exported |

### LLM Agent

| Key | Type | Default | Description |
|---|---|---|---|
| `Llm:DefaultProvider` | string | `"anthropic"` | Provider used when no tool attribute or tenant override specifies one. Use `"ollama"` in development |
| `Llm:Routing:FallbackChain` | string[] | `["openai","ollama"]` | Providers tried in order when the primary provider returns non-200 or times out |
| `Llm:ScopeGuard:Enabled` | bool | `true` | Enable pre-flight scope classification. Set `false` only in trusted dev environments |
| `Llm:ToolGuard:Enabled` | bool | `true` | Enable tool allowlist/denylist. Set `false` only in trusted dev environments |
| `Llm:ToolGuard:AllowedTools` | string[] | `[]` | Allowlist patterns. Empty = no restriction. Example: `["math.*","weather.current"]` |
| `Llm:ToolGuard:DeniedTools` | string[] | `[]` | Denylist patterns. Takes precedence over AllowedTools. Example: `["hr.*","finance.*"]` |
| `Llm:Budget:MaxTokensPerRequest` | int | `2048` | Max output tokens per single LLM call |
| `Llm:Budget:MaxTokensPerSession` | int | `32768` | Cumulative token circuit-breaker across all LLM calls in a session |
| `Llm:Budget:MaxIterations` | int | `5` | Max agentic loop iterations before returning `MaxIterations` error |

### LLM Providers

| Key | Description |
|---|---|
| `Llm:Providers:anthropic:Model` | Anthropic model name. Default: `"claude-sonnet-4-5"` |
| `Llm:Providers:anthropic:ApiKeyEnvVar` | Environment variable holding the Anthropic API key. Default: `"ANTHROPIC_API_KEY"` |
| `Llm:Providers:anthropic:MaxTokens` | Token ceiling for Anthropic responses |
| `Llm:Providers:anthropic:TimeoutSeconds` | HTTP request timeout. Default: `30` |
| `Llm:Providers:anthropic:Temperature` | Sampling temperature `0.0–1.0`. Classifier always overrides to `0.0` |
| `Llm:Providers:openai:*` | Same keys. Model default: `"gpt-4o"`. Valid temperature range: `0.0–2.0` |
| `Llm:Providers:ollama:BaseUrl` | Ollama REST base URL. Default: `"http://localhost:11434"` |
| `Llm:Providers:ollama:Model` | Ollama model name. Default: `"llama3.1:8b"` |
| `Llm:Providers:ollama:TimeoutSeconds` | Extended timeout for local inference cold-start. Default: `120` |

### Production Environment Variables

Never commit secrets to source control. Set these in your deployment environment:

```bash
JWT__SECRET=<32+ byte base64 string>
ANTHROPIC_API_KEY=<your key>
OPENAI_API_KEY=<your key>
CONNECTIONSTRINGS__DEFAULT=<postgresql or sqlserver connection string>
CONNECTIONSTRINGS__REDIS=<redis connection string>
```

---

## 10. Getting Started

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8)
- SQLite (bundled via NuGet — no separate install required for development)
- Optional: Docker (for PostgreSQL / Redis in production-like dev)

### 1. Clone and Build

```bash
git clone <repo-url>
cd onebcg-toolengine/code
dotnet build
```

### 2. Run the API

```bash
cd src/Hosts/ToolEngine.Api
dotnet run
```

The API starts at `http://localhost:5174`.

| Endpoint | URL |
|---|---|
| Swagger UI | `http://localhost:5174/swagger` |
| Health (live) | `http://localhost:5174/healthz/live` |
| Health (ready) | `http://localhost:5174/healthz/ready` |

### 3. Get a Dev Token

```bash
curl "http://localhost:5174/dev/token?tenant=onebcg-default-tenant"
```

Returns:
```json
{
  "token": "eyJhbGci...",
  "expiresIn": 28800,
  "tenantId": "onebcg-default-tenant"
}
```

Export it for subsequent requests:
```bash
export TOKEN=eyJhbGci...
```

### 4. Invoke a Tool

```bash
curl -X POST http://localhost:5174/tools/math/calculate/v1/invoke \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"a":10,"b":5}'
```

```json
{
  "correlationId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "success": true,
  "data": { "result": 15 },
  "metrics": { "durationMs": 4 }
}
```

### 5. Use the Agent

```bash
curl -X POST http://localhost:5174/agent/chat \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"text":"What is 42 multiplied by 7?"}'
```

```json
{
  "reply": "42 multiplied by 7 is 294.",
  "toolInvoked": "math.calculate",
  "sessionId": "abc-123",
  "usage": { "inputTokens": 312, "outputTokens": 18, "totalTokens": 330 }
}
```

### 6. Multi-Turn Session

Pass the `sessionId` from the previous response to continue the conversation:

```bash
curl -X POST http://localhost:5174/agent/chat \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"text":"Now divide that by 6", "sessionId":"abc-123"}'
```

### 7. Run the CLI

```bash
cd src/Hosts/ToolEngine.Cli
dotnet run
```

```
toolengine> list
toolengine> invoke math calculate v1 {"a":10,"b":5}
toolengine> search "add two numbers"
toolengine> exit
```

### 8. Run Tests

```bash
dotnet test
```

---

## 11. Creating a New Tool

### Step 1 — Define Input and Output models

```csharp
// In your Tools project
public sealed record CurrencyConvertInput(
    decimal Amount,
    string  FromCurrency,
    string  ToCurrency);

public sealed record CurrencyConvertOutput(
    decimal ConvertedAmount,
    string  ExchangeRateSource);
```

### Step 2 — Implement the handler

```csharp
using ToolEngine.Tools.Abstractions;

[ToolMetadata(
    Namespace:   "finance",
    Name:        "currency-convert",
    Version:     "v1",
    Description: "Converts an amount from one currency to another using live exchange rates.",
    WhenToUse:   "When the user asks to convert currency or asks what an amount is worth in another currency.",
    WhenNotToUse:"For historical exchange rates or batch conversions."
)]
public sealed class CurrencyConvertTool
    : ToolHandlerBase<CurrencyConvertInput, CurrencyConvertOutput>
{
    private readonly IExchangeRateService _rates;

    public CurrencyConvertTool(IExchangeRateService rates)
        => _rates = rates;

    protected override async Task<ToolResponse<CurrencyConvertOutput>> ExecuteAsync(
        CurrencyConvertInput input,
        ToolContext          context,
        CancellationToken    ct)
    {
        var rate   = await _rates.GetRateAsync(input.FromCurrency, input.ToCurrency, ct);
        var output = new CurrencyConvertOutput(
            ConvertedAmount:    input.Amount * rate.Rate,
            ExchangeRateSource: rate.Source);

        return ToolResponse<CurrencyConvertOutput>.Ok(context.CorrelationId, output);
    }
}
```

### Step 3 — Add approval (optional)

For tools that modify data or trigger side effects:

```csharp
[RequiresApproval(
    Risk:   ApprovalRisk.Medium,
    Reason: "Currency conversion triggers a financial API call that may incur cost.")]
public sealed class CurrencyConvertTool : ToolHandlerBase<...>
```

Risk tiers:
- `Low` — auto-approved with audit log
- `Medium` — suspend + notify via tenant default channel
- `High` — suspend + EU AI Act acknowledgement statement
- `Critical` — suspend + OTP verification required

### Step 4 — Register the tool

In your service extension or `Program.cs`:

```csharp
// Option A: register via assembly scan
services.AddToolsFromAssembly(typeof(CurrencyConvertTool).Assembly);

// Option B: register manually
services.AddTool<CurrencyConvertTool, CurrencyConvertInput, CurrencyConvertOutput>();
```

### Step 5 — Grant the tenant namespace access

```csharp
// In your tenant seed or admin endpoint
tenant.AllowNamespace("finance");
// Or for unrestricted dev access:
tenant.AllowNamespace("*");
```

### Step 6 — Test the tool

```csharp
// Unit test
[Fact]
public async Task Execute_ValidInput_ReturnsConvertedAmount()
{
    // Arrange
    var rates = new FakeExchangeRateService(rate: 1.12m);
    var tool  = new CurrencyConvertTool(rates);

    // Act
    var result = await tool.InvokeAsync(
        new CurrencyConvertInput(100m, "USD", "EUR"),
        ToolContext.ForTest(), CancellationToken.None);

    // Assert
    result.Success.Should().BeTrue();
    result.Data!.ConvertedAmount.Should().Be(112m);
}
```

```bash
# Integration test via API
curl -X POST http://localhost:5174/tools/finance/currency-convert/v1/invoke \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"amount":100,"fromCurrency":"USD","toCurrency":"EUR"}'
```

---

## 12. Using as a Project Template

ToolEngine is designed to be the foundational template for every new ONE BCG service. The following guide bootstraps a new service inheriting all security, compliance, observability, and pipeline controls.

### Step 1 — Copy the scaffold

```bash
# Copy the solution and rename
cp -r onebcg-toolengine my-new-service
cd my-new-service/code

# Replace ToolEngine with your service name across all files
# (use your IDE's global rename or the SKILL-app-template.md scaffold script)
```

### Step 2 — Keep what you need

| Component | Keep | Modify | Remove |
|---|---|---|---|
| `Core.Abstractions` | Always | Never | Never |
| `Core.Domain` (entities, Result, Error) | Always | Add domain entities | Remove sample entities |
| `Application` (pipeline behaviors) | Always | Add validators | Remove sample commands |
| `Infrastructure` (repositories, EF) | Always | Add your DbContext tables | Remove sample data |
| `Tools.*` | If tool-invocation needed | Rename namespaces | Remove entirely for non-tool services |
| `Llm.*` | If AI-agent features needed | Configure providers | Remove for non-AI services |
| `Api` host | Always | Add your endpoints | Remove sample endpoints |
| `Tools.Samples` | Development only | Replace with your tools | Remove before production |

### Step 3 — Configure constants for your service

All constant strings live in `Core.Domain.Constants`. Update:

- `ServiceLimits` — adjust limits for your workload
- `ProviderNames` — keep only providers you use
- `ConfigKeys` — add any new config section names

### Step 4 — Add your domain entities

Follow the existing pattern:

```csharp
// In Core.Domain/Entities/
public sealed class MyEntity : AggregateRoot<Guid>
{
    // Private setters — only mutated through methods that enforce invariants
    public string Name    { get; private set; }
    public string TenantId { get; private set; }

    private MyEntity() { } // EF Core

    public static Result<MyEntity> Create(string name, string tenantId)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result<MyEntity>.Failure(Error.Validation("Name is required."));

        return Result<MyEntity>.Success(new MyEntity { Name = name, TenantId = tenantId });
    }
}
```

### Step 5 — Add your CQRS commands

```csharp
// In Application/Commands/
public sealed record MyCommand(
    Guid   CorrelationId,
    string TenantId,
    string UserId,
    string Payload
) : IRequest<MyCommandResponse>;

public sealed class MyCommandHandler : IRequestHandler<MyCommand, MyCommandResponse>
{
    public async Task<MyCommandResponse> Handle(MyCommand request, CancellationToken ct)
    {
        // business logic
    }
}
```

### Step 6 — Configure appsettings for your environment

Copy `appsettings.json` as your base. At minimum configure:

```json
{
  "Jwt": { "Issuer": "my-service", "Audience": "my-service-clients", "Secret": "ENV_VAR" },
  "Database": { "Provider": "postgresql" },
  "Cache": { "Provider": "redis" }
}
```

Set production secrets via environment variables. Never commit real values.

### Step 7 — Apply quality gates

Before shipping:

- [ ] `dotnet build` with zero warnings (`TreatWarningsAsErrors = true`)
- [ ] All unit tests pass — minimum 80% coverage
- [ ] SonarQube quality gate passes (see `docs/future phase/SKILL-sonar-quality-gate.md`)
- [ ] `Approval:BaseUrl` set to `https://` in production config
- [ ] JWT secret ≥ 32 bytes confirmed
- [ ] `Tools.Samples` removed or replaced with production tools
- [ ] Tenant seeded with correct namespace allowlists
- [ ] OTLP endpoint configured for production observability

---

## 13. API Reference

### Authentication

All endpoints (except `/dev/token` and `/approvals/{token}/decide`) require:
```
Authorization: Bearer {jwt}
```

### Optional Headers

| Header | Values | Effect |
|---|---|---|
| `X-Llm-Provider` | `"anthropic"`, `"openai"`, `"ollama"` | Override LLM provider for this request |
| `X-Correlation-Id` | UUID | Attach your own correlation ID; generated if absent |

### Endpoints

#### Tools

| Method | Path | Description |
|---|---|---|
| `GET` | `/tools` | List all tools visible to the authenticated tenant |
| `POST` | `/tools/{ns}/{name}/{version}/invoke` | Invoke a specific tool |
| `POST` | `/tools/{ns}/{name}/{version}/invoke/stream` | Invoke with SSE streaming |

#### Agent

| Method | Path | Description |
|---|---|---|
| `POST` | `/agent/chat` | Free-text agent invocation |
| `POST` | `/agent/chat/stream` | Free-text agent with SSE streaming |

Agent request body:
```json
{ "text": "your question or instruction", "sessionId": "optional-session-id" }
```

#### Approvals

| Method | Path | Description |
|---|---|---|
| `GET` | `/approvals/pending` | List pending approvals for the authenticated tenant |
| `POST` | `/approvals/{token}/decide?action=approve\|deny` | Decide via magic-link token |
| `POST` | `/approvals/otp/verify` | Submit OTP for Critical-risk approvals |

OTP verify body:
```json
{ "approvalToken": "...", "otp": "123456", "approverUserId": "optional" }
```

#### Invocations

| Method | Path | Description |
|---|---|---|
| `GET` | `/invocations/{id}/status` | Poll status of an approval-suspended invocation |

Status response values: `"pending"`, `"approved"`, `"denied"`, `"expired"`

#### Health

| Method | Path | Description |
|---|---|---|
| `GET` | `/healthz/live` | Liveness — process is running |
| `GET` | `/healthz/ready` | Readiness — dependencies healthy |

#### Dev (development only)

| Method | Path | Description |
|---|---|---|
| `GET` | `/dev/token?tenant={slug}` | Generate a short-lived JWT for local testing |

### Error Response Shape

All error responses use RFC 7807 Problem Details:

```json
{
  "type": "https://tools.ietf.org/html/rfc7807",
  "title": "AGENT_LOOP_DETECTED",
  "status": 429,
  "detail": "Tool 'math.calculate' called 11 times for correlation abc-123. Circuit open — agent loop suspected."
}
```

### Response Codes

| Code | Meaning |
|---|---|
| `200` | Success — data in body |
| `202` | Accepted — approval pending, poll `/invocations/{id}/status` |
| `400` | Bad request — validation error or malformed input |
| `401` | Unauthenticated — missing or invalid JWT |
| `403` | Unauthorized — tenant inactive, namespace not allowed, or approval denied |
| `404` | Not found |
| `410` | Gone — approval expired |
| `429` | Rate limited — budget exceeded, loop detected, or OTP rate limit hit |
| `500` | Internal server error |

---

## Further Reading

| Document | Location |
|---|---|
| Implementation Guide (full detail) | `docs/IMPLEMENTATION_GUIDE.md` |
| Architecture Review | `docs/ARCHITECTURE_REVIEW.md` |
| Phase 1–5 SKILL files | `docs/SKILL-phase*.md` |
| Advancement Phase SKILLs | `docs/future phase/SKILL-advance-phase*.md` |
| Quality Standards | `docs/future phase/SKILL-quality-standards.md` |
| Unit Testing Standards | `docs/future phase/SKILL-unit-testing.md` |
| SonarQube Quality Gate | `docs/future phase/SKILL-sonar-quality-gate.md` |
| New Application Template | `docs/future phase/SKILL-app-template.md` |

---

*Confidential — Internal Use Only | ONE BCG | ToolEngine v2026*

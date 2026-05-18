# ToolEngine — Implementation Guide

**ONE BCG** | ToolEngine v2026 | .NET 8

---

## Contents

1. [Overview](#1-overview)
2. [Solution Architecture](#2-solution-architecture)
3. [Core Domain Layer](#3-core-domain-layer)
4. [Tools Abstractions](#4-tools-abstractions)
5. [Tool Registry and Discovery](#5-tool-registry-and-discovery)
6. [Tool Executor and Plan Executor](#6-tool-executor-and-plan-executor)
7. [Application Layer — CQRS and Pipeline](#7-application-layer--cqrs-and-pipeline)
8. [Infrastructure Layer](#8-infrastructure-layer)
9. [Approval Engine](#9-approval-engine)
10. [API Host](#10-api-host)
11. [CLI Host](#11-cli-host)
12. [Authoring a New Tool](#12-authoring-a-new-tool)
13. [Using the Approval Gate](#13-using-the-approval-gate)
14. [Configuration Reference](#14-configuration-reference)
15. [Deployment Notes](#15-deployment-notes)
16. [Security Hardening — Phase E](#16-security-hardening--phase-e)
17. [Provider Abstractions — Phase F](#17-provider-abstractions--phase-f)
18. [Observability — Phase G](#18-observability--phase-g)
19. [Compliance — Phase H](#19-compliance--phase-h)
20. [LLM Agent Layer — Phase L](#20-llm-agent-layer--phase-l)

---

## 1. Overview

ToolEngine is a multi-tenant, extensible tool-invocation platform built on .NET 8. It provides a structured execution pipeline for discrete, typed operations — referred to as *tools* — with built-in concerns for:

- **Multi-tenancy** — every invocation is scoped to a tenant with configurable namespaces, token budgets, and channel preferences.
- **Human-in-the-loop approval** — risk-tiered gate that auto-approves, suspends, or prompts based on tool classification.
- **Audit trail** — every invocation is persisted with full lifecycle state.
- **Extensibility** — tools are plain classes implementing a typed interface; no framework coupling.
- **Dual surface** — a RESTful API (HTTP/JWT) and an interactive CLI (REPL).

The system is designed for consumption by AI agents and human operators alike. Tools represent any discrete side-effecting or read-only operation: data lookups, LLM calls, payment actions, infrastructure commands, or composite workflows.

---

## 2. Solution Architecture

```
onebcg-toolengine/
└── code/
    └── src/
        ├── Core/
        │   ├── ToolEngine.Core.Abstractions        (pure interfaces, no implementations)
        │   └── ToolEngine.Core.Domain              (entities, contracts, Result<T>, enums)
        │
        ├── Tools/
        │   ├── ToolEngine.Tools.Abstractions       (ITool, IToolHandler, base classes, attributes)
        │   ├── ToolEngine.Tools.Registry           (IToolRegistry, IToolDiscovery, registration)
        │   ├── ToolEngine.Tools.Executor           (ToolExecutor, ToolPlanExecutor, IExecutor)
        │   └── ToolEngine.Tools.Samples            (reference tool implementations)
        │
        ├── Application/
        │   └── ToolEngine.Application              (CQRS commands, MediatR handlers, behaviors)
        │
        ├── Llm/
        │   └── ToolEngine.Llm                      (LLM providers, agent orchestrator, session store)
        │
        ├── Infrastructure/
        │   └── ToolEngine.Infrastructure           (EF Core, repositories, approval channels)
        │
        └── Hosts/
            ├── ToolEngine.Api                      (ASP.NET Core Minimal API, JWT auth)
            └── ToolEngine.Cli                      (Spectre.Console REPL)
```

### Dependency direction

```
Hosts  →  Application  →  Tools.Abstractions  →  Core.Domain  →  Core.Abstractions
       →  Llm          ↘  Core.Domain
                       →  Tools.Registry
Infrastructure         →  Core.Abstractions
                       →  Core.Domain
                       →  Tools.Abstractions
Llm                    →  Application
                       →  Tools.Registry
                       →  Core.Domain
```

Rules enforced by project references:
- `Core.Abstractions` has no NuGet or project dependencies.
- `Core.Domain` depends only on `Core.Abstractions`.
- `Infrastructure` does not depend on Application or Llm. The composition root (host) wires them.
- `Tools.Abstractions` does not reference Infrastructure or Llm.
- `Llm` does not reference Infrastructure — it uses `ICacheProvider` (Core.Abstractions) for session storage.

---

## 3. Core Domain Layer

### 3.1 Result\<T\>

Railway-oriented result type. Domain code never throws — it returns `Result.Failure(error)`.

```csharp
// Success path
Result<Tenant> result = Tenant.Create("acme", "Acme Corp", "admin", clock);
if (result.IsSuccess)
    Console.WriteLine(result.Value.Name); // "Acme Corp"

// Failure path
Result<Tenant> bad = Tenant.Create("", "Acme", "admin", clock);
Console.WriteLine(bad.IsFailure);       // true
Console.WriteLine(bad.Error.Code);      // "VALIDATION_ERROR"

// Map / Bind
Result<string> name = result.Map(t => t.Name);
Result<string> upper = result.Bind(t =>
    string.IsNullOrEmpty(t.Name)
        ? Result.Failure<string>(Error.Validation("Name empty"))
        : Result.Success(t.Name.ToUpper()));
```

### 3.2 Error

Structured error with a `SCREAMING_SNAKE_CASE` code and human-readable description.

```csharp
Error.NotFound("Tenant", "acme-corp")
Error.Validation("TenantId cannot be empty.")
Error.Unauthorized("Tenant 'acme' is inactive.")
Error.Conflict("Approval is not in Pending status.")
Error.ApprovalPending(invocationId)   // suspends execution, returns poll URL
Error.InvalidOtp()
Error.InvalidApprovalToken()
```

### 3.3 ToolRequest / ToolResponse

Single entry and exit contracts for every tool invocation.

```csharp
// Request
var request = new ToolRequest<MyInput>(
    correlationId: Guid.NewGuid(),
    tenantId:      "acme-corp",
    toolName:      "calculate",
    toolVersion:   "v1",
    input:         new MyInput(A: 10, B: 5),
    toolNamespace: "math");

// Success response
ToolResponse<MyOutput>.Ok(correlationId, new MyOutput(Result: 15));

// Failure response
ToolResponse<MyOutput>.Fail(correlationId, ToolError.NotFound("Tool 'math.calculate' not found."));

// Suspended response (awaiting approval)
ToolResponse<MyOutput>.Suspended(correlationId, pendingInvocationId);
```

### 3.4 PendingApproval entity

Durable state created when a tool execution is suspended pending human approval.

| Field | Description |
|---|---|
| `Id` | `Guid` — the invocation identifier used in poll URLs |
| `ApprovalToken` | 256-bit CSPRNG token (`RandomNumberGenerator.GetBytes(32)` → hex) — 64-character hex string, included in magic-link emails and webhook payloads |
| `OtpHash` | SHA-256 hash of the one-time password (only set for `EmailOtp` channel) |
| `Status` | `Pending` → `Approved` / `Denied` / `Expired` |
| `Risk` | `Low` / `Medium` / `High` / `Critical` |
| `Channel` | `Dashboard` / `EmailMagicLink` / `EmailOtp` / `Webhook` |
| `ExpiresAt` | UTC timestamp after which the approval auto-expires |
| `SerializedResult` | JSON result written back after the approved execution completes |

### 3.5 Tenant entity

```csharp
var tenant = Tenant.Create("acme-corp", "Acme Corp", "admin@acme.com", clock).Value;

// Restrict to specific namespaces (empty list = all allowed)
tenant.AllowNamespace("math");
tenant.AllowNamespace("weather");

// Cap per-request LLM tokens and daily tool invocations
// DailyToolCallBudget == 0 means no cap (tenant unrestricted)
tenant.SetLimits(maxResponseTokens: 8_000, dailyBudget: 5_000);

// Link to a secret vault entry (never the raw key)
tenant.SetLlmProvider("openai", secretRef: "acme/openai-key");
```

---

## 4. Tools Abstractions

### 4.1 ITool

Every tool implements `ITool` via one of the four base classes.

```csharp
public interface ITool
{
    string Namespace { get; }  // e.g. "math"
    string Name      { get; }  // e.g. "calculate"
    string Version   { get; }  // e.g. "v1"
    string FullName  => $"{Namespace}.{Name}";
    ToolSchema Schema { get; }
}
```

### 4.2 Base classes

| Base class | Use for |
|---|---|
| `LogicToolBase<TIn, TOut>` | Pure computation, no I/O |
| `ApiToolBase<TIn, TOut>` | HTTP calls to external services |
| `DatabaseToolBase<TIn, TOut>` | Database queries or mutations |
| `CompositeToolBase<TIn, TOut>` | Orchestrates other tools |

All four expose `public string FullName => $"{Namespace}.{Name}";`.

### 4.3 ToolSchema

Rich schema attached to every tool — drives discovery, routing, and agent guidance.

```csharp
public ToolSchema Schema => new(
    Namespace:    "hr",
    Name:         "user-lookup",
    Version:      "v1",
    Description:  "Retrieves a user record by employee ID.",
    WhenToUse:    "When the agent needs to resolve an employee ID to a full profile.",
    WhenNotToUse: "Do not use for bulk exports — use hr.bulk-export instead.",
    Parameters:   [new ToolParameter("employeeId", "string", "Employee identifier", Required: true)],
    Examples:     [new ToolExample("""{"employeeId":"E12345"}""", """{"name":"Jane Doe","dept":"Eng"}""")]
);
```

### 4.4 RequiresApprovalAttribute

Marks a tool as requiring human approval before execution. The MediatR `ApprovalBehavior` reads this via `IToolDiscovery` — no tight coupling to the registry.

```csharp
[RequiresApproval(
    Risk:   ApprovalRisk.High,
    Reason: "This tool modifies live HR records and cannot be undone within 24 hours.")]
public class UpdateEmployeeHandler : LogicToolBase<UpdateInput, UpdateOutput>
{
    // ...
}
```

Risk tiers:

| Risk | Behaviour |
|---|---|
| `Low` | Auto-approved. Audit log only. No gate interaction. |
| `Medium` | Suspended. Notifies via configured channel. Returns HTTP 202. |
| `High` | Same as Medium but always requires explicit decision. |
| `Critical` | Forces `EmailOtp` channel regardless of tenant preference. |

---

## 5. Tool Registry and Discovery

### 5.1 IToolRegistry — registration

```csharp
// Register a tool handler for a given version.
// FullName is derived from the handler's own Namespace + Name — no drift possible.
registry.Register<CalculateHandler>("v1");
registry.Register<CalculateHandler>("v2");   // multiple versions supported

// Resolve by namespace + name + version (tenant-scoped)
Result<ToolDescriptor> descriptor = registry.Resolve("math", "calculate", "v1", "acme-corp");

// List all registered tools (optionally filtered by tenant)
IReadOnlyList<ToolDescriptor> all = registry.ListAll("acme-corp");

// List versions for a specific tool
IReadOnlyList<string> versions = registry.GetVersions("math", "calculate");
```

### 5.2 IToolDiscovery — semantic search

```csharp
// Exact resolve — O(1)
Result<ToolDiscoveryDescriptor> desc = discovery.Resolve("math", "calculate", "v1", "acme");

// Semantic search — word-overlap scoring on Description + WhenToUse
IReadOnlyList<ToolDiscoveryDescriptor> results =
    await discovery.SearchAsync("add two numbers", "acme-corp", topK: 3);

// ToolDiscoveryDescriptor carries approval metadata
Console.WriteLine(desc.Value.NeedsApproval);   // true / false
Console.WriteLine(desc.Value.ApprovalRisk);    // High
Console.WriteLine(desc.Value.ApprovalReason);  // "Modifies live HR records..."
```

---

## 6. Tool Executor and Plan Executor

### 6.1 Direct tool execution

```csharp
var result = await executor.ExecuteAsync<MyInput, MyOutput>(
    new ToolRequest<MyInput>(
        correlationId: Guid.NewGuid(),
        tenantId:      "acme-corp",
        toolName:      "calculate",
        toolVersion:   "v1",
        input:         new MyInput(A: 10, B: 5),
        toolNamespace: "math"));
```

### 6.2 ToolPlanExecutor — multi-step execution

Three execution modes for orchestrating multiple tool calls in a plan.

#### Sequential (fail-fast)

```csharp
var plan = new ToolPlan(
    PlanId: Guid.NewGuid(),
    Mode:   ExecutionMode.Sequential,
    Steps: [
        new ToolStep("step-1", "math", "add",       "v1", inputA),
        new ToolStep("step-2", "math", "multiply",  "v1", inputB),
        new ToolStep("step-3", "report", "generate","v1", inputC)
    ]);

var results = await planExecutor.ExecuteAsync(plan, "acme-corp", ct);
// If step-2 fails, step-3 is skipped immediately.
```

#### Parallel

```csharp
var plan = new ToolPlan(
    PlanId: Guid.NewGuid(),
    Mode:   ExecutionMode.Parallel,
    Steps: [
        new ToolStep("fetch-weather", "weather", "current", "v1", weatherInput),
        new ToolStep("fetch-stocks",  "finance", "quote",   "v1", stockInput),
        new ToolStep("fetch-news",    "news",    "latest",  "v1", newsInput)
    ]);

// All three execute concurrently via Task.WhenAll.
```

#### DAG (dependency graph)

```csharp
var plan = new ToolPlan(
    PlanId: Guid.NewGuid(),
    Mode:   ExecutionMode.Dag,
    Steps: [
        new ToolStep("fetch-user",    "hr",     "user-lookup", "v1", userInput,
                     DependsOn: []),
        new ToolStep("fetch-dept",    "hr",     "dept-lookup", "v1", deptInput,
                     DependsOn: ["fetch-user"]),
        new ToolStep("build-report",  "report", "generate",    "v1", reportInput,
                     DependsOn: ["fetch-user", "fetch-dept"])
    ]);

// Wave 1: fetch-user (no deps)
// Wave 2: fetch-dept (after fetch-user completes)
// Wave 3: build-report (after both complete)
```

---

## 7. Application Layer — CQRS and Pipeline

### 7.1 ExecuteToolCommand

The single CQRS command used by all callers.

```csharp
var command = new ExecuteToolCommand<JsonElement, JsonElement>(
    CorrelationId:    Guid.NewGuid(),
    TenantId:         "acme-corp",
    UserId:           "user-123",
    ToolName:         "calculate",
    ToolVersion:      "v1",
    Input:            JsonDocument.Parse("""{"a":10,"b":5}""").RootElement,
    ToolType:         ToolType.Logic,
    ToolNamespace:    "math",
    MaxResponseTokens: 4_000);   // tenant cap enforced by TokenBudgetBehavior

var response = await mediator.Send(command, ct);
```

### 7.2 MediatR pipeline

Behaviors execute outermost to innermost. Each is a guard that either short-circuits with an error response or calls `next()`.

Authorization runs before Validation so that unauthorized callers never receive detailed field-level validation errors (OWASP A01:2025 Broken Access Control).

```
Request
  │
  ▼
┌─────────────────────────────────────────┐
│  1. TenantAuthorizationBehavior         │  Loads Tenant from DB. Rejects unknown,
│                                         │  inactive, or namespace-blocked tenants.
│                                         │  Auth before Validation — OWASP A01:2025.
├─────────────────────────────────────────┤
│  2. ValidationBehavior                  │  FluentValidation — rejects malformed input.
│                                         │  Only reached by authorized callers.
├─────────────────────────────────────────┤
│  3. TokenBudgetBehavior                 │  Compares MaxResponseTokens against tenant cap.
│                                         │  Rejects if exceeded.
├─────────────────────────────────────────┤
│  4. DailyBudgetBehavior                 │  Counts today's ToolInvocationRecords for the
│                                         │  tenant. Rejects if DailyToolCallBudget reached.
│                                         │  0 = no cap (tenant unrestricted).
├─────────────────────────────────────────┤
│  5. LoopDetectionBehavior               │  Counts invocations per (correlationId, tool).
│                                         │  Circuit-opens after threshold (default 10).
├─────────────────────────────────────────┤
│  6. ApprovalBehavior                    │  Reads [RequiresApproval] via IToolDiscovery.
│                                         │  Routes to IHumanApprovalGate. Suspends,
│                                         │  denies, or passes through.
├─────────────────────────────────────────┤
│  7. AuditBehavior                       │  Creates ToolInvocationRecord before handler.
│                                         │  Marks succeeded or failed after.
└─────────────────────────────────────────┘
  │
  ▼
Handler (ExecuteToolCommandHandler)
```

### 7.3 Short-circuit response codes

| Behavior | HTTP | Error code |
|---|---|---|
| TenantAuthorizationBehavior (not found / inactive) | 401 | `UNAUTHORIZED` |
| TenantAuthorizationBehavior (namespace blocked) | 403 | `UNAUTHORIZED` |
| ValidationBehavior | 400 | `VALIDATION_ERROR` |
| TokenBudgetBehavior | 400 | `TOKEN_BUDGET_EXCEEDED` |
| DailyBudgetBehavior | 429 | `DAILY_BUDGET_EXCEEDED` |
| LoopDetectionBehavior | 429 | `AGENT_LOOP_DETECTED` |
| ApprovalBehavior — suspended | 202 | `APPROVAL_PENDING` |
| ApprovalBehavior — denied | 403 | `APPROVAL_DENIED` |

---

## 8. Infrastructure Layer

### 8.1 Repository pattern

Two repository interfaces — write and read — prevent read queries from going through the write path.

```csharp
// Write (tracked by EF change tracker)
IRepository<PendingApproval, Guid> repo;
await repo.AddAsync(entity, ct);
repo.Update(entity);
repo.Remove(entity);

// Read (AsNoTracking, ISpecification)
IReadRepository<PendingApproval, Guid> readRepo;
var single  = await readRepo.GetByIdAsync(id, ct);
var all     = await readRepo.ListAllAsync(ct);
var filtered = await readRepo.ListAsync(
    new LambdaSpecification<PendingApproval>(
        a => a.TenantId == "acme-corp" && a.Status == ApprovalStatus.Pending),
    ct);
```

### 8.2 LambdaSpecification\<T\>

Inline specification — no need to create a dedicated spec class for one-off queries.

```csharp
var spec = new LambdaSpecification<PendingApproval>(
    a => a.ApprovalToken == token);
var results = await readRepo.ListAsync(spec, ct);
```

### 8.3 EF Core configuration

The `AppDbContext` applies all configurations from the assembly automatically:

```csharp
protected override void OnModelCreating(ModelBuilder builder)
    => builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
```

`PendingApprovalConfiguration` adds:
- Unique index on `ApprovalToken`
- Index on `(TenantId, Status)` for efficient dashboard queries
- Max-length constraints on string columns

---

## 9. Approval Engine

### 9.1 Architecture

```
ApprovalBehavior (Application)
    │  builds ApprovalContext from IExecuteToolCommand
    │
    ▼
IHumanApprovalGate
    │
    ├── AsyncApprovalGate (API host)
    │       creates PendingApproval in DB
    │       calls ApprovalChannelSelector
    │           │
    │           ├── DashboardChannel       (no push — approver polls)
    │           ├── EmailMagicLinkChannel  (approve/deny URLs in email)
    │           ├── EmailOtpChannel        (6-digit OTP; forced for Critical)
    │           └── WebhookChannel         (POST to Slack/Teams/custom)
    │       returns ApprovalDecision.Suspend(pendingInvocationId)
    │
    └── ConsoleApprovalGate (CLI host)
            Spectre.Console Y/N prompt
            returns ApprovalDecision.Allow / .Deny synchronously
```

### 9.2 Risk routing

| Risk | Gate behaviour | Channel |
|---|---|---|
| `Low` | Auto-approved immediately | None |
| `Medium` | Suspended — notification sent | Tenant default channel |
| `High` | Suspended — notification sent | Tenant default channel |
| `Critical` | Suspended — OTP required | `EmailOtp` (forced) |

Tenant channel overrides via config:
```json
"Approval": {
  "DefaultChannel": "Dashboard",
  "TenantChannelOverrides": {
    "acme-corp": "Webhook",
    "beta-tenant": "EmailMagicLink"
  }
}
```

### 9.3 PendingApproval lifecycle

```
Create (AsyncApprovalGate)
    │
    ▼
 Pending ──────────── Expired (timeout reached, lazy on next read)
    │
    ├── Approve(decidedByUserId)  →  Approved
    └── Deny(decidedByUserId)     →  Denied
```

### 9.4 Channel detail

#### DashboardChannel
No external notification. The `PendingApproval` record is already in the DB. The approver logs into the dashboard and acts via `GET /approvals/pending`.

#### EmailMagicLinkChannel
Sends an email containing two one-click URLs:

```
Approve: POST https://app.onebcg.com/approvals/{token}/decide?action=approve
Deny:    POST https://app.onebcg.com/approvals/{token}/decide?action=deny
```

The `token` is a **256-bit CSPRNG secret** — `Convert.ToHexString(RandomNumberGenerator.GetBytes(32))` — producing a 64-character hex string. It acts as the shared secret; no JWT is required on the decide endpoint. The use of `RandomNumberGenerator` (not `Guid.NewGuid()`) satisfies the OWASP minimum 128-bit CSPRNG entropy requirement for passwordless authentication tokens.

#### EmailOtpChannel
For `Critical` risk tools only. Sends a 6-digit OTP to `ApproverEmail`. The OTP is:
1. Generated using `RandomNumberGenerator` (cryptographically secure — NIST SP 800-63B compliant).
2. SHA-256 hashed before storage — never stored in plaintext.
3. Verified by `POST /approvals/otp/verify` — hash of submitted OTP is compared to stored hash.

**Rate limiting and lockout** (OWASP MFA Cheat Sheet compliance):
- The OTP verify endpoint is rate-limited at the IP level: **10 attempts per IP per 10 minutes** via ASP.NET Core's `SlidingWindowRateLimiter`. Excess requests receive `429 Too Many Requests` with a `Retry-After: 60` header.
- The `PendingApproval` entity tracks `FailedOtpAttempts`. After **5 consecutive failures** on the same token, `IncrementFailedOtpAttempts()` transitions the approval to `Expired`, permanently invalidating it. The user must request a new approval.
- Remaining attempts are surfaced in the `400` error body: `"Invalid OTP. 3 attempt(s) remaining."` so the approver knows their position without probing blindly.

#### WebhookChannel
POSTs a JSON payload to `ApprovalOptions.WebhookUrl`:

```json
{
  "invocationId":  "3fa85f64-...",
  "toolFullName":  "hr.update-employee",
  "risk":          "High",
  "reason":        "Modifies live HR records.",
  "tenantId":      "acme-corp",
  "requestedBy":   "user-123",
  "expiresAt":     "2026-05-18T14:30:00Z",
  "approveUrl":    "https://app.onebcg.com/approvals/{token}/decide?action=approve",
  "denyUrl":       "https://app.onebcg.com/approvals/{token}/decide?action=deny"
}
```

The Slack/Teams handler at that URL should POST back to `/approvals/{token}/decide` with `{ "action": "approve" }` or `{ "action": "deny" }`.

### 9.5 HTTP 202 flow (API)

```
Client                        API                              Approver
  │                             │                                   │
  │  POST /tools/hr/update/v1   │                                   │
  │ ─────────────────────────► │                                   │
  │                             │  ApprovalBehavior suspends        │
  │                             │  AsyncApprovalGate creates DB row │
  │                             │  Channel sends notification ─────►│
  │  202 Accepted               │                                   │
  │ ◄───────────────────────── │                                   │
  │  { "invocationId": "...",   │                                   │
  │    "pollUrl": "..." }        │                                   │
  │                             │         (approver clicks link)    │
  │                             │  POST /approvals/{token}/decide ◄─│
  │                             │  Approval.Approve("approver-id")  │
  │                             │                                   │
  │  GET /invocations/{id}/status│                                   │
  │ ─────────────────────────► │                                   │
  │  200 { "status": "approved" }│                                   │
  │ ◄───────────────────────── │                                   │
```

---

## 10. API Host

### 10.1 Authentication

All invocation endpoints require a JWT bearer token. Claims expected:

| Claim | Used as |
|---|---|
| `tenant_id` | `TenantId` in commands |
| `sub` (NameIdentifier) | `UserId` in commands |

Dev token generation: `GET /dev/token?tenantId=acme-corp&userId=user-123` (development environment only).

### 10.2 Endpoints

#### Tool invocation

```http
POST /tools/{ns}/{name}/{version}/invoke
Authorization: Bearer {token}
X-Correlation-Id: {guid}           (optional — generated if absent)
Content-Type: application/json

{ ...tool-specific input... }
```

**Success (200)**
```json
{
  "correlationId": "3fa85f64-...",
  "success": true,
  "data": { ...tool output... },
  "metrics": { "durationMs": 42 },
  "timestamp": "2026-05-18T10:00:00Z"
}
```

**Suspended (202)**
```json
{
  "status": "pending_approval",
  "invocationId": "9c112b4a-...",
  "pollUrl": "/invocations/9c112b4a-.../status",
  "message": "Tool execution is suspended pending human approval."
}
```

**Error (4xx/5xx)**
```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.3",
  "title": "APPROVAL_DENIED",
  "status": 403,
  "detail": "Human approval was denied for tool 'hr.update-employee'."
}
```

#### Tool listing

```http
GET /tools
GET /tools/{ns}/{name}/versions
```

#### Approval — magic-link decision

```http
POST /approvals/{token}/decide?action=approve
Content-Type: application/json

{ "decidedByUserId": "approver-jane" }
```

```http
POST /approvals/{token}/decide?action=deny
Content-Type: application/json

{ "decidedByUserId": "approver-jane", "reason": "Not authorised at this time." }
```

**Response (200)**
```json
{
  "invocationId": "9c112b4a-...",
  "status": "Approved",
  "decidedBy": "approver-jane",
  "decidedAt": "2026-05-18T10:05:00Z"
}
```

#### Approval — OTP verification

```http
POST /approvals/otp/verify
Content-Type: application/json

{
  "approvalToken": "3fa85f64abc...",
  "otp": "847291",
  "approverUserId": "approver-jane"
}
```

#### Approval dashboard (authenticated)

```http
GET /approvals/pending
Authorization: Bearer {token}
```

**Response (200)**
```json
[
  {
    "invocationId":  "9c112b4a-...",
    "toolFullName":  "hr.update-employee",
    "risk":          "High",
    "reason":        "Modifies live HR records.",
    "requestedBy":   "user-123",
    "channel":       "Dashboard",
    "expiresAt":     "2026-05-18T11:00:00Z",
    "createdAt":     "2026-05-18T10:00:00Z"
  }
]
```

#### Invocation status poll

```http
GET /invocations/{id}/status
Authorization: Bearer {token}
```

**Response — pending**
```json
{
  "invocationId": "9c112b4a-...",
  "toolFullName": "hr.update-employee",
  "status":       "pending",
  "risk":         "High",
  "channel":      "Dashboard",
  "createdAt":    "2026-05-18T10:00:00Z",
  "expiresAt":    "2026-05-18T11:00:00Z",
  "decidedAt":    null,
  "result":       null
}
```

**Response — approved and completed**
```json
{
  "invocationId": "9c112b4a-...",
  "status":       "approved",
  "decidedAt":    "2026-05-18T10:05:00Z",
  "decidedByUserId": "approver-jane",
  "result":       { ...tool output... }
}
```

### 10.3 SSE streaming

```http
POST /tools/{ns}/{name}/{version}/stream
Authorization: Bearer {token}
Content-Type: application/json

{ ...input... }
```

Response: `Content-Type: text/event-stream`

```
data: {"correlationId":"...","content":"Hello","index":0,"isFinal":false}

data: {"correlationId":"...","content":" World","index":1,"isFinal":false}

data: {"correlationId":"...","content":"","index":2,"isFinal":true}
```

---

## 11. CLI Host

### 11.1 Starting the REPL

```bash
cd src/Hosts/ToolEngine.Cli
dotnet run
```

```
ToolEngine CLI — type help for commands.

> help
  list                                          List all registered tools.
  invoke <ns> <name> <version> <json>          Invoke a tool directly with JSON input.
  ask <text>                                    Single-turn: LLM selects and invokes the right tool.
  chat                                          Enter multi-turn chat session.
  chat end                                      End the current chat session.
  exit / quit                                   Exit the REPL.
```

### 11.2 Invoking a tool directly

```
> invoke math calculate v1 {"a":10,"b":5,"operator":"add"}
Tool result:
{
  "result": 15
}
```

### 11.3 Single-turn LLM (ask)

The `ask` command sends free-text to the configured LLM. The LLM selects the correct tool, the full 8-behavior MediatR pipeline executes it, and the result is fed back to the LLM for a natural-language reply.

```
> ask what is 25 times 48?
Tool selected: math.calculate
Tool result:
{
  "result": 1200
}

25 times 48 is 1200.
Tokens: 142 | Cost: $0.0004 | Session: a1b2c3d4...
```

### 11.4 Multi-turn chat

`chat` creates a persistent session. Every subsequent line is sent as a message in that session. History is preserved across turns in the session store (backed by `ICacheProvider`).

```
> chat
Chat mode started. Type chat end to exit.
chat> What is the capital of France?
Paris is the capital of France.
Tokens: 88 | Cost: $0.0002 | Session: f7e6d5c4...

chat> And what is its population?
The population of Paris proper is approximately 2.1 million...
Tokens: 210 | Cost: $0.0006 | Session: f7e6d5c4...

chat> chat end
Chat session ended.
```

The default CLI provider is `ollama` (zero API cost for local development). Override via `appsettings.json` or `ANTHROPIC_API_KEY` / `OPENAI_API_KEY` environment variables.

### 11.5 Approval prompt (CLI)

When a `[RequiresApproval]` tool is invoked in the CLI (directly or via LLM selection), `ConsoleApprovalGate` halts execution and presents a coloured prompt:

```
⚠  Approval Required — High Risk
  Tool   : hr.update-employee
  Reason : Modifies live HR records and cannot be undone within 24 hours.

Allow execution of 'hr.update-employee'? [y/n] (n): _
```

Risk colour coding: `Critical` = red, `High` = orange, `Medium` = yellow.

Answering `n` returns `APPROVAL_DENIED` immediately. Answering `y` proceeds to execution.

---

## 12. Authoring a New Tool

### Step 1 — Create the handler class

```csharp
namespace ToolEngine.Tools.Samples.Weather;

using ToolEngine.Tools.Abstractions.Base;
using ToolEngine.Core.Domain.Contracts;

public sealed class WeatherHandler : ApiToolBase<WeatherInput, WeatherOutput>
{
    public override string Namespace => "weather";
    public override string Name      => "current";
    public override string Version   => "v1";

    public override ToolSchema Schema => new(
        Namespace:    "weather",
        Name:         "current",
        Version:      "v1",
        Description:  "Returns the current weather for a given city.",
        WhenToUse:    "When the user asks about current weather conditions.",
        WhenNotToUse: "Do not use for forecasts — use weather.forecast instead.",
        Parameters:   [new ToolParameter("city", "string", "City name", Required: true)],
        Examples:     [new ToolExample("""{"city":"London"}""", """{"temp":15,"condition":"Cloudy"}""")]
    );

    protected override async Task<ToolResponse<WeatherOutput>> ExecuteCoreAsync(
        ToolRequest<WeatherInput> request, CancellationToken ct)
    {
        // Implementation
        var output = new WeatherOutput(Temp: 15, Condition: "Cloudy");
        return ToolResponse<WeatherOutput>.Ok(request.CorrelationId, output);
    }
}

public sealed record WeatherInput(string City);
public sealed record WeatherOutput(int Temp, string Condition);
```

### Step 2 — Add [RequiresApproval] if needed

```csharp
using ToolEngine.Core.Domain.Attributes;
using ToolEngine.Core.Domain.Enums;

[RequiresApproval(
    Risk:   ApprovalRisk.High,
    Reason: "This tool sends alerts to all employees. Confirm before executing.")]
public sealed class BroadcastAlertHandler : LogicToolBase<AlertInput, AlertOutput>
{
    // ...
}
```

### Step 3 — Register in DI

```csharp
// In your Tools.Samples extension or the host's Program.cs
public static IServiceCollection AddToolSamples(this IServiceCollection services)
{
    services.AddToolRegistry();

    // Register handler in DI
    services.AddTransient<WeatherHandler>();
    services.AddTransient<BroadcastAlertHandler>();

    return services;
}
```

### Step 4 — Register with IToolRegistry

```csharp
// In an IHostedService or startup routine
registry.Register<WeatherHandler>("v1");
registry.Register<BroadcastAlertHandler>("v1");
```

### Step 5 — Verify

```bash
GET /tools
# WeatherHandler and BroadcastAlertHandler appear in the list

POST /tools/weather/current/v1/invoke
{ "city": "London" }
# Returns 200 with weather data

POST /tools/hr/broadcast-alert/v1/invoke
{ "message": "System maintenance tonight." }
# Returns 202 if approval is pending, or prompts in CLI
```

---

## 13. Using the Approval Gate

### When to apply [RequiresApproval]

Apply the attribute to any tool that:

- Modifies persistent state (database writes, file changes, user updates)
- Sends external communications (emails, SMS, Slack messages, alerts)
- Initiates financial transactions (payments, refunds, subscription changes)
- Executes infrastructure operations (deployments, restarts, configuration changes)
- Takes actions that are difficult or impossible to reverse

Do not apply the attribute to:
- Read-only lookups
- Pure computation
- Idempotent queries with no side effects

### Choosing the risk tier

| Risk | Guidance |
|---|---|
| `Low` | No gate required — attribute adds audit context only. Rarely used directly; omit the attribute instead. |
| `Medium` | Reversible within a reasonable window. Examples: send a draft email, stage a deployment. |
| `High` | Difficult to reverse or has significant downstream impact. Examples: update HR records, post to a live channel. |
| `Critical` | Irreversible or financially significant. Examples: process a payment, delete an account, send a mass broadcast. OTP is mandatory. |

### What the approver receives

**EmailMagicLink** — email with tool name, reason, input summary, and one-click approve/deny links. No login required.

**EmailOtp** — email with the same summary plus a 6-digit OTP valid for the configured timeout. The approver submits the OTP to `POST /approvals/otp/verify`.

**Webhook** — JSON payload delivered to the configured URL (Slack, Teams, or custom endpoint). The handler responds by POSTing back to the decide URL.

**Dashboard** — no push notification. The approver visits `GET /approvals/pending` and acts from there.

### Client polling pattern

```
1. POST /tools/{ns}/{name}/{version}/invoke
   → 202 { "invocationId": "...", "pollUrl": "..." }

2. Poll GET /invocations/{id}/status at a suitable interval (e.g. every 10 seconds)
   → { "status": "pending" }    (keep polling)
   → { "status": "approved" }   (proceed — result included if execution completed)
   → { "status": "denied" }     (halt — surface reason to user)
   → { "status": "expired" }    (approval window closed — retry if appropriate)
```

Recommended: use exponential back-off with a maximum interval of 60 seconds. Do not poll more frequently than every 5 seconds.

---

## 14. Configuration Reference

### appsettings.json (API)

```json
{
  "ConnectionStrings": {
    "Default": "Data Source=toolengine-dev.db"
  },
  "Jwt": {
    "Secret":   "your-256-bit-secret-key-here",
    "Issuer":   "toolengine-api",
    "Audience": "toolengine-clients"
  },
  "Approval": {
    "DefaultChannel":        "Dashboard",
    "ApprovalTimeoutMinutes": 60,
    "BaseUrl":               "https://app.onebcg.com",
    "WebhookUrl":            "https://hooks.slack.com/services/...",
    "TenantChannelOverrides": {
      "acme-corp":    "Webhook",
      "beta-tenant":  "EmailMagicLink"
    }
  },
  "LoopDetection": {
    "MaxCallsPerCorrelation": 10
  },
  "Llm": {
    "DefaultProvider": "anthropic",
    "Routing": {
      "FallbackChain": ["anthropic", "openai"]
    },
    "Budget": {
      "MaxTokensPerRequest":  4096,
      "MaxTokensPerSession":  32768,
      "MaxIterations":        10
    },
    "Providers": {
      "anthropic": {
        "Model":         "claude-opus-4-5",
        "ApiKeyEnvVar":  "ANTHROPIC_API_KEY",
        "MaxTokens":     4096,
        "TimeoutSeconds": 60
      },
      "openai": {
        "Model":         "gpt-4o",
        "ApiKeyEnvVar":  "OPENAI_API_KEY",
        "MaxTokens":     4096,
        "TimeoutSeconds": 60
      },
      "ollama": {
        "BaseUrl":       "http://localhost:11434",
        "Model":         "llama3.1:8b",
        "MaxTokens":     4096,
        "TimeoutSeconds": 120
      }
    }
  },
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System":    "Warning"
      }
    }
  }
}
```

### Configuration sections

| Section | Key | Default | Description |
|---|---|---|---|
| `Approval` | `DefaultChannel` | `Dashboard` | Channel used when no tenant override is set |
| `Approval` | `ApprovalTimeoutMinutes` | `60` | Minutes before a Pending approval auto-expires |
| `Approval` | `BaseUrl` | `http://localhost:5174` | Base URL prepended to magic-link and OTP verify URLs |
| `Approval` | `WebhookUrl` | `null` | Webhook target URL; required if any tenant uses `Webhook` channel |
| `Approval` | `TenantChannelOverrides` | `{}` | Per-tenant channel override map |
| `LoopDetection` | `MaxCallsPerCorrelation` | `10` | Max same-tool invocations before circuit opens |
| `Jwt` | `Secret` | — | HMAC-SHA256 signing key (min 256 bits) |
| `Jwt` | `Issuer` / `Audience` | — | JWT validation parameters |
| `Llm` | `DefaultProvider` | `"anthropic"` | Provider used when no tenant or tool override applies |
| `Llm:Routing` | `FallbackChain` | `[]` | Ordered list of providers tried on `CompleteAsync` failure |
| `Llm:Budget` | `MaxTokensPerRequest` | `4096` | `max_tokens` sent to the provider API per call |
| `Llm:Budget` | `MaxTokensPerSession` | `32768` | Cumulative token ceiling per session — circuit breaker |
| `Llm:Budget` | `MaxIterations` | `10` | Max agentic loop iterations before `MaxIterationsExceeded` |
| `Llm:Providers:{name}` | `Model` | — | Provider-specific model identifier |
| `Llm:Providers:{name}` | `ApiKeyEnvVar` | — | Environment variable name holding the API key |
| `Llm:Providers:{name}` | `BaseUrl` | — | Ollama only — base URL of the local server |
| `Llm:Providers:{name}` | `TimeoutSeconds` | `60` | HTTP request timeout for provider calls |

### Provider routing precedence

1. `[LlmProvider("ollama")]` attribute on the tool handler class
2. `Tenant.LlmProviderOverride` (set via `tenant.SetLlmProvider(...)`)
3. `Llm:DefaultProvider` from `appsettings.json`
4. `Llm:Routing:FallbackChain` — tried in order if the selected provider fails

### Environment-specific overrides

```bash
# Override via environment variable (recommended for production secrets)
APPROVAL__BASEURL="https://app.onebcg.com"
APPROVAL__WEBHOOKURL="https://hooks.slack.com/services/..."
JWT__SECRET="$(cat /run/secrets/jwt_secret)"

# LLM provider API keys — never in config files
ANTHROPIC_API_KEY="sk-ant-..."
OPENAI_API_KEY="sk-..."

# Override default provider for a deployment
LLM__DEFAULTPROVIDER="openai"
```

---

## 15. Deployment Notes

### Database

The default configuration uses SQLite (`toolengine-dev.db`). For production, swap the provider in Program.cs:

```csharp
// SQL Server
builder.Services.AddToolInfrastructure(
    opt => opt.UseSqlServer(connectionString));

// PostgreSQL
builder.Services.AddToolInfrastructure(
    opt => opt.UseNpgsql(connectionString));
```

`EnsureCreated()` is called in development only. For production, use EF Core migrations:

```bash
dotnet ef migrations add InitialCreate \
  --project src/Infrastructure/ToolEngine.Infrastructure \
  --startup-project src/Hosts/ToolEngine.Api

dotnet ef database update \
  --startup-project src/Hosts/ToolEngine.Api
```

### Secret vault

`NullSecretVault` is the dev stub — replace with a real implementation for production:

```csharp
// Register Azure Key Vault implementation
services.AddSingleton<ISecretVault, AzureKeyVaultSecretVault>();
```

### Email sender

`LoggingEmailSender` logs email content to console only — no messages are sent. Replace for production:

```csharp
// SendGrid
services.AddSingleton<IEmailSender, SendGridEmailSender>();

// SMTP
services.AddSingleton<IEmailSender, SmtpEmailSender>();
```

### Loop detection — distributed deployments

`LoopDetectionBehavior` uses an in-process `ConcurrentDictionary`. In a horizontally scaled deployment, replace with a distributed cache:

```csharp
// Redis-backed implementation (example)
public sealed class RedisLoopDetectionBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
{
    // Use IConnectionMultiplexer with INCR + EXPIRE per key
}
```

### Health checks

```http
GET /health
→ 200 { "status": "Healthy" }
```

---

---

## 16. Security Hardening — Phase E

Phase E resolves eight security gaps identified in the architecture review against OWASP, NIST, and internal compliance requirements. All items are implemented and build clean.

### E1 — CSPRNG approval token (Critical)

**Before:** `ApprovalToken = Guid.NewGuid().ToString("N")` — 122 bits of entropy, predictable UUID structure.

**After:**
```csharp
// PendingApproval constructor
ApprovalToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
// Result: 64-char hex string, 256 bits of CSPRNG entropy
```

Why it matters: UUID v4 has fixed version/variant nibbles that reduce effective entropy and make format-based enumeration marginally easier. OWASP requires CSPRNG for passwordless authentication tokens. The new token exceeds the OWASP minimum (128 bits) by 2×.

### E2 — OTP attempt counter and per-token lockout (Critical)

`PendingApproval` now tracks failed OTP attempts. After 5 consecutive failures, the approval is irreversibly expired — a new approval request is required.

```csharp
// Entity method
public bool IncrementFailedOtpAttempts(int maxAttempts = 5)
{
    FailedOtpAttempts++;
    if (FailedOtpAttempts >= maxAttempts) { Expire(); return true; }
    return false;
}

// VerifyOtp endpoint
var locked = tracked.IncrementFailedOtpAttempts(maxAttempts: 5);
await uow.SaveChangesAsync(ct);

return locked
    ? Results.Problem("Maximum OTP attempts exceeded. Approval request has been invalidated.",
        statusCode: 410, title: "APPROVAL_EXPIRED")
    : Results.Problem($"Invalid OTP. {5 - tracked.FailedOtpAttempts} attempt(s) remaining.",
        statusCode: 400, title: "INVALID_OTP");
```

This satisfies the OWASP MFA Cheat Sheet entity-level lockout requirement independently of IP-based rate limiting.

### E3 — IP-level rate limiting on OTP verify endpoint (Critical)

```csharp
// Program.cs
builder.Services.AddRateLimiter(opt =>
{
    opt.AddPolicy("otp-verify", ctx =>
        RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                Window            = TimeSpan.FromMinutes(10),
                SegmentsPerWindow = 5,
                PermitLimit       = 10,
                QueueLimit        = 0
            }));

    opt.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    opt.OnRejected = async (ctx, token) =>
    {
        ctx.HttpContext.Response.Headers["Retry-After"] = "60";
        await ctx.HttpContext.Response.WriteAsJsonAsync(
            new { error = "TOO_MANY_REQUESTS",
                  description = "Too many OTP verification attempts. Try again in 10 minutes." },
            token);
    };
});

// Middleware pipeline — must precede auth
app.UseRateLimiter();

// Endpoint
group.MapPost("/otp/verify", VerifyOtp)
     .RequireRateLimiting("otp-verify");
```

The two-layer defence (IP rate limit + per-token attempt counter) addresses the OWASP 2025 real-world finding: "no rate limiting on OTP verification endpoint."

### E4 — Pipeline reorder: TenantAuth before Validation (Critical / OWASP A01:2025)

`ServiceCollectionExtensions` registers behaviors in this order (outermost first):

```csharp
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TenantAuthorizationBehavior<,>));
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TokenBudgetBehavior<,>));
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(DailyBudgetBehavior<,>));
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoopDetectionBehavior<,>));
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ApprovalBehavior<,>));
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(AuditBehavior<,>));
```

Unauthorized callers receive `401 UNAUTHORIZED` before they receive any `400 VALIDATION_ERROR` field-level detail.

### E5 — DailyBudgetBehavior (High)

`DailyBudgetBehavior<TRequest, TResponse>` enforces `Tenant.DailyToolCallBudget`. It runs after `TokenBudgetBehavior` and before `LoopDetectionBehavior`.

```csharp
// Uses COUNT(*) on today's ToolInvocationRecords for the tenant.
// DailyToolCallBudget == 0 means no cap — tenant unrestricted.
if (todayCount >= tenant.DailyToolCallBudget)
    return Fail(cmd, new ToolError("DAILY_BUDGET_EXCEEDED",
        $"Tenant '{cmd.TenantId}' has reached its daily tool-call budget " +
        $"of {tenant.DailyToolCallBudget}. Budget resets at midnight UTC.", 429));
```

**Performance note:** Currently issues one `COUNT(*)` query per request. Phase F replaces this with a Redis `INCR` counter keyed on `tenantId + date` for O(1) distributed enforcement.

### E6 — RFC 7231 §6.3.3 compliance on 202 response (High)

```csharp
// ToolEndpoints.cs — InvokeTool handler
if (response.PendingInvocationId.HasValue)
{
    var pollUrl = $"/invocations/{response.PendingInvocationId}/status";
    ctx.Response.Headers["Retry-After"] = "10";          // ← E6: guides client polling
    return Results.Accepted(pollUrl, new               // ← Results.Accepted sets Location header
    {
        status       = "pending_approval",
        invocationId = response.PendingInvocationId,
        pollUrl,
        message      = response.Error?.Description
    });
}
```

`Results.Accepted(location, value)` sets the `Location` response header automatically. `Retry-After: 10` (seconds) guides clients to a 10-second initial poll interval with recommended exponential back-off thereafter.

### E7 — Startup security validation (High)

Two hard-fail checks added to `Program.cs` that prevent the API from accepting traffic when misconfigured.

```csharp
// JWT key entropy — checked immediately after config binding
if (Encoding.UTF8.GetBytes(jwt.Secret).Length < 32)
    throw new InvalidOperationException(
        "Jwt:Secret must be at least 32 bytes (256 bits). " +
        "Generate a secure key: openssl rand -base64 32");

// BaseUrl HTTPS — checked after app is built, non-dev only
if (!app.Environment.IsDevelopment())
{
    var approvalOpts = app.Services
        .GetRequiredService<IOptions<ApprovalOptions>>().Value;
    if (!approvalOpts.BaseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException(
            "Approval:BaseUrl must use HTTPS in non-development environments. " +
            "Magic links sent over HTTP are vulnerable to interception.");
}
```

### E8 — OTP context propagated through approval

`AsyncApprovalGate` now calls `SaveChangesAsync` twice: once to persist the new `PendingApproval` row, and once after `channel.SendAsync` to persist any OTP hash written by `EmailOtpChannel`. This prevents a race condition where the record existed but the OTP hash was absent on the first verify attempt.

```csharp
// AsyncApprovalGate.cs — simplified
await _repo.AddAsync(pending, ct);
await _uow.SaveChangesAsync(ct);          // persist record + Id

await _channelSelector.SelectChannel(context.TenantId)
    .SendAsync(pending, context, ct);     // EmailOtpChannel writes OtpHash here

await _uow.SaveChangesAsync(ct);          // persist OtpHash
```

---

### Phase E — compliance coverage

| Item | Standard | Status |
|---|---|---|
| E1 — CSPRNG token | OWASP Secrets Management | ✅ |
| E2 — OTP lockout | OWASP MFA Cheat Sheet | ✅ |
| E3 — OTP rate limiting | OWASP Top 10 2025 A05 | ✅ |
| E4 — Auth before Validation | OWASP A01:2025 | ✅ |
| E5 — Daily budget enforcement | SOC 2 CC6 (resource controls) | ✅ |
| E6 — RFC 7231 202 headers | RFC 7231 §6.3.3 / Azure Async pattern | ✅ |
| E7 — JWT key length validation | NIST SP 800-131A (min 112-bit) | ✅ |
| E7 — BaseUrl HTTPS validation | OWASP Transport Layer Security | ✅ |
| E8 — OTP hash persistence timing | Internal reliability | ✅ |

---

---

## 17. Provider Abstractions — Phase F

Phase F makes ToolEngine production-ready: modular database/cache providers, per-request tenant caching, deny-by-default namespace policy, idempotent approvals, and at-least-once channel delivery via outbox.

### F1 — Modular database provider

The database provider is selected via `"Database:Provider"` in `appsettings.json`. No code changes are required to switch providers.

```json
{
  "Database": {
    "Provider": "sqlite"
  },
  "ConnectionStrings": {
    "Default": "Data Source=toolengine-dev.db"
  }
}
```

| Provider value | Database | Connection string key |
|---|---|---|
| `sqlite` (default) | SQLite | `ConnectionStrings:Default` |
| `postgresql` | PostgreSQL (Npgsql) | `ConnectionStrings:Default` |
| `sqlserver` | SQL Server | `ConnectionStrings:Default` |

### F2 — EF Core migrations (production)

`EnsureCreated()` is used in development only. Production uses `MigrateAsync()` at startup, which applies any pending EF Core migrations automatically.

Creating the initial migration:

```bash
dotnet ef migrations add InitialCreate \
  --project src/Infrastructure/ToolEngine.Infrastructure \
  --startup-project src/Hosts/ToolEngine.Api

dotnet ef database update \
  --startup-project src/Hosts/ToolEngine.Api
```

### F3 — Cache provider abstraction

`ICacheProvider` in `Core.Abstractions.Common` decouples the loop detection and future caching behaviors from the cache backend.

```json
{
  "Cache": {
    "Provider": "memory"
  },
  "ConnectionStrings": {
    "Redis": "localhost:6379"
  }
}
```

| Provider value | Backend | Use case |
|---|---|---|
| `memory` (default) | `IMemoryCache` | Development, single-node |
| `redis` | `IDistributedCache` / StackExchange.Redis | Production, multi-pod |

The Redis provider requires an additional registration in `Program.cs` (already included):
```csharp
builder.Services.AddStackExchangeRedisCache(opt => opt.Configuration = redisConnStr);
builder.Services.AddDistributedCacheProvider(); // from Infrastructure extension
```

### F4 — LoopDetectionBehavior → ICacheProvider

`LoopDetectionBehavior` now stores its counter in `ICacheProvider` instead of a static `ConcurrentDictionary`. The key pattern is `loop:{correlationId}:{namespace}.{name}` with a 10-minute TTL.

When `"Cache:Provider": "redis"` is configured, loop detection state is shared across all pods — a loop that spans multiple API instances is correctly detected.

### F5 — Scoped Tenant cache (CachedTenantReadRepository)

`CachedTenantReadRepository` is a scoped decorator over `ReadRepository<Tenant, string>`. It holds a `Dictionary<string, Tenant?>` per-request lifetime. The first call to `GetByIdAsync` hits the database; all subsequent calls within the same HTTP request return the cached value.

This eliminates the duplicate DB read that previously occurred across `TenantAuthorizationBehavior`, `TokenBudgetBehavior`, and `DailyBudgetBehavior` on every invocation.

### F6 — Deny-by-default namespace allowlist

Namespace policy is now **deny-by-default** (aligned with AWS SaaS prescriptive guidance):

| `AllowedNamespaces` value | Access |
|---|---|
| Empty list (new tenant default) | No namespaces permitted |
| `["*"]` | All namespaces permitted (unrestricted) |
| `["math", "weather"]` | Only `math` and `weather` namespaces |

Seed dev tenants with `AllowNamespace("*")`. Production tenants receive explicit grants:

```csharp
tenant.AllowNamespace("hr");
tenant.AllowNamespace("finance");
// tenant does NOT have access to "infrastructure" or "admin"
```

### F7 — Outbox pattern for channel notifications

`AsyncApprovalGate` no longer calls `channel.SendAsync` directly. Instead, it writes a `PendingApproval` and an `OutboxMessage` to the database **in a single `SaveChangesAsync` call** — atomically.

`NotificationDispatchService` (IHostedService) polls every 15 seconds, reads unsent `OutboxMessages`, and calls `channel.SendAsync`. Failures are retried with exponential back-off:

| Attempt | Wait |
|---|---|
| 1 | 30 seconds |
| 2 | 2 minutes |
| 3 | 8 minutes |
| 4 | 32 minutes |
| 5 | 2 hours (max) |

After 5 consecutive failures, the message is abandoned and logged. The `PendingApproval` remains in `Pending` status — an operator can manually re-trigger.

**Reliability guarantee:** If the process crashes between `SaveChangesAsync` (row committed) and the channel call, the unsent `OutboxMessage` is picked up on the next dispatch cycle. The approver receives the notification with at most one dispatch cycle of delay (≤ 15 seconds).

### F8 — Idempotency key

Clients that retry a `POST /tools/{ns}/{name}/{version}/invoke` call (e.g., on network timeout) should send an `Idempotency-Key` header. If an existing `PendingApproval` with the same key + tenant + tool is found, the gate returns the existing record instead of creating a duplicate.

```http
POST /tools/hr/update-employee/v1/invoke
Authorization: Bearer {token}
Idempotency-Key: 550e8400-e29b-41d4-a716-446655440000
Content-Type: application/json

{ "employeeId": "E12345", "department": "Engineering" }
```

On the first call: creates `PendingApproval` with the key, returns `202`.
On retry with the same key: returns `202` pointing to the same `PendingApproval`. No duplicate approval, no duplicate notification.

### F9 — Pagination on IReadRepository

`IReadRepository<TEntity, TId>` now includes `PagedListAsync`:

```csharp
var page = await approvalRepo.PagedListAsync(
    new LambdaSpecification<PendingApproval>(
        a => a.TenantId == "acme-corp" && a.Status == ApprovalStatus.Pending),
    pageNumber: 2,
    pageSize:   20,
    ct);

Console.WriteLine(page.Items.Count);    // up to 20
Console.WriteLine(page.TotalCount);     // total matching rows
Console.WriteLine(page.TotalPages);     // Math.Ceiling(total / pageSize)
Console.WriteLine(page.HasNext);        // true if more pages
Console.WriteLine(page.HasPrevious);    // true if not first page
```

---

## 18. Observability — Phase G

### G1 — OpenTelemetry tracing (W3C TraceContext)

ToolEngine emits OpenTelemetry spans via a custom `ActivitySource` named `"ToolEngine"`. Spans are automatically linked to incoming HTTP requests via the W3C `traceparent` header — every tool invocation is a child span of the HTTP request span.

Configure the OTLP exporter endpoint:

```json
{
  "Otlp": {
    "Endpoint": "http://otel-collector:4317"
  }
}
```

When `Otlp:Endpoint` is absent, tracing is still active but not exported (useful in development with local Jaeger or Zipkin).

**Custom spans emitted:**

| Span name | Tags | Description |
|---|---|---|
| `tool.execute` | tool.fullName, tool.version, tenant.id, correlation.id, invocation.status | Wraps the full tool execution pipeline |
| `tool.approval.gate` | tool.fullName, tenant.id, approval.risk, approval.decision, approval.invocationId | Human approval gate evaluation |

Both spans are child spans of the `POST /tools/{ns}/{name}/{version}/invoke` HTTP span.

**Automatic instrumentation included:**
- ASP.NET Core HTTP request spans
- Outbound HTTP spans (webhook channel calls)
- Entity Framework Core DB query spans

### G2 — Custom metrics (OTel Meter)

All metrics use the meter name `"ToolEngine"` and follow OTel semantic naming conventions.

| Metric | Type | Unit | Tags |
|---|---|---|---|
| `tool.invocation.duration` | Histogram | ms | tool.fullName, tenant.id, invocation.status |
| `tool.invocation.count` | Counter | {invocation} | tool.fullName, tenant.id, invocation.status |
| `tool.approval.pending.count` | UpDownCounter | {approval} | tenant.id, approval.risk |
| `tool.approval.wait.duration` | Histogram | ms | tenant.id, channel, risk, decision |
| `tool.loop.detection.triggers` | Counter | {trigger} | tool.fullName, tenant.id |
| `tool.daily.budget.exceeded` | Counter | {event} | tenant.id |

Dashboards (Grafana/Datadog) should query these metrics for:
- p50 / p95 / p99 invocation latency per tool
- Pending approval queue depth per tenant
- Budget exhaustion rate
- Loop detection circuit-open rate

### G3 — CorrelationId vs TraceId

`CorrelationId` in ToolEngine is the **business correlation** — identifies one logical agent turn or user action, spans multiple HTTP requests.

W3C `TraceId` (set automatically by the OTel SDK) is the **infrastructure trace** — identifies one HTTP request and its child spans.

Both coexist. `CorrelationId` is propagated via the `X-Correlation-Id` request header and as an OTel span tag `correlation.id`. In distributed scenarios, use `TraceId` to find the request in APM and `CorrelationId` to correlate all invocations in an agent workflow.

### G4 — PII masking in Serilog

All structured log string properties are passed through a Serilog destructuring policy that masks email-format strings:

```
alice@acme.com  →  al***@***.***
```

This prevents approver email addresses and webhook URLs (which may contain query tokens) from appearing in plaintext log streams shipped to third-party log aggregators (Datadog, Elastic, Splunk).

The policy is registered in `Program.cs`:
```csharp
.Destructure.ByTransforming<string>(s =>
    s.Contains('@')
        ? $"{s[..2]}***@***.***"
        : s)
```

---

## 19. Compliance — Phase H

Phase H implements four compliance obligations across SOC 2, GDPR, EU AI Act, and ISO 42001.

### H1 — Append-only ToolInvocationEvent table (SOC 2)

A separate `ToolInvocationEvent` table captures one immutable row per lifecycle transition (Invoked → Running → Succeeded / Failed / Suspended). The mutable `ToolInvocationRecord` is retained for operational queries; the event table is the authoritative SOC 2 audit trail.

**Design constraints:**
- No mutation methods on `ToolInvocationEvent` — all properties are private-set.
- The application DB user should hold INSERT permission only on this table (no UPDATE or DELETE). Enforced out-of-band in the deployment runbook §4 Database Hardening.
- `AuditBehavior` emits one event per lifecycle point, batched into the same `SaveChangesAsync` as the record mutation.

```
Invocation arrives → AuditBehavior: emit Invoked + Running events → handler runs
                   → emit Succeeded / Failed / Suspended event (with DurationMs)
```

**Entity:** `ToolEngine.Core.Domain.Entities.ToolInvocationEvent`
**Configuration:** `ToolInvocationEventConfiguration` (indexes on CorrelationId, TenantId+OccurredAt, InvocationRecordId)
**Registration:** `IRepository<ToolInvocationEvent, Guid>` + `IReadRepository<ToolInvocationEvent, Guid>` in `ServiceCollectionExtensions`

**Regulatory basis:** SOC 2 CC6.2 (logical access controls), NIST Cyber AI Profile (traceability), EU AI Act Article 17 (logging for high-risk AI systems).

---

### H2 — GDPR anonymisation + RetainUntil (ToolInvocationRecord)

Every `ToolInvocationRecord` now carries:

| Property | Type | Purpose |
|---|---|---|
| `RetainUntil` | `DateTimeOffset` | Earliest permitted deletion/anonymisation date (default: InvokedAt + 90 days) |
| `IsAnonymized` | `bool` | Set to `true` after `Anonymize()` has been called |

**`Anonymize()` method:**
```csharp
public void Anonymize()
{
    if (IsAnonymized) return;
    UserId                 = "[anonymized]";
    ErrorMessage           = null;
    GovernanceMetadataJson = null;
    IsAnonymized           = true;
}
```

The method is idempotent. A background retention sweep job queries:
```sql
WHERE RetainUntil <= @today AND IsAnonymized = false
```
and calls `Anonymize()` on each record. The event table is exempt from anonymisation — it stores no user PII beyond `UserId`, which is needed for legal accountability (GDPR Recital 26).

**Regulatory basis:** GDPR Article 17 (right to erasure), Article 5(1)(e) (storage limitation).

---

### H3 — EU AI Act Article 14 Acknowledgement (PendingApproval)

For `High` and `Critical` risk tools, `AsyncApprovalGate` generates and persists an `AcknowledgementStatement` JSON blob on the `PendingApproval` record before dispatching the notification. This documents that the approving operator was informed of the risk classification and regulatory basis at the time of the approval request.

**`AcknowledgementStatement` record:**
```csharp
public sealed record AcknowledgementStatement(
    string         RegulatoryBasis,    // "EU AI Act Article 14 §4 — Human Oversight"
    ApprovalRisk   RiskLevel,          // High / Critical
    string         ToolFullName,       // "payments.charge-card"
    string         OperatorStatement,  // Human-readable oversight declaration
    DateTimeOffset IssuedAt);          // UTC timestamp
```

The JSON is stored in `PendingApproval.AcknowledgementJson` and survives the lifecycle of the approval record. It is not subject to GDPR anonymisation.

**Gate logic:**
```csharp
if (risk >= ApprovalRisk.High)
{
    var ack = new AcknowledgementStatement(
        RegulatoryBasis:   "EU AI Act Article 14 §4 — Human Oversight",
        RiskLevel:         risk,
        ToolFullName:      context.ToolFullName,
        OperatorStatement: "...",
        IssuedAt:          DateTimeOffset.UtcNow);
    pending.SetAcknowledgement(JsonSerializer.Serialize(ack));
}
```

**Regulatory basis:** EU AI Act Article 14 §4 (human oversight measures for high-risk AI systems).

---

### H4 — Agent identity claims (`CallerType`)

A new `CallerType` enum distinguishes invocation origins:

```csharp
public enum CallerType
{
    Human         = 0,   // human via UI or direct API
    AiAgent       = 1,   // autonomous AI agent
    SystemService = 2    // scheduler / background job
}
```

**Flow:**
1. JWT includes claim `caller_type` with value `"human"`, `"ai_agent"`, or `"system_service"`.
2. `ToolEndpoints.InvokeTool` maps the claim to `CallerType` and passes it on `ExecuteToolCommand`.
3. `AuditBehavior` writes `CallerType` to both `ToolInvocationRecord` and every `ToolInvocationEvent`.

This enables dashboards and audit exports to filter AI-generated actions from human-generated ones — a prerequisite for demonstrating human oversight under EU AI Act Article 14.

**Configuration:**
- JWT issuer must include `caller_type` claim in the token payload.
- Default (claim absent or unrecognised): `CallerType.Human`.

---

### H5 — ISO 42001 AI Governance Metadata

Callers may supply an optional `X-Governance-Metadata` header containing a JSON blob that describes the AI model, agent id, deployment context, or any ISO 42001-required governance attributes.

```
X-Governance-Metadata: {"model":"claude-opus-4","agentId":"acme-billing-agent","policyVersion":"2026.1"}
```

The value is stored verbatim in:
- `ToolInvocationRecord.GovernanceMetadataJson`
- `ToolInvocationEvent.GovernanceMetadataJson`

No schema is enforced — the platform is a neutral carrier. Downstream compliance tooling (SIEM, CSPM) parses and validates the blob against the organisation's ISO 42001 control set.

**Anonymisation note:** `GovernanceMetadataJson` is nulled by `ToolInvocationRecord.Anonymize()` after `RetainUntil` passes, as it may contain agent identifiers that qualify as personal data.

---

### H — File inventory

| File | Change |
|---|---|
| `Core.Domain/Enums/CallerType.cs` | New — H4 |
| `Core.Domain/Enums/InvocationEventType.cs` | New — H1 |
| `Core.Domain/Entities/ToolInvocationEvent.cs` | New — H1, H4, H5 |
| `Core.Domain/Contracts/AcknowledgementStatement.cs` | New — H3 |
| `Core.Domain/Entities/ToolInvocationRecord.cs` | RetainUntil, IsAnonymized, Anonymize(), CallerType, GovernanceMetadataJson — H2, H4, H5 |
| `Core.Domain/Entities/PendingApproval.cs` | AcknowledgementJson, SetAcknowledgement() — H3 |
| `Application/Abstractions/IExecuteToolCommand.cs` | CallerType, GovernanceMetadataJson — H4, H5 |
| `Application/Commands/ExecuteToolCommand.cs` | CallerType, GovernanceMetadataJson params — H4, H5 |
| `Application/Behaviors/AuditBehavior.cs` | EmitEventAsync(), IRepository<ToolInvocationEvent> dep, CallerType, GovernanceMetadataJson propagation — H1, H4, H5 |
| `Infrastructure/Persistence/AppDbContext.cs` | DbSet<ToolInvocationEvent> — H1 |
| `Infrastructure/Persistence/Configurations/ToolInvocationEventConfiguration.cs` | New — H1 |
| `Infrastructure/Persistence/Configurations/ToolInvocationRecordConfiguration.cs` | CallerType, RetainUntil, IsAnonymized, GovernanceMetadataJson columns — H2, H4, H5 |
| `Infrastructure/Persistence/Configurations/PendingApprovalConfiguration.cs` | AcknowledgementJson column — H3 |
| `Infrastructure/Approval/AsyncApprovalGate.cs` | AcknowledgementStatement generation for High/Critical — H3 |
| `Infrastructure/Extensions/ServiceCollectionExtensions.cs` | Register IRepository/IReadRepository<ToolInvocationEvent> — H1 |
| `Hosts/Api/Endpoints/ToolEndpoints.cs` | Read caller_type claim, X-Governance-Metadata header — H4, H5 |

---

---

## 20. LLM Agent Layer — Phase L

Phase L adds a multi-provider LLM orchestration layer. Callers supply free-text; the LLM selects and invokes the correct tool automatically. All tool governance (auth, budget, approval, audit) applies unchanged — the LLM is an orchestrator, not a bypass.

### 20.1 Architecture

```
Text input (CLI ask / POST /agent/chat)
        │
        ▼
┌─────────────────────────────┐
│  AgentChatCommand           │  MediatR command — text + optional sessionId
└────────────┬────────────────┘
             │
        ┌────▼──────────────────────────────┐
        │  AgentOrchestrator                │  Core agentic loop (MaxIterations circuit breaker)
        │  ┌────────────────────────────┐   │
        │  │ ILlmProvider               │   │  Anthropic / OpenAI / Ollama
        │  │ CompleteAsync(...)         │   │  (selected by IProviderRouter)
        │  └────────────────────────────┘   │
        │  ┌────────────────────────────┐   │
        │  │ ToolSchemaConverter        │   │  ToolEngine schema → provider format
        │  └────────────────────────────┘   │
        │  ┌────────────────────────────┐   │
        │  │ AgentSessionStore          │   │  Message history + token accumulator
        │  └────────────────────────────┘   │  (backed by ICacheProvider)
        └────────────┬──────────────────────┘
                     │ tool_use decision
                     ▼
        ┌────────────────────────────────┐
        │  MediatR Pipeline              │  UNCHANGED — all 8 behaviors apply
        │  (TenantAuth → Approval        │  CallerType = AiAgent (non-negotiable)
        │   → Audit → Budget …)          │  GovernanceMetadataJson = {provider, model, sessionId}
        └────────────┬───────────────────┘
                     │ ToolResponse
                     ▼
        Result fed back to LLM → natural-language reply returned to caller
```

### 20.2 New project: `ToolEngine.Llm`

```
src/Llm/ToolEngine.Llm/
├── Abstractions/
│   ├── ILlmProvider.cs           CompleteAsync(messages, tools, options, ct) → LlmResponse
│   ├── IAgentSession.cs          Messages, TokensUsed, AddMessage(), RecordUsage()
│   └── IProviderRouter.cs        Select(tenantOverride?, toolDescriptor?) → (ILlmProvider, ProviderOptions)
├── Models/
│   ├── LlmMessage.cs             Role (User|Assistant|Tool), Content, ToolCall, ToolCallId
│   ├── LlmToolDefinition.cs      SanitizedName, OriginalFullName, Description, InputSchemaJson
│   ├── LlmToolCall.cs            Id, ToolName (sanitized), Arguments (JsonElement)
│   ├── LlmResponse.cs            Content?, ToolCall?, Usage, StopReason, ErrorMessage?
│   ├── LlmUsage.cs               InputTokens, OutputTokens, TotalTokens, EstimatedCostUsd
│   └── StopReason.cs             EndTurn | ToolUse | MaxTokens | Error
├── Providers/
│   ├── AnthropicLlmProvider.cs   Raw HTTP POST to /v1/messages — official wire format
│   ├── OpenAiLlmProvider.cs      Raw HTTP POST to /v1/chat/completions
│   └── OllamaLlmProvider.cs      Extends OpenAiLlmProvider — overrides URL + no auth bearer
├── Routing/
│   └── ProviderRouter.cs         Precedence: [LlmProvider] attr > tenant override > config default
├── Session/
│   ├── AgentSession.cs           In-memory message list + token accumulator
│   ├── AgentSessionStore.cs      ICacheProvider-backed; JSON DTO serialization
│   └── AgentOrchestrator.cs      Core agentic loop
├── Conversion/
│   └── ToolSchemaConverter.cs    ToolDescriptor → LlmToolDefinition; WhenToUse embedded
├── Attributes/
│   └── LlmProviderAttribute.cs   [LlmProvider("ollama")] on tool class — overrides routing
├── Options/
│   ├── LlmOptions.cs             Root "Llm" config
│   ├── ProviderOptions.cs        Model, ApiKeyEnvVar, BaseUrl, MaxTokens, TimeoutSeconds
│   ├── RoutingOptions.cs         DefaultProvider, FallbackChain
│   └── BudgetOptions.cs          MaxTokensPerRequest, MaxTokensPerSession, MaxIterations
└── Extensions/
    └── ServiceCollectionExtensions.cs   AddToolLlm(services, configuration)
```

### 20.3 Providers

#### Anthropic (`claude-opus-4-5` default)

POST `https://api.anthropic.com/v1/messages` with headers `x-api-key` and `anthropic-version: 2023-06-01`.

Tool results are sent as `role: user` with `type: tool_result` content blocks (Anthropic wire format — differs from OpenAI).

#### OpenAI (`gpt-4o` default)

POST `https://api.openai.com/v1/chat/completions` with `Authorization: Bearer {key}`.

#### Ollama (local, zero cost)

Extends `OpenAiLlmProvider`. Overrides base URL to `http://localhost:11434/v1/chat/completions` and omits the `Authorization` header. The Ollama server accepts the OpenAI-compatible `/v1/chat/completions` format natively.

Recommended as the CLI default (`DefaultProvider: ollama`) — no API cost for developer use.

### 20.4 Tool name sanitization

Anthropic and OpenAI reject function names containing dots. `ToolSchemaConverter` sanitizes names at conversion time:

```
math.calculate  →  math__calculate   (dots replaced with double underscore)
```

`AgentOrchestrator` maintains a lookup table (`sanitized → original`) per request. When the LLM returns `math__calculate`, it is desanitized back to `math.calculate` before the MediatR command is built.

### 20.5 Agentic loop

```csharp
// Simplified — see AgentOrchestrator.cs for full implementation
for (int i = 0; i < budget.MaxIterations; i++)
{
    if (session.TokensUsed >= budget.MaxTokensPerSession)
        return AgentResult.BudgetExceeded(session.SessionId, session.TotalUsage);

    var response = await provider.CompleteAsync(session.Messages, toolDefs, provOpts, ct);
    session.RecordUsage(response.Usage);

    if (response.StopReason == StopReason.EndTurn)
        return AgentResult.Ok(response.Content!, session.SessionId, ...);

    if (response.StopReason == StopReason.ToolUse)
    {
        // CallerType = AiAgent — NON-NEGOTIABLE, never overridable by the caller
        var executeCmd = new ExecuteToolCommand<JsonElement, JsonElement>(
            correlationId, tenantId, userId, toolName, "latest", args,
            ToolType:               ToolType.Logic,
            ToolNamespace:          ns,
            CallerType:             CallerType.AiAgent,
            GovernanceMetadataJson: JsonSerializer.Serialize(new { provider, model, sessionId }));

        var toolResponse = await mediator.Send(executeCmd, ct); // full 8-behavior pipeline
        session.AddMessage(LlmMessage.ToolResult(toolCallId, resultJson));
        // loop continues — LLM summarizes the result
    }
}
return AgentResult.MaxIterations(session.SessionId, session.TotalUsage);
```

**Circuit breakers:**

| Guard | Mechanism |
|---|---|
| Per-iteration token check | `session.TokensUsed >= budget.MaxTokensPerSession` before each LLM call |
| Iteration limit | `MaxIterations = 10` — prevents O(n²) token explosion in runaway loops |
| Tool approval gate | High/Critical tools suspend the loop, return `ToolPending` result |
| Tenant daily budget | Existing `DailyBudgetBehavior` — LLM-initiated calls count against the cap |

### 20.6 Session management

Single-turn `ask` commands use a transient session (not persisted after the call).

Multi-turn `chat` sessions are stored in `ICacheProvider` keyed by `agent:session:{sessionId}`. The session record includes: message history, cumulative token count, and creation timestamp.

When `Cache:Provider = redis`, sessions survive pod restarts and are accessible across all API instances. With `memory` (dev default), sessions are in-process only.

### 20.7 API endpoints

#### Single-turn and multi-turn chat

```http
POST /agent/chat
Authorization: Bearer {token}
X-Llm-Provider: ollama      (optional — overrides routing for this request)
Content-Type: application/json

{
  "text":      "What is 6 times 7?",
  "sessionId": "optional-uuid-for-multi-turn"
}
```

**200 Success**
```json
{
  "success":      true,
  "reply":        "6 times 7 is 42.",
  "toolInvoked":  "math.calculate",
  "toolResult":   { "result": 42 },
  "sessionId":    "f7e6d5c4-...",
  "usage": {
    "inputTokens":       88,
    "outputTokens":      54,
    "totalTokens":       142,
    "estimatedCostUsd":  0.0004
  }
}
```

**202 Pending approval** (tool required human approval)
```json
{
  "success":              false,
  "pendingInvocationId":  "9c112b4a-...",
  "sessionId":            "f7e6d5c4-..."
}
```

**429 Budget exceeded**
```json
{
  "type":   "https://tools.ietf.org/html/rfc7231#section-6.5.29",
  "title":  "BUDGET_EXCEEDED",
  "status": 429,
  "detail": "Session token budget of 32768 exceeded."
}
```

#### Streaming chat

```http
POST /agent/chat/stream
```

Response: `Content-Type: text/event-stream`

```
data: {"event":"status","message":"Thinking..."}

data: {"event":"tool_selected","tool":"math.calculate"}

data: {"event":"tool_result","result":{"result":42}}

data: {"event":"reply","text":"6 times 7 is 42.","sessionId":"...","usage":{...}}
```

### 20.8 Security controls

| Control | Implementation |
|---|---|
| API keys never in config | Read from environment variable named by `ProviderOptions.ApiKeyEnvVar` |
| LLM sees only permitted tools | `_registry.ListAll(tenantId)` — tenant namespace allowlist applied |
| All tool calls traverse MediatR | `AgentOrchestrator` calls `mediator.Send(executeCmd)` — no bypass |
| `CallerType = AiAgent` always | Set in `BuildExecuteToolCommand` — not a parameter exposed to callers |
| Approval gate intact | High/Critical tools suspend the loop — `AgentResult.ToolPending` returned |
| Prompt injection resistance | System prompt is code-generated; user text is isolated as `role: user` |
| Session token circuit breaker | Checked before every LLM call, not after |
| Iteration circuit breaker | `MaxIterations = 10` default |
| GovernanceMetadataJson | Records `{ provider, model, sessionId }` on every `ToolInvocationRecord` (H5) |

### 20.9 Per-tool LLM routing override

Annotate a tool handler class with `[LlmProvider]` to route all LLM selections of that tool through a specific provider:

```csharp
// This tool's schema is always submitted to the Ollama provider,
// regardless of tenant config or global default.
[LlmProvider("ollama")]
public sealed class SensitiveAnalysisHandler : LogicToolBase<SensitiveInput, SensitiveOutput>
{
    // ...
}
```

Routing precedence: `[LlmProvider]` attribute → `Tenant.LlmProviderOverride` → `Llm:DefaultProvider`.

### 20.10 Phase L — file inventory

| File | Description |
|---|---|
| `src/Llm/ToolEngine.Llm/` (new project) | All LLM abstractions, providers, orchestrator |
| `src/Application/.../Commands/AgentChatCommand.cs` | New MediatR command |
| `src/Application/.../Handlers/AgentChatHandler.cs` | Handler — delegates to `AgentOrchestrator` |
| `src/Hosts/ToolEngine.Api/Endpoints/AgentEndpoints.cs` | `/agent/chat` and `/agent/chat/stream` |
| `src/Hosts/ToolEngine.Api/appsettings.json` | Added `"Llm"` section |
| `src/Hosts/ToolEngine.Cli/Repl/ReplLoop.cs` | Added `ask`, `chat`, `chat end` REPL commands |
| `src/Hosts/ToolEngine.Cli/appsettings.json` | Added `"Llm"` section (`DefaultProvider: ollama`) |
| `tests/ToolEngine.Llm.Tests/` (new project) | 30 unit tests — orchestrator, router, converter, session |
| `tests/ToolEngine.Integration.Tests/Agent/AgentChatTests.cs` | 4 integration tests — CallerType, GovernanceMetadataJson, pipeline end-to-end |

---

*Confidential — Internal Use Only | ONE BCG | ToolEngine v2026*

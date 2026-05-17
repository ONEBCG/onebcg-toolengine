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
                       ↘  Core.Domain
Infrastructure         →  Core.Abstractions
                       →  Core.Domain
                       →  Tools.Abstractions
```

Rules enforced by project references:
- `Core.Abstractions` has no NuGet or project dependencies.
- `Core.Domain` depends only on `Core.Abstractions`.
- `Infrastructure` does not depend on Application. The composition root (host) wires them.
- `Tools.Abstractions` does not reference Infrastructure.

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
| `ApprovalToken` | Opaque URL-safe token included in magic-link emails and webhook payloads |
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

// Cap LLM token usage
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

```
Request
  │
  ▼
┌─────────────────────────────────────────┐
│  1. ValidationBehavior                  │  FluentValidation — rejects malformed input
│                                         │  before any DB access or tool resolution.
├─────────────────────────────────────────┤
│  2. TenantAuthorizationBehavior         │  Loads Tenant from DB. Rejects if inactive
│                                         │  or namespace not in AllowedNamespaces.
├─────────────────────────────────────────┤
│  3. TokenBudgetBehavior                 │  Compares MaxResponseTokens against tenant cap.
│                                         │  Rejects if exceeded.
├─────────────────────────────────────────┤
│  4. LoopDetectionBehavior               │  Counts invocations per (correlationId, tool).
│                                         │  Circuit-opens after threshold (default 10).
├─────────────────────────────────────────┤
│  5. ApprovalBehavior                    │  Reads [RequiresApproval] via IToolDiscovery.
│                                         │  Routes to IHumanApprovalGate. Suspends,
│                                         │  denies, or passes through.
├─────────────────────────────────────────┤
│  6. AuditBehavior                       │  Creates ToolInvocationRecord before handler.
│                                         │  Marks succeeded or failed after.
└─────────────────────────────────────────┘
  │
  ▼
Handler (ExecuteToolCommandHandler)
```

### 7.3 Short-circuit response codes

| Behavior | HTTP | Error code |
|---|---|---|
| ValidationBehavior | 400 | `VALIDATION_ERROR` |
| TenantAuthorizationBehavior (not found) | 401 | `UNAUTHORIZED` |
| TenantAuthorizationBehavior (inactive / namespace blocked) | 403 | `UNAUTHORIZED` |
| TokenBudgetBehavior | 400 | `TOKEN_BUDGET_EXCEEDED` |
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

The `token` is an opaque `Guid.NewGuid().ToString("N")` — 32-character hex string. It acts as the shared secret; no JWT is required on the decide endpoint.

#### EmailOtpChannel
For `Critical` risk tools only. Sends a 6-digit OTP to `ApproverEmail`. The OTP is:
1. Generated using `RandomNumberGenerator` (cryptographically secure).
2. SHA-256 hashed before storage — never stored in plaintext.
3. Verified by `POST /approvals/otp/verify` — hash of submitted OTP is compared to stored hash.

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
toolengine> help
  invoke <namespace> <name> <version> <json>   Execute a tool
  list                                          List registered tools
  search <intent>                               Semantic tool search
  exit                                          Exit the REPL
```

### 11.2 Invoking a tool

```
toolengine> invoke math calculate v1 {"a":10,"b":5}
{
  "correlationId": "...",
  "success": true,
  "data": { "result": 15 },
  "metrics": { "durationMs": 3 }
}
```

### 11.3 Approval prompt (CLI)

When a `[RequiresApproval]` tool is invoked in the CLI, `ConsoleApprovalGate` halts execution and presents a coloured prompt:

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

### Environment-specific overrides

```bash
# Override via environment variable (recommended for production secrets)
APPROVAL__BASEURL="https://app.onebcg.com"
APPROVAL__WEBHOOKURL="https://hooks.slack.com/services/..."
JWT__SECRET="$(cat /run/secrets/jwt_secret)"
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

*Confidential — Internal Use Only | ONE BCG | ToolEngine v2026*

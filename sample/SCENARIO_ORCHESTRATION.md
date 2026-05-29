# Scenario Orchestration Layer

**ONE BCG ToolEngine v2026 POC** — Technical Reference

---

## Overview

The Scenario Orchestration Layer sits between the HTTP surface and the tool registry. It enables named, versioned, multi-step pipelines where each step is a registered tool, steps declare dependencies, and outputs from one step automatically feed into subsequent steps.

**Key guarantee:** every step in every scenario passes through the full MediatR behavior pipeline — validation, loop detection, approval gate, and audit all apply consistently.

---

## Component Map

```
IScenarioDefinition      ← Declares a named pipeline as a ToolPlan factory
IRequiresSetup           ← Optional hook: create domain entities before the plan runs
IScenarioRegistry        ← Singleton — register and look up scenario definitions
ScenarioRunner           ← Application entry point for RunAsync / ResumeAsync
IToolPlanOrchestrator    ← Executes each step through MediatR (governed path)
StepContext              ← In-memory + serialisable output store for inter-step data
ScenarioExecution        ← EF Core entity — durable state for suspend/resume
ScenarioRunResult        ← Result type returned to the HTTP layer
```

---

## Execution Flow

```
ScenarioRunner.RunAsync(name, version, input, userId, callerType)
  │
  ├─ IScenarioRegistry.Resolve(name, version)
  │
  ├─ IRequiresSetup.SetupAsync(input, services, ct)     [if implemented]
  │   May create DB entities and inject IDs into the input.
  │   Example: PaymentComplianceScenario creates a PaymentInstruction
  │            and injects the generated PRID into the input JsonElement.
  │
  ├─ IScenarioDefinition.Build(input) → ToolPlan
  │   Pure function — no I/O, no side effects.
  │   Constructs steps with static inputs and output mapping declarations.
  │
  ├─ ScenarioExecution.Start() → DB   [durability checkpoint]
  │
  └─ IToolPlanOrchestrator.ExecuteAsync(plan, userId, callerType)
        │
        For each step (topologically ordered for DAG; in-order for Sequential):
        │
        ├─ StepContext.ResolveInput(step.Input, step.OutputMappings)
        │   Merges static step input with mapped values from the context.
        │
        ├─ ISender.Send(ExecuteToolCommand)   ← full MediatR pipeline
        │   Validation → LoopDetection → Approval → Audit → Handler
        │
        ├─ StepContext.SetStepOutput(stepId, data)
        │   Stores this step's output for downstream steps to consume.
        │
        └─ If IsSuspended:
               Return OrchestratorResult.Suspended(approvalId, stepId, context)
               ScenarioExecution.Suspend(stepId, approvalId, context.Serialise())
               HTTP 202 → caller must approve then call /resume
```

---

## Inter-Step Output Mapping

### Concept

Each `ToolStep` carries an `OutputMappings` dictionary:

```
{ "targetField": "sourceStepId.fieldPath" }
```

**Before a step executes**, `StepContext.ResolveInput` merges the step's static input JSON with values resolved from prior step outputs, using dot-notation field paths.

### Concrete example

`payment.compliance-check` chains 4 steps. Step 1 (verify-payee) produces:
```json
{ "payeeId": "11111111-...", "legalName": "Acme Ltd", "jurisdiction": "GB", "entityType": "Corporate" }
```

Step 2 (ppm-check) declares:
```csharp
OutputMappings: new Dictionary<string, string>
{
    ["verifiedPayeeId"] = "step-1-verify-payee.payeeId"
}
```

At runtime, before step 2 executes:
1. The resolver splits `"step-1-verify-payee.payeeId"` → stepId = `step-1-verify-payee`, field = `payeeId`
2. Looks up `step-1-verify-payee` in `StepContext._outputs`
3. Extracts `payeeId` → `"11111111-..."`
4. Overrides `verifiedPayeeId` in step 2's merged input

Step 4 (kyc-screen) demonstrates mapping **multiple fields** from a single prior step:
```csharp
OutputMappings: new Dictionary<string, string>
{
    ["payeeId"]           = "step-1-verify-payee.payeeId",
    ["payeeLegalName"]    = "step-1-verify-payee.legalName",
    ["payeeJurisdiction"] = "step-1-verify-payee.jurisdiction",
    ["entityType"]        = "step-1-verify-payee.entityType",
}
```

### Nested field paths

Dot notation supports deeply nested fields:
```
"step-1.payee.bankDetails.iban"  →  walks: payee → bankDetails → iban
```

---

## Durable Execution — Suspend and Resume

When a step triggers the approval gate (`[RequiresApproval]`):

**Suspension:**
1. MediatR pipeline returns `ToolResponse.Suspended(pendingInvocationId)`
2. `ToolPlanOrchestrator` captures: `{ pendingApprovalId, suspendedAtStepId, context }`
3. `ScenarioRunner` calls `execution.Suspend(stepId, approvalId, context.Serialise())`
4. `StepContextJson` (all prior step outputs as JSON) written to `ScenarioExecution` in DB
5. API returns HTTP 202 with `{ executionId, pendingApprovalId, resumeUrl }`

**Resume** (`POST /api/v1/scenarios/{executionId}/resume`):
1. Load `ScenarioExecution` from DB
2. `StepContext.Deserialise(execution.StepContextJson)` — restores all prior outputs
3. Rebuild `ToolPlan` from `execution.InputJson` via `IScenarioDefinition.Build()`
4. `ToolPlanOrchestrator.ExecuteAsync(plan, ..., resumeFromStepId: execution.SuspendedAtStepId)`
5. Orchestrator starts at `suspendedStepIndex + 1` — the suspended step already completed its work (e.g., submitted the approval request)
6. Execution continues; `ScenarioExecution` updated with final status

This means scenarios survive process restarts — the full execution state is checkpointed to the database.

---

## IRequiresSetup — Pre-Execution Hook

Payment scenarios need a `PaymentInstruction` entity to exist in the database before their tool handlers run. `IRequiresSetup` handles this:

```csharp
public sealed class PaymentComplianceScenario : IScenarioDefinition, IRequiresSetup
{
    public async Task<JsonElement> SetupAsync(
        JsonElement input, IServiceProvider services, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var instruction = PaymentInstruction.Create(/* fields from input */);
        db.Set<PaymentInstruction>().Add(instruction);
        await db.SaveChangesAsync(ct);

        // Return input with prid injected — Build() and all step handlers use it
        var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(input)!;
        dict["prid"] = JsonSerializer.SerializeToElement(instruction.Id);
        return JsonSerializer.SerializeToElement(dict);
    }
}
```

`ScenarioRunner` calls `SetupAsync` before `Build()`. If setup throws, the scenario returns `ScenarioRunResult.SetupFailed(message)` without creating a `ScenarioExecution` record.

---

## Registered Payment Scenarios

| Name | Implements | Steps | Result if input is valid |
|------|-----------|-------|--------------------------|
| `payment.compliance-check` | `IRequiresSetup` | 1→2→3→4 | All 4 stages pass. Demonstrates full output mapping chain. |
| `payment.expired-ppm` | `IRequiresSetup` | 1→2 | Stage 1 passes. Stage 2 blocks — `CONTRACT_INACTIVE`. |
| `payment.kyc-block` | `IRequiresSetup` | 1→2→3→4 | Stages 1–3 pass. Stage 4 blocks — `KYC_CONFIRMED_MATCH`. |
| `payment.over-limit` | `IRequiresSetup` | 1→2 | Stage 1 passes. Stage 2 blocks — `CONTRACT_AMOUNT_EXCEEDED`. |

All four use hardcoded payee/PPM combinations matching the seeded test data.

---

## Adding a New Scenario

See the main README — [Adding a New Scenario](../code/README.md) section.

Key rules:
- `Name` must be globally unique (e.g., `procurement.validate-supplier`)
- `Build()` must be a pure function — no I/O, no side effects
- Use `IRequiresSetup` when the scenario needs domain entities created before tool handlers run
- Register the scenario class as `Transient` inside `AddPaymentModule()`, then call `registry.Register(...)` inside `RegisterPaymentScenarios(provider, registry)`
- The top-level entry point for both tool descriptor and scenario registration is `RegisterPaymentModuleAsync(app.Services, scenarioRegistry)`, called from `Program.cs` after `app.Build()`
- There is no auto-discovery; every new scenario must be explicitly added to both `AddPaymentModule()` and `RegisterPaymentScenarios()`

---

## Future Direction — JSON-Defined Scenarios

The current implementation uses C# classes. A natural evolution is JSON-defined scenarios stored as files, enabling non-engineers to author workflows:

```json
{
  "name": "payment.custom-check",
  "version": "v1",
  "plan": {
    "mode": 0,
    "steps": [
      {
        "stepId": "step-1",
        "namespace": "payment",
        "toolName": "verify-payee",
        "inputTemplate": { "paymentId": "$input.prid", "payeeRef": "$input.payeeRef" },
        "dependsOn": [],
        "outputMappings": {}
      }
    ]
  }
}
```

Key addition needed: pre-seed `StepContext` with `"$input"` containing the scenario input before execution begins. Then `"$input.payeeRef"` in `inputTemplate` resolves using the **same** `StepContext.ResolveInput` mechanism as `outputMappings` — no separate template engine required.

---

*ONE BCG ToolEngine v2026 — Scenario Orchestration Layer*
*Confidential – Internal Use Only*

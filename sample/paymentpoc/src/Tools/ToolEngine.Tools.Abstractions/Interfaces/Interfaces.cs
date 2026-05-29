using System.Text.Json;
using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Core.Domain.Enums;
using ToolEngine.Tools.Abstractions.Models;

namespace ToolEngine.Tools.Abstractions.Interfaces;

// ── IToolHandler<TInput,TOutput> ─────────────────────────────────────────────

public interface IToolHandler<TInput, TOutput>
{
    Task<ToolResponse<TOutput>> ExecuteAsync(
        ToolRequest<TInput> request, CancellationToken ct = default);
}

// ── IToolExecutor ────────────────────────────────────────────────────────────

public interface IToolExecutor
{
    Task<ToolResponse<TOutput>> ExecuteAsync<TInput, TOutput>(
        ToolRequest<TInput> request, CancellationToken ct = default);
}

// ── IToolPlanExecutor ────────────────────────────────────────────────────────

public interface IToolPlanExecutor
{
    Task<IReadOnlyList<ToolPlanResult>> ExecuteAsync(
        ToolPlan plan, CancellationToken ct = default);
}

// ── IHumanApprovalGate ───────────────────────────────────────────────────────
// Two implementations: AsyncApprovalGate (API) and ConsoleApprovalGate (CLI)

public interface IHumanApprovalGate
{
    Task<ApprovalDecision> RequestApprovalAsync(
        ApprovalContext context, CancellationToken ct = default);
}

// ── IToolRegistry ────────────────────────────────────────────────────────────

public interface IToolRegistry
{
    void Register(ToolDescriptor descriptor);
    Result<ToolDescriptor> Resolve(string fullName, string version);
    IReadOnlyList<ToolDescriptor> ListAll();
    IReadOnlyList<ToolDescriptor> ListTools();
}

// ── IToolDiscovery ───────────────────────────────────────────────────────────

public interface IToolDiscovery
{
    Task<IReadOnlyList<ToolDescriptor>> DiscoverAsync(CancellationToken ct = default);
}

// ── IToolPlanOrchestrator ────────────────────────────────────────────────────
// Governed plan executor: routes every step through the MediatR behavior pipeline
// (Validation → LoopDetection → Approval → Audit).
// Use instead of IToolPlanExecutor when governance (audit, approval) is required.

public interface IToolPlanOrchestrator
{
    Task<OrchestratorResult> ExecuteAsync(
        ToolPlan      plan,
        string?       userId,
        CallerType    callerType,
        StepContext?  resumeContext     = null,
        string?       resumeFromStepId = null,
        CancellationToken ct           = default);
}

// ── IScenarioDefinition ──────────────────────────────────────────────────────
// Declares a named, versioned workflow as a ToolPlan factory.
// Scenarios are discovered at startup via assembly scan and registered in
// IScenarioRegistry. Build() produces a ToolPlan from the caller-supplied input;
// it does NOT execute — execution is delegated to IToolPlanOrchestrator.

public interface IScenarioDefinition
{
    string      Name        { get; }
    string      Version     { get; }
    string      Description { get; }
    JsonElement InputSchema  { get; }
    ToolPlan    Build(JsonElement input);
}

// ── IScenarioRegistry ────────────────────────────────────────────────────────

public interface IScenarioRegistry
{
    void                             Register(IScenarioDefinition scenario);
    IScenarioDefinition?             Resolve(string name, string version = "v1");
    IReadOnlyList<IScenarioDefinition> ListAll();
}

// ── IRequiresSetup ───────────────────────────────────────────────────────────
// Optional interface for scenario definitions that must perform infrastructure
// setup before the plan executes (e.g., creating a domain entity and injecting
// its ID into the scenario input).
// ScenarioRunner detects this interface and calls SetupAsync before Build().

public interface IRequiresSetup
{
    /// <summary>
    /// Perform pre-execution setup. May modify and return the input
    /// (e.g., inject a generated PRID). Called before Build(input).
    /// </summary>
    Task<JsonElement> SetupAsync(
        JsonElement       input,
        IServiceProvider  services,
        CancellationToken ct);
}

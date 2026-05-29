using System.Text.Json;
using ToolEngine.Core.Domain.Common;
using ToolEngine.Core.Domain.Enums;

namespace ToolEngine.Core.Domain.Entities;

/// <summary>
/// Persisted record of a scenario execution. Enables durable orchestration:
/// the ScenarioRunner checkpoints after every step, so executions survive
/// process restarts and approval gate suspensions.
/// </summary>
public sealed class ScenarioExecution : Entity<Guid>
{
    public string         ScenarioName      { get; private set; } = default!;
    public string         ScenarioVersion   { get; private set; } = default!;
    public ScenarioStatus Status            { get; private set; }
    public string?        SuspendedAtStepId { get; private set; }
    public Guid?          PendingApprovalId { get; private set; }
    public string?        FailedAtStepId    { get; private set; }
    public string?        FailureReason     { get; private set; }

    // JSON-serialised StepContext — rehydrated on resume to restore prior step outputs
    public string         StepContextJson   { get; private set; } = "{}";

    // Original scenario input — used to rebuild the ToolPlan on resume
    public string         InputJson         { get; private set; } = "{}";

    public string?        InitiatedBy       { get; private set; }
    public DateTimeOffset? CompletedAt      { get; private set; }

    private ScenarioExecution() { }

    // ── Factory ───────────────────────────────────────────────────────────────

    public static ScenarioExecution Start(
        string      scenarioName,
        string      scenarioVersion,
        JsonElement input,
        string?     initiatedBy)
    {
        var now = DateTimeOffset.UtcNow;
        return new ScenarioExecution
        {
            Id              = Guid.NewGuid(),
            ScenarioName    = scenarioName,
            ScenarioVersion = scenarioVersion,
            Status          = ScenarioStatus.Running,
            InputJson       = JsonSerializer.Serialize(input),
            InitiatedBy     = initiatedBy,
            CreatedAt       = now,
            UpdatedAt       = now,
        };
    }

    // ── State transitions ─────────────────────────────────────────────────────

    public void Complete(string contextJson)
    {
        Status          = ScenarioStatus.Completed;
        StepContextJson = contextJson;
        CompletedAt     = DateTimeOffset.UtcNow;
        UpdatedAt       = DateTimeOffset.UtcNow;
    }

    public void Suspend(string stepId, Guid approvalId, string contextJson)
    {
        Status            = ScenarioStatus.Suspended;
        SuspendedAtStepId = stepId;
        PendingApprovalId = approvalId;
        StepContextJson   = contextJson;
        UpdatedAt         = DateTimeOffset.UtcNow;
    }

    public void Resume()
    {
        Status    = ScenarioStatus.Running;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Fail(string? stepId, string? reason)
    {
        Status         = ScenarioStatus.Failed;
        FailedAtStepId = stepId;
        FailureReason  = reason;
        UpdatedAt      = DateTimeOffset.UtcNow;
    }
}

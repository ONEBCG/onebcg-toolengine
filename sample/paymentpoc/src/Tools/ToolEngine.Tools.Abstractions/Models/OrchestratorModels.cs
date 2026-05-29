using System.Text.Json;
using ToolEngine.Core.Domain.Contracts;

namespace ToolEngine.Tools.Abstractions.Models;

// ── StepContext ───────────────────────────────────────────────────────────────
// Carries step outputs across a plan execution. Each step writes its output
// (keyed by StepId); subsequent steps resolve their inputs by merging static
// JSON with mapped values pulled from this context.
// Serialises to/from JSON for durable persistence in ScenarioExecution.

public sealed class StepContext
{
    private readonly Dictionary<string, JsonElement> _outputs;

    public StepContext() => _outputs = new(StringComparer.Ordinal);
    private StepContext(Dictionary<string, JsonElement> outputs) => _outputs = outputs;

    /// <summary>Write a completed step's output data into the context.</summary>
    public void SetStepOutput(string stepId, JsonElement output)
        => _outputs[stepId] = output;

    /// <summary>Retrieve a prior step's output by step ID.</summary>
    public JsonElement? GetStepOutput(string stepId)
        => _outputs.TryGetValue(stepId, out var v) ? v : null;

    /// <summary>
    /// Merge static step input with values resolved from prior step outputs.
    /// Mapping syntax: { "targetField": "sourceStepId.field" }
    /// Nested fields:  { "targetField": "sourceStepId.nested.deep.field" }
    /// Mapped values override any placeholder values in the static input.
    /// </summary>
    public JsonElement ResolveInput(
        JsonElement staticInput,
        IReadOnlyDictionary<string, string>? mappings)
    {
        if (mappings is null || mappings.Count == 0)
            return staticInput;

        var merged = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(staticInput)
            ?? new Dictionary<string, JsonElement>();

        foreach (var (targetField, sourcePath) in mappings)
        {
            var dotIndex = sourcePath.IndexOf('.');
            if (dotIndex < 0) continue;

            var sourceStepId = sourcePath[..dotIndex];
            var fieldPath    = sourcePath[(dotIndex + 1)..];
            var stepOutput   = GetStepOutput(sourceStepId);

            if (stepOutput is null) continue;

            var resolved = ExtractField(stepOutput.Value, fieldPath);
            if (resolved.HasValue)
                merged[targetField] = resolved.Value;
        }

        return JsonSerializer.SerializeToElement(merged);
    }

    // Walk a dot-separated path through a JsonElement object tree.
    private static JsonElement? ExtractField(JsonElement element, string dotPath)
    {
        var current = element;
        foreach (var part in dotPath.Split('.'))
        {
            if (current.ValueKind != JsonValueKind.Object) return null;
            if (!current.TryGetProperty(part, out current)) return null;
        }
        return current;
    }

    /// <summary>Serialise the context for durable storage.</summary>
    public string Serialise()
        => JsonSerializer.Serialize(_outputs);

    /// <summary>Rehydrate a context from a previously serialised string.</summary>
    public static StepContext Deserialise(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return new StepContext();

        var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)
            ?? new Dictionary<string, JsonElement>();

        return new StepContext(dict);
    }
}

// ── OrchestratorStatus ───────────────────────────────────────────────────────

public enum OrchestratorStatus { Completed, Suspended, Failed }

// ── StepResult ───────────────────────────────────────────────────────────────

public sealed record StepResult(
    string       StepId,
    bool         Success,
    JsonElement? Data,
    ToolError?   Error);

// ── OrchestratorResult ───────────────────────────────────────────────────────
// Returned by IToolPlanOrchestrator after executing a plan (or partial plan).

public sealed class OrchestratorResult
{
    public OrchestratorStatus        Status            { get; private init; }
    public IReadOnlyList<StepResult> CompletedSteps    { get; private init; } = [];
    public StepContext               Context           { get; private init; } = new();

    // Populated when IsSuspended
    public string? SuspendedAtStepId { get; private init; }
    public Guid?   PendingApprovalId { get; private init; }

    // Populated when IsFailed
    public string?    FailedAtStepId { get; private init; }
    public ToolError? FailureError   { get; private init; }

    public bool IsCompleted => Status == OrchestratorStatus.Completed;
    public bool IsSuspended => Status == OrchestratorStatus.Suspended;
    public bool IsFailed    => Status == OrchestratorStatus.Failed;

    public static OrchestratorResult Completed(
        StepContext ctx, IReadOnlyList<StepResult> steps) =>
        new() { Status = OrchestratorStatus.Completed, Context = ctx, CompletedSteps = steps };

    public static OrchestratorResult Suspended(
        Guid approvalId, string stepId,
        StepContext ctx, IReadOnlyList<StepResult> steps) =>
        new() { Status            = OrchestratorStatus.Suspended,
                PendingApprovalId = approvalId,
                SuspendedAtStepId = stepId,
                Context           = ctx,
                CompletedSteps    = steps };

    public static OrchestratorResult Failed(
        string stepId, ToolError? error,
        IReadOnlyList<StepResult> steps) =>
        new() { Status         = OrchestratorStatus.Failed,
                FailedAtStepId = stepId,
                FailureError   = error,
                CompletedSteps = steps };
}

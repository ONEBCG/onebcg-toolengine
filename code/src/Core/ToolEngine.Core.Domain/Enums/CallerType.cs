namespace ToolEngine.Core.Domain.Enums;

/// <summary>
/// Distinguishes human users from automated AI agents.
/// Sourced from the JWT claim "caller_type" and persisted on every
/// ToolInvocationRecord and ToolInvocationEvent for SOC 2 and EU AI Act traceability.
///
/// EU AI Act Article 14 mandates that AI-generated actions be identifiable
/// so that human operators can exercise meaningful oversight.
/// </summary>
public enum CallerType
{
    /// <summary>Request originated from a human user via a UI or direct API call.</summary>
    Human = 0,

    /// <summary>Request originated from an autonomous AI agent acting on behalf of a user or system.</summary>
    AiAgent = 1,

    /// <summary>Request originated from an internal system service (scheduler, background job).</summary>
    SystemService = 2
}

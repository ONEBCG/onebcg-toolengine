namespace ToolEngine.Core.Domain.Enums;

/// <summary>
/// Lifecycle event types emitted to the append-only ToolInvocationEvent table.
/// The table has no UPDATE or DELETE permissions for the application DB user,
/// ensuring tamper-evidence for SOC 2 Type II audits.
/// </summary>
public enum InvocationEventType
{
    /// <summary>Invocation was received and a record created.</summary>
    Invoked = 0,

    /// <summary>Handler execution started (post-approval, post-validation).</summary>
    Running = 1,

    /// <summary>Handler completed successfully.</summary>
    Succeeded = 2,

    /// <summary>Handler or pipeline raised a terminal error.</summary>
    Failed = 3,

    /// <summary>Invocation suspended pending human approval.</summary>
    Suspended = 4,

    /// <summary>Invocation was cancelled by the caller or timeout.</summary>
    Cancelled = 5
}

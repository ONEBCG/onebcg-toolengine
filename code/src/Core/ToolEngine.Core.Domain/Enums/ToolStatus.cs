namespace ToolEngine.Core.Domain.Enums;

public enum ToolStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Cancelled,
    TimedOut,
    /// <summary>
    /// Execution is paused awaiting human approval (ApprovalBehavior returned Suspend).
    /// The invocation record remains open until the approval is decided and the tool re-runs.
    /// </summary>
    Suspended
}

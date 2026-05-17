namespace ToolEngine.Core.Domain.Enums;

/// <summary>Risk level of a tool action. Determines human-in-the-loop gate behaviour.</summary>
public enum ApprovalRisk
{
    Low      = 0,
    Medium   = 1,
    High     = 2,
    /// <summary>Irreversible action (e.g. charge, delete). Always requires approval.</summary>
    Critical = 3
}

namespace ToolEngine.Core.Domain.Enums;

/// <summary>Classifies a tool by its primary concern for template selection and auditing.</summary>
public enum ToolType
{
    /// <summary>Pure computation — no I/O, fully deterministic.</summary>
    Logic,
    /// <summary>Calls an external HTTP API.</summary>
    Api,
    /// <summary>Reads from or writes to a data store.</summary>
    Database,
    /// <summary>Orchestrates other tools (pipeline or DAG).</summary>
    Composite
}

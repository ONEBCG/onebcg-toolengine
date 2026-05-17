namespace ToolEngine.Core.Domain.Enums;

/// <summary>Controls how multiple tools are orchestrated.</summary>
public enum ExecutionMode
{
    /// <summary>Tools run one after another; output of each feeds next.</summary>
    Sequential,
    /// <summary>Tools run concurrently; results collected when all complete.</summary>
    Parallel,
    /// <summary>Tools run in dependency order defined by a directed acyclic graph.</summary>
    Dag
}

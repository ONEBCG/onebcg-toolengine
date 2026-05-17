namespace ToolEngine.Tools.Abstractions.Interfaces;

using ToolEngine.Core.Domain.Enums;
using ToolEngine.Core.Domain.Schema;

/// <summary>
/// Metadata contract for every tool. Decoupled from execution so registries
/// and discovery mechanisms can inspect tools without invoking them.
/// FullName = "namespace.name" — used as the MCP tool name and registry key.
/// </summary>
public interface ITool
{
    /// <summary>Domain slug, e.g. "payment", "weather", "hr". Lowercase, no hyphens.</summary>
    string     Namespace    { get; }
    /// <summary>Action name, e.g. "validate", "current", "user-lookup". Lowercase.</summary>
    string     Name         { get; }
    /// <summary>Fully-qualified registry key: "namespace.name".</summary>
    string     FullName     => $"{Namespace}.{Name}";
    /// <summary>Pinned version, e.g. "v1" or "v2".</summary>
    string     Version      { get; }
    string     Description  { get; }
    ToolType   Type         { get; }
    ToolSchema InputSchema  { get; }
    ToolSchema OutputSchema { get; }
}

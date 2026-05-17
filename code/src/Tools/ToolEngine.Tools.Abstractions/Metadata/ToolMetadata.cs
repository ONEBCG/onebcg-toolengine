namespace ToolEngine.Tools.Abstractions.Metadata;

using ToolEngine.Core.Domain.Enums;
using ToolEngine.Core.Domain.Schema;

/// <summary>
/// Snapshot of a tool's identity — used by the registry to surface tool
/// information without holding a reference to the handler instance.
/// </summary>
public sealed record ToolMetadata(
    string     Namespace,
    string     Name,
    string     Version,
    string     Description,
    ToolType   Type,
    ToolSchema InputSchema,
    ToolSchema OutputSchema,
    string?    TenantId  = null,   // null = global registration
    bool       IsEnabled = true)
{
    public string FullName => $"{Namespace}.{Name}";
}

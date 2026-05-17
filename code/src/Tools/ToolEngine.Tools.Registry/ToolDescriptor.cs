namespace ToolEngine.Tools.Registry;

using ToolEngine.Tools.Abstractions.Metadata;

/// <summary>Immutable snapshot of a registered tool — no reference to the handler instance.</summary>
public sealed record ToolDescriptor(
    ToolMetadata Metadata,
    Type         HandlerType,     // typeof(WeatherTool) etc.
    string?      TenantId = null  // null = global
)
{
    /// <summary>Convenience accessor: "namespace.name".</summary>
    public string FullName => Metadata.FullName;
}

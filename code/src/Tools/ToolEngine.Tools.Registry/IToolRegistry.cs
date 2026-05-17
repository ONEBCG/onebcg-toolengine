namespace ToolEngine.Tools.Registry;

using ToolEngine.Core.Domain.Common;
using ToolEngine.Tools.Abstractions.Interfaces;

public interface IToolRegistry
{
    /// <summary>
    /// Register a tool globally. FullName (namespace.name) is auto-derived from the handler type —
    /// no caller-supplied name means no drift between registration key and tool identity.
    /// </summary>
    void Register<THandler>(string version)
        where THandler : class, ITool;

    /// <summary>Register a tool for a specific tenant only.</summary>
    void RegisterForTenant<THandler>(string version, string tenantId)
        where THandler : class, ITool;

    /// <summary>
    /// Resolve by explicit namespace + name + version. Tenant-scoped registration takes
    /// precedence over global. "latest" resolves to the highest registered v{N}.
    /// </summary>
    Result<ToolDescriptor> Resolve(string ns, string name, string version, string tenantId);

    /// <summary>
    /// Resolve by pre-composed fullName, e.g. "weather.current".
    /// Splits on first dot and delegates to Resolve(ns, name, version, tenantId).
    /// </summary>
    Result<ToolDescriptor> Resolve(string fullName, string version, string tenantId);

    IReadOnlyList<ToolDescriptor> ListAll(string? tenantId = null);

    IReadOnlyList<string> GetVersions(string ns, string name, string? tenantId = null);

    /// <summary>Overload for pre-composed fullName, e.g. "weather.current".</summary>
    IReadOnlyList<string> GetVersions(string fullName, string? tenantId = null);
}

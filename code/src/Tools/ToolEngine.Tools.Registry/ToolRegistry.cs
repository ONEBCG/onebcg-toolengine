namespace ToolEngine.Tools.Registry;

using System.Collections.Concurrent;
using ToolEngine.Core.Domain.Common;
using ToolEngine.Tools.Abstractions.Interfaces;
using ToolEngine.Tools.Abstractions.Metadata;

public sealed class ToolRegistry : IToolRegistry
{
    // Key: (fullName "namespace.name", normalizedVersion, tenantId ?? "")
    private readonly ConcurrentDictionary<(string, string, string), ToolDescriptor> _store = new();

    public void Register<THandler>(string version)
        where THandler : class, ITool =>
        RegisterCore<THandler>(version, tenantId: null);

    public void RegisterForTenant<THandler>(string version, string tenantId)
        where THandler : class, ITool =>
        RegisterCore<THandler>(version, tenantId);

    private void RegisterCore<THandler>(string version, string? tenantId)
        where THandler : class, ITool
    {
        // GetUninitializedObject skips the constructor — safe because Namespace/Name
        // are expression-bodied properties that never read instance fields.
        var instance = (ITool)System.Runtime.CompilerServices
            .RuntimeHelpers.GetUninitializedObject(typeof(THandler));

        var fullName = instance.FullName;   // "namespace.name" e.g. "math.calculate"
        var key      = MakeKey(fullName, version, tenantId);

        var metadata = new ToolMetadata(
            instance.Namespace, instance.Name, instance.Version, instance.Description,
            instance.Type, instance.InputSchema, instance.OutputSchema,
            tenantId);

        _store[key] = new ToolDescriptor(metadata, typeof(THandler), tenantId);
    }

    // --- Resolve ---

    public Result<ToolDescriptor> Resolve(
        string ns, string name, string version, string tenantId) =>
        Resolve($"{ns}.{name}".ToLowerInvariant(), version, tenantId);

    public Result<ToolDescriptor> Resolve(string fullName, string version, string tenantId)
    {
        var normalizedVersion = version.Equals("latest", StringComparison.OrdinalIgnoreCase)
            ? ResolveLatestVersion(fullName, tenantId)
            : version.ToLowerInvariant();

        if (normalizedVersion is null)
            return Result.Failure<ToolDescriptor>(Error.ToolNotFound(fullName, version));

        // Tenant-scoped takes precedence over global.
        if (_store.TryGetValue(MakeKey(fullName, normalizedVersion, tenantId), out var tenantDesc))
            return Result.Success(tenantDesc);

        if (_store.TryGetValue(MakeKey(fullName, normalizedVersion, null), out var globalDesc))
            return Result.Success(globalDesc);

        return Result.Failure<ToolDescriptor>(Error.ToolNotFound(fullName, normalizedVersion));
    }

    // --- List / Versions ---

    public IReadOnlyList<ToolDescriptor> ListAll(string? tenantId = null) =>
        _store.Values
              .Where(d => d.TenantId is null || d.TenantId == tenantId)
              .ToList()
              .AsReadOnly();

    public IReadOnlyList<string> GetVersions(string ns, string name, string? tenantId = null) =>
        GetVersions($"{ns}.{name}".ToLowerInvariant(), tenantId);

    public IReadOnlyList<string> GetVersions(string fullName, string? tenantId = null) =>
        _store.Keys
              .Where(k => k.Item1 == fullName.ToLowerInvariant() &&
                          (k.Item3 == "" || k.Item3 == (tenantId ?? "")))
              .Select(k => k.Item2)
              .Distinct()
              .OrderByDescending(ExtractVersionForOrdering)
              .ToList()
              .AsReadOnly();

    // --- Helpers ---

    private string? ResolveLatestVersion(string fullName, string tenantId) =>
        GetVersions(fullName, tenantId).FirstOrDefault();

    private static (string, string, string) MakeKey(
        string fullName, string version, string? tenantId) =>
        (fullName.ToLowerInvariant(), version.ToLowerInvariant(), tenantId ?? "");

    /// <summary>
    /// H14 — Orders versions so that "latest" resolves to the highest semantic version.
    /// Uses <see cref="Version"/> parsing (strips a leading "v") for accurate comparison:
    /// "v1.10" &gt; "v1.2" where the old \d+ regex returned 1 == 1 (undefined ordering).
    /// Falls back to the first digit run only when the version string is not parseable.
    /// </summary>
    private static Version ExtractVersionForOrdering(string version)
    {
        var stripped = version.TrimStart('v', 'V');
        return Version.TryParse(stripped, out var parsed)
            ? parsed
            : new Version(0, 0);
    }
}

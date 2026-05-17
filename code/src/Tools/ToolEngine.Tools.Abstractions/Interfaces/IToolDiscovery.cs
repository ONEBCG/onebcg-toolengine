namespace ToolEngine.Tools.Abstractions.Interfaces;

using ToolEngine.Core.Domain.Common;
using ToolEngine.Core.Domain.Enums;

/// <summary>
/// Two-mode tool discovery:
/// 1. Exact resolve by namespace + name + version (fast, O(1))
/// 2. Semantic search by natural language intent (for 50+ tool registries)
///
/// Per Anthropic/OpenAI guidance: registries with 50+ tools should use embedding
/// similarity to route agent queries rather than relying on exact name matching.
/// </summary>
public interface IToolDiscovery
{
    Result<ToolDiscoveryDescriptor> Resolve(
        string ns, string name, string version, string tenantId);

    /// <summary>
    /// Finds tools matching the intent using semantic similarity on
    /// ToolSchema.Description + WhenToUse embeddings.
    /// </summary>
    Task<IReadOnlyList<ToolDiscoveryDescriptor>> SearchAsync(
        string            intent,
        string            tenantId,
        int               topK = 5,
        CancellationToken ct   = default);

    IReadOnlyList<ToolDiscoveryDescriptor> ListAll(string? tenantId = null);
    IReadOnlyList<string>                  GetVersions(string ns, string name, string? tenantId = null);
}

/// <summary>
/// Lightweight descriptor returned by IToolDiscovery — no handler type reference.
/// Approval metadata is surfaced here so ApprovalBehavior (Application layer)
/// can check [RequiresApproval] without depending on IToolRegistry or reflection.
/// </summary>
public sealed record ToolDiscoveryDescriptor(
    string       Namespace,
    string       Name,
    string       Version,
    string       Description,
    string       WhenToUse,
    string       WhenNotToUse,
    string?      TenantId              = null,
    bool         NeedsApproval         = false,
    ApprovalRisk ApprovalRisk          = ApprovalRisk.High,
    string?      ApprovalReason        = null)
{
    public string FullName => $"{Namespace}.{Name}";
}

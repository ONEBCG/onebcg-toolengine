namespace ToolEngine.Tools.Registry;

using ToolEngine.Core.Domain.Attributes;
using ToolEngine.Core.Domain.Common;
using ToolEngine.Core.Domain.Enums;
using ToolEngine.Tools.Abstractions.Interfaces;

/// <summary>
/// Concrete IToolDiscovery backed by IToolRegistry.
///
/// Two modes:
///   1. Exact resolve — O(1) lookup by namespace + name + version via registry key.
///   2. Semantic search — text-based stub: scores Description + WhenToUse against
///      the caller's intent using word-overlap. Embeddings deferred to a future phase.
///
/// Approval metadata is surfaced in ToolDiscoveryDescriptor by reflecting on the
/// handler's [RequiresApproval] attribute — so ApprovalBehavior never needs to
/// reference IToolRegistry or perform reflection itself.
/// </summary>
public sealed class ToolDiscovery : IToolDiscovery
{
    private readonly IToolRegistry _registry;

    public ToolDiscovery(IToolRegistry registry) =>
        _registry = registry;

    // --- Exact resolve ---

    public Result<ToolDiscoveryDescriptor> Resolve(
        string ns, string name, string version, string tenantId)
    {
        var result = _registry.Resolve(ns, name, version, tenantId);
        if (result.IsFailure)
            return Result.Failure<ToolDiscoveryDescriptor>(result.Error);

        return Result.Success(ToDescriptor(result.Value));
    }

    // --- Semantic search ---

    public Task<IReadOnlyList<ToolDiscoveryDescriptor>> SearchAsync(
        string            intent,
        string            tenantId,
        int               topK = 5,
        CancellationToken ct   = default)
    {
        if (string.IsNullOrWhiteSpace(intent))
            return Task.FromResult<IReadOnlyList<ToolDiscoveryDescriptor>>([]);

        var intentWords = Tokenize(intent);

        var ranked = _registry.ListAll(tenantId)
            .Select(d => (Descriptor: ToDescriptor(d), Score: Score(d, intentWords)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .Select(x => x.Descriptor)
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyList<ToolDiscoveryDescriptor>>(ranked);
    }

    // --- List / versions ---

    public IReadOnlyList<ToolDiscoveryDescriptor> ListAll(string? tenantId = null) =>
        _registry.ListAll(tenantId)
                 .Select(ToDescriptor)
                 .ToList()
                 .AsReadOnly();

    public IReadOnlyList<string> GetVersions(string ns, string name, string? tenantId = null) =>
        _registry.GetVersions(ns, name, tenantId);

    // --- Helpers ---

    private static ToolDiscoveryDescriptor ToDescriptor(ToolDescriptor d)
    {
        // Reflect on the handler type to surface [RequiresApproval] metadata.
        var attr = d.HandlerType
            .GetCustomAttributes(typeof(RequiresApprovalAttribute), inherit: false)
            .FirstOrDefault() as RequiresApprovalAttribute;

        return new(
            d.Metadata.Namespace,
            d.Metadata.Name,
            d.Metadata.Version,
            d.Metadata.Description,
            d.Metadata.InputSchema.WhenToUse,
            d.Metadata.InputSchema.WhenNotToUse,
            d.TenantId,
            NeedsApproval:  attr is not null,
            ApprovalRisk:   attr?.Risk   ?? ApprovalRisk.High,
            ApprovalReason: attr?.Reason);
    }

    private static double Score(ToolDescriptor d, HashSet<string> intentWords)
    {
        var corpus = Tokenize(
            $"{d.Metadata.Description} {d.Metadata.InputSchema.WhenToUse}");

        double bonus = intentWords.Contains(d.Metadata.Name.ToLowerInvariant()) ? 2.0
                     : intentWords.Contains(d.FullName.ToLowerInvariant())       ? 3.0
                     : 0.0;

        return corpus.Intersect(intentWords).Count() + bonus;
    }

    private static HashSet<string> Tokenize(string text) =>
        text.ToLowerInvariant()
            .Split([' ', ',', '.', '-', '_', '(', ')', '\n', '\r'],
                   StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet();
}

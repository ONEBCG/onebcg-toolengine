namespace ToolEngine.Tools.Registry;

using ToolEngine.Core.Domain.Common;
using ToolEngine.Tools.Abstractions.Interfaces;

/// <summary>
/// Concrete IToolDiscovery backed by IToolRegistry.
///
/// Two modes:
///   1. Exact resolve — O(1) lookup by namespace + name + version via registry key.
///   2. Semantic search — text-based stub: scores Description + WhenToUse against
///      the caller's intent using word-overlap. Embeddings deferred to Phase D.
///
/// Per Anthropic/OpenAI guidance: registries with 50+ tools should expose discovery
/// so agents can route by intent rather than exact name matching.
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

    private static ToolDiscoveryDescriptor ToDescriptor(ToolDescriptor d) =>
        new(d.Metadata.Namespace,
            d.Metadata.Name,
            d.Metadata.Version,
            d.Metadata.Description,
            d.Metadata.InputSchema.WhenToUse,
            d.Metadata.InputSchema.WhenNotToUse,
            d.TenantId);

    // Word-overlap scoring: count how many intent words appear in the tool's
    // Description + WhenToUse text. Simple and deterministic — no external deps.
    private static double Score(ToolDescriptor d, HashSet<string> intentWords)
    {
        var corpus = Tokenize(
            $"{d.Metadata.Description} {d.Metadata.InputSchema.WhenToUse}");

        // Weighted: exact FullName / Name match gets a bonus.
        double bonus = intentWords.Contains(d.Metadata.Name.ToLowerInvariant())  ? 2.0
                     : intentWords.Contains(d.FullName.ToLowerInvariant())        ? 3.0
                     : 0.0;

        return corpus.Intersect(intentWords).Count() + bonus;
    }

    private static HashSet<string> Tokenize(string text) =>
        text.ToLowerInvariant()
            .Split([' ', ',', '.', '-', '_', '(', ')', '\n', '\r'],
                   StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet();
}

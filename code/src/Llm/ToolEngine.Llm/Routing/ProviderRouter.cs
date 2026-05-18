namespace ToolEngine.Llm.Routing;

using Microsoft.Extensions.Options;
using ToolEngine.Llm.Abstractions;
using ToolEngine.Llm.Attributes;
using ToolEngine.Llm.Options;
using ToolEngine.Tools.Registry;

public sealed class ProviderRouter : IProviderRouter
{
    private readonly IReadOnlyDictionary<string, ILlmProvider> _providers;
    private readonly LlmOptions                                 _options;

    public ProviderRouter(IEnumerable<ILlmProvider> providers, IOptions<LlmOptions> options)
    {
        _providers = providers.ToDictionary(p => p.ProviderName, StringComparer.OrdinalIgnoreCase);
        _options   = options.Value;
    }

    public (ILlmProvider Provider, ProviderOptions Options) Select(
        string?         tenantProviderOverride,
        ToolDescriptor? toolDescriptor = null)
    {
        // Precedence: [LlmProvider] attribute on tool class > tenant override > global default
        var key      = ResolveKey(tenantProviderOverride, toolDescriptor);
        var provider = FindProvider(key);
        var opts = _options.Providers.TryGetValue(provider.ProviderName, out var o)
            ? o
            : new ProviderOptions();
        return (provider, opts);
    }

    private string ResolveKey(string? tenantOverride, ToolDescriptor? descriptor)
    {
        // 1. Tool-level attribute
        if (descriptor is not null)
        {
            var attr = descriptor.HandlerType
                .GetCustomAttributes(typeof(LlmProviderAttribute), false)
                .FirstOrDefault() as LlmProviderAttribute;
            if (attr is not null) return attr.ProviderName;
        }

        // 2. Tenant override
        if (!string.IsNullOrWhiteSpace(tenantOverride)) return tenantOverride;

        // 3. Global default
        return _options.Routing.DefaultProvider;
    }

    private ILlmProvider FindProvider(string key)
    {
        if (_providers.TryGetValue(key, out var p)) return p;

        // Try fallback chain
        foreach (var fallback in _options.Routing.FallbackChain)
        {
            if (_providers.TryGetValue(fallback, out var fp)) return fp;
        }

        // M7 — throw rather than silently returning _providers.Values.First():
        // dictionary iteration order depends on DI registration order; a silent
        // "first registered" fallback makes routing non-deterministic and hides
        // misconfiguration until production incidents occur.
        var registered = string.Join(", ", _providers.Keys);
        throw new InvalidOperationException(
            $"LLM provider '{key}' is not registered and no fallback in the chain matched. " +
            $"Registered providers: [{registered}]. " +
            $"Check Llm:DefaultProvider and Llm:Routing:FallbackChain in appsettings.");
    }
}

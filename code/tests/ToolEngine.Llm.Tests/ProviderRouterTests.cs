namespace ToolEngine.Llm.Tests;

using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using ToolEngine.Core.Domain.Enums;
using ToolEngine.Core.Domain.Schema;
using ToolEngine.Llm.Abstractions;
using ToolEngine.Llm.Attributes;
using ToolEngine.Llm.Options;
using ToolEngine.Llm.Routing;
using ToolEngine.Tools.Abstractions.Metadata;
using ToolEngine.Tools.Registry;
using Xunit;

public sealed class ProviderRouterTests
{
    private static ILlmProvider MakeProvider(string name)
    {
        var p = Substitute.For<ILlmProvider>();
        p.ProviderName.Returns(name);
        return p;
    }

    private static ProviderRouter BuildRouter(
        string defaultProvider = "anthropic",
        string[]? fallback = null,
        params ILlmProvider[] providers)
    {
        var opts = new LlmOptions
        {
            Routing   = new RoutingOptions { DefaultProvider = defaultProvider, FallbackChain = fallback ?? [] },
            Providers = providers.ToDictionary(p => p.ProviderName, _ => new ProviderOptions())
        };
        return new ProviderRouter(providers, Options.Create(opts));
    }

    [Fact]
    public void Select_UsesGlobalDefault_WhenNoOverrides()
    {
        var anthropic = MakeProvider("anthropic");
        var router    = BuildRouter("anthropic", providers: [anthropic]);

        var (provider, _) = router.Select(tenantProviderOverride: null);
        provider.ProviderName.Should().Be("anthropic");
    }

    [Fact]
    public void Select_TenantOverride_Wins_Over_Default()
    {
        var anthropic = MakeProvider("anthropic");
        var openai    = MakeProvider("openai");
        var router    = BuildRouter("anthropic", providers: [anthropic, openai]);

        var (provider, _) = router.Select(tenantProviderOverride: "openai");
        provider.ProviderName.Should().Be("openai");
    }

    [Fact]
    public void Select_ToolAttribute_Wins_Over_TenantOverride()
    {
        var anthropic = MakeProvider("anthropic");
        var openai    = MakeProvider("openai");
        var ollama    = MakeProvider("ollama");
        var router    = BuildRouter("anthropic", providers: [anthropic, openai, ollama]);

        // Create a descriptor for a tool class decorated with [LlmProvider("ollama")]
        var schema     = ToolSchema.For<object>("A tool.");
        var metadata   = new ToolMetadata("math", "calculate", "v1", "A tool.", ToolType.Logic, schema, ToolSchema.Empty);
        var descriptor = new ToolDescriptor(metadata, typeof(LlmProviderTaggedTool));

        var (provider, _) = router.Select(tenantProviderOverride: "openai", toolDescriptor: descriptor);
        provider.ProviderName.Should().Be("ollama");
    }

    [Fact]
    public void Select_FallsBackToChain_WhenDefaultUnknown()
    {
        var openai = MakeProvider("openai");
        var router = BuildRouter("anthropic", fallback: ["openai"], providers: [openai]);

        var (provider, _) = router.Select(tenantProviderOverride: null);
        provider.ProviderName.Should().Be("openai");
    }

    // Helper tool class tagged with [LlmProvider("ollama")]
    [LlmProvider("ollama")]
    private sealed class LlmProviderTaggedTool { }
}

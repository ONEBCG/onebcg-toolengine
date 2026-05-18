namespace ToolEngine.Llm.Extensions;

using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ToolEngine.Llm.Abstractions;
using ToolEngine.Llm.Commands;
using ToolEngine.Llm.Conversion;
using ToolEngine.Llm.Guards;
using ToolEngine.Llm.Handlers;
using ToolEngine.Llm.Options;
using ToolEngine.Llm.Providers;
using ToolEngine.Llm.Routing;
using ToolEngine.Llm.Session;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the LLM agent layer.
    /// Call AFTER AddToolApplication() in the composition root.
    /// Registers Anthropic, OpenAI, and Ollama providers.
    /// Registers AgentOrchestrator, AgentSessionStore, ProviderRouter, ToolSchemaConverter.
    /// Registers AgentChatHandler with MediatR.
    /// </summary>
    public static IServiceCollection AddToolLlm(
        this IServiceCollection services,
        IConfiguration          configuration)
    {
        // Options
        services.AddOptions<LlmOptions>().Bind(configuration.GetSection("Llm"));

        // HTTP clients — named, each provider uses its own instance
        services.AddHttpClient("anthropic");
        services.AddHttpClient("openai");
        services.AddHttpClient("ollama");

        // Providers (all registered; router selects at runtime)
        services.AddSingleton<ILlmProvider, AnthropicLlmProvider>();
        services.AddSingleton<ILlmProvider, OpenAiLlmProvider>();
        services.AddSingleton<ILlmProvider, OllamaLlmProvider>();

        // Routing + conversion
        services.AddSingleton<IProviderRouter, ProviderRouter>();
        services.AddSingleton<ToolSchemaConverter>();

        // Tool guard — singleton; reads options once at startup.
        // Applied at two enforcement points inside AgentOrchestrator:
        //   1. Pre-LLM: strips blocked tools from the schema before the provider call.
        //   2. Post-selection: re-validates the LLM's chosen tool before MediatR executes it.
        services.AddSingleton<ToolGuardFilter>();

        // Scope enforcer — singleton; builds the response-quality system prompt
        // (missing params + response grounding rules) injected into each new session.
        services.AddSingleton<AgentScopeEnforcer>();

        // Scope classifier — singleton; makes a pre-flight LLM call before each
        // main loop to classify which parts of the request are tool-addressable.
        // Fails open so availability is not affected by classification errors.
        services.AddSingleton<AgentScopeClassifier>();

        // Session store (uses existing ICacheProvider)
        services.AddSingleton<IAgentSessionStore, AgentSessionStore>();

        // Orchestrator (scoped — uses IMediator which is scoped)
        services.AddScoped<AgentOrchestrator>();

        // MediatR handler for AgentChatCommand
        services.AddTransient<IRequestHandler<AgentChatCommand, AgentChatResponse>, AgentChatHandler>();

        return services;
    }
}

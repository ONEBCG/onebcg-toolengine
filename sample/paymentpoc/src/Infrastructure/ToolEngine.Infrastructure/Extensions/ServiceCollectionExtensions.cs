using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using ToolEngine.Core.Abstractions.Cache;
using ToolEngine.Core.Abstractions.Common;
using ToolEngine.Core.Abstractions.Llm;
using ToolEngine.Core.Abstractions.Secrets;
using ToolEngine.Infrastructure.Approval;
using ToolEngine.Infrastructure.Cache;
using ToolEngine.Infrastructure.Common;
using ToolEngine.Infrastructure.Database;
using ToolEngine.Infrastructure.Llm;
using ToolEngine.Infrastructure.Persistence;
using ToolEngine.Infrastructure.Secrets;
using ToolEngine.Tools.Abstractions.Interfaces;

namespace ToolEngine.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all infrastructure services.
    ///
    /// Provider selection driven by appsettings:
    ///   Database:Provider  — "sqlite" (default) | "sqlserver" | "postgres"
    ///   Cache:Provider     — "memory" (default) | "redis"
    ///   LLM:Provider       — "claude" (default) | "openai"
    ///
    /// LLM API keys can be set via appsettings or environment variables:
    ///   ANTHROPIC_API_KEY  (Claude)
    ///   OPENAI_API_KEY     (OpenAI)
    /// </summary>
    public static IServiceCollection AddToolInfrastructure(
        this IServiceCollection services,
        IConfiguration          configuration)
    {
        // ── Database ──────────────────────────────────────────────────────────
        var dbOpts = configuration.GetSection(DatabaseOptions.Section)
                         .Get<DatabaseOptions>() ?? new DatabaseOptions();

        services.Configure<DatabaseOptions>(
            configuration.GetSection(DatabaseOptions.Section));

        if (dbOpts.Provider.Equals("sqlserver", StringComparison.OrdinalIgnoreCase))
        {
            services.AddDbContext<AppDbContext>((sp, opts) =>
                opts.UseSqlServer(dbOpts.ConnectionString, sql =>
                {
                    sql.EnableRetryOnFailure(maxRetryCount: 3);
                    sql.CommandTimeout(30);
                    sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                }));
        }
        else if (dbOpts.Provider.Equals("postgres", StringComparison.OrdinalIgnoreCase))
        {
            services.AddDbContext<AppDbContext>((sp, opts) =>
                opts.UseNpgsql(dbOpts.ConnectionString, pg =>
                    pg.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)));
        }
        else // sqlite (default)
        {
            services.AddDbContext<AppDbContext>((sp, opts) =>
                opts.UseSqlite(dbOpts.ConnectionString));
        }

        services.AddScoped<ToolEngine.Core.Abstractions.Persistence.IUnitOfWork, UnitOfWork>();

        // ── Cache ─────────────────────────────────────────────────────────────
        var cacheOpts = configuration.GetSection(CacheOptions.Section)
                            .Get<CacheOptions>() ?? new CacheOptions();

        services.Configure<CacheOptions>(
            configuration.GetSection(CacheOptions.Section));

        if (cacheOpts.Provider.Equals("redis", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IConnectionMultiplexer>(
                _ => ConnectionMultiplexer.Connect(cacheOpts.Redis.ConnectionString));
            services.AddSingleton<ICacheProvider, RedisCacheProvider>();
        }
        else // memory (default)
        {
            services.AddMemoryCache();
            services.AddSingleton<ICacheProvider, MemoryCacheProvider>();
        }

        // ── LLM ───────────────────────────────────────────────────────────────
        var llmOpts = configuration.GetSection(LlmOptions.Section)
                          .Get<LlmOptions>() ?? new LlmOptions();

        services.Configure<LlmOptions>(
            configuration.GetSection(LlmOptions.Section));

        if (llmOpts.Provider.Equals("openai", StringComparison.OrdinalIgnoreCase))
        {
            var apiKey = ResolveApiKey(llmOpts.OpenAI.ApiKey, "OPENAI_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey))
                services.AddSingleton<ILlmProvider, NullLlmProvider>();
            else
                services.AddSingleton<ILlmProvider>(sp =>
                    new OpenAiProvider(
                        sp.GetRequiredService<IHttpClientFactory>(),
                        llmOpts.OpenAI with { ApiKey = apiKey },
                        llmOpts.Streaming));
        }
        else if (llmOpts.Provider.Equals("gemini", StringComparison.OrdinalIgnoreCase))
        {
            var apiKey = ResolveApiKey(llmOpts.Gemini.ApiKey, "GOOGLE_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey))
                services.AddSingleton<ILlmProvider, NullLlmProvider>();
            else
                services.AddSingleton<ILlmProvider>(sp =>
                    new GeminiProvider(
                        sp.GetRequiredService<IHttpClientFactory>(),
                        llmOpts.Gemini with { ApiKey = apiKey },
                        llmOpts.Streaming));
        }
        else // claude (default)
        {
            var apiKey = ResolveApiKey(llmOpts.Claude.ApiKey, "ANTHROPIC_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey))
                services.AddSingleton<ILlmProvider, NullLlmProvider>();
            else
                services.AddSingleton<ILlmProvider>(sp =>
                    new ClaudeProvider(
                        sp.GetRequiredService<IHttpClientFactory>(),
                        llmOpts.Claude with { ApiKey = apiKey },
                        llmOpts.Streaming));
        }

        // ── Supporting services ───────────────────────────────────────────────
        services.AddScoped<IHumanApprovalGate, AsyncApprovalGate>();
        services.AddSingleton<ISecretVault, NullSecretVault>();
        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();

        return services;
    }

    /// <summary>
    /// Resolves an API key: appsettings value takes precedence;
    /// falls back to an environment variable if the config value is empty.
    /// </summary>
    private static string ResolveApiKey(string configValue, string envVarName) =>
        !string.IsNullOrWhiteSpace(configValue)
            ? configValue
            : Environment.GetEnvironmentVariable(envVarName) ?? string.Empty;
}

namespace ToolEngine.Llm.Providers;

using Microsoft.Extensions.Logging;
using ToolEngine.Llm.Options;

/// <summary>
/// Ollama local inference — OpenAI-compatible API at http://localhost:11434/v1/chat/completions.
/// No API key required. BaseUrl is read from ProviderOptions.BaseUrl.
/// </summary>
public sealed class OllamaLlmProvider : OpenAiLlmProvider
{
    public override string ProviderName    => "ollama";
    protected override string HttpClientName => "ollama";
    protected override bool   RequiresBearer => false;

    protected override string GetBaseUrl(ProviderOptions options)
    {
        var baseAddr = !string.IsNullOrWhiteSpace(options.BaseUrl)
            ? options.BaseUrl.TrimEnd('/')
            : "http://localhost:11434";
        return $"{baseAddr}/v1/chat/completions";
    }

    public OllamaLlmProvider(IHttpClientFactory httpFactory, ILogger<OpenAiLlmProvider> logger)
        : base(httpFactory, logger) { }
}

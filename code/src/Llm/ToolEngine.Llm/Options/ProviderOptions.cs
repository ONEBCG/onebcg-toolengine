namespace ToolEngine.Llm.Options;

public sealed class ProviderOptions
{
    /// <summary>Provider-specific model identifier (e.g. "claude-sonnet-4-5", "gpt-4o").</summary>
    public string  Model          { get; set; } = "gpt-4o";

    /// <summary>
    /// Name of the environment variable that holds the API key.
    /// The key is resolved at call time via <c>Environment.GetEnvironmentVariable</c> —
    /// never stored in config files.
    /// </summary>
    public string? ApiKeyEnvVar   { get; set; }

    /// <summary>
    /// Optional base URL override. Used by Ollama (http://localhost:11434) and
    /// Azure OpenAI deployments. When null, the provider uses its default endpoint.
    /// </summary>
    public string? BaseUrl        { get; set; }

    /// <summary>
    /// Output token ceiling passed as <c>max_tokens</c> to the provider API.
    /// 2 048 is sufficient for tool-selection and summary responses.
    /// </summary>
    public int     MaxTokens      { get; set; } = 2_048;

    /// <summary>
    /// HTTP request timeout in seconds. Anthropic/OpenAI: 30 s covers p99 latency.
    /// Ollama: 120 s accounts for cold-start model load on first request.
    /// </summary>
    public int     TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Sampling temperature passed to the provider API.
    /// Controls response randomness: 0.0 = deterministic (best for classification/JSON),
    /// 1.0 = default creative sampling, 2.0 = maximum randomness (OpenAI scale).
    /// <para>
    /// Anthropic accepts 0.0–1.0. OpenAI accepts 0.0–2.0.
    /// The <see cref="AgentScopeClassifier"/> overrides this to 0.0 for its
    /// dedicated classification call regardless of what is set here.
    /// </para>
    /// Default: 1.0 — preserves the provider's standard sampling behaviour.
    /// </summary>
    public double  Temperature    { get; set; } = 1.0;
}

namespace ToolEngine.Llm.Options;

public sealed class BudgetOptions
{
    /// <summary>
    /// Advisory ceiling on LLM output tokens per call. Passed as <c>max_tokens</c>
    /// to the provider API via <see cref="ProviderOptions.MaxTokens"/> — this value
    /// documents the intent; the effective limit is whichever is lower.
    /// Tool-selection outputs are 100–500 tokens; summaries 300–800 tokens.
    /// </summary>
    public int MaxTokensPerRequest  { get; set; } = 2_048;

    /// <summary>
    /// Cumulative token circuit-breaker checked before each LLM call.
    /// When <c>session.TokensUsed &gt;= MaxTokensPerSession</c> the orchestrator
    /// returns <c>AgentResult.BudgetExceeded</c> without making another LLM call.
    /// </summary>
    public int MaxTokensPerSession  { get; set; } = 16_384;

    /// <summary>
    /// Hard stop on the number of agentic loop iterations.
    /// Typical flow (understand → select → execute → summarise) uses 2–3 iterations.
    /// Default of 5 allows one disambiguation round plus one retry.
    /// </summary>
    public int MaxIterations        { get; set; } = 5;
}

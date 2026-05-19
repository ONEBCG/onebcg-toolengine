namespace ToolEngine.Llm.Prompts;

/// <summary>
/// Provides named LLM prompt strings loaded from an external source.
///
/// Externalising prompts from C# source allows behaviour tuning — grounding rules,
/// classification instructions, fallback messages — without a code recompile.
/// All implementations must throw <see cref="KeyNotFoundException"/> when a key is not found
/// so callers fail loudly at startup rather than silently sending an empty system prompt.
/// </summary>
public interface IPromptStore
{
    /// <summary>
    /// Returns the prompt text registered under <paramref name="key"/>.
    /// </summary>
    /// <param name="key">Dot-separated prompt key, e.g. "agent.grounding-reminder".</param>
    /// <exception cref="KeyNotFoundException">
    /// Thrown when no prompt is registered for <paramref name="key"/>.
    /// Callers should fail fast — an absent prompt causes silent behavioural regression.
    /// </exception>
    string Get(string key);

    /// <summary>
    /// Returns the prompt text registered under <paramref name="key"/>,
    /// or <paramref name="defaultValue"/> when the key is absent.
    /// Use only for optional / gracefully-degradable prompts.
    /// </summary>
    string GetOrDefault(string key, string defaultValue);
}

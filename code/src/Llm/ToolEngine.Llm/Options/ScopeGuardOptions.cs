namespace ToolEngine.Llm.Options;

/// <summary>
/// Controls the pre-flight scope classification guard that prevents the LLM from
/// answering requests outside the domain of available tools.
///
/// <para>
/// <b>How it works:</b>
/// Before each main orchestration loop, <c>AgentScopeClassifier</c> makes a
/// lightweight LLM call to classify which parts of the user's request can be
/// fulfilled by available tools. Three outcomes:
/// <list type="bullet">
///   <item>
///     <b>Fully out of scope</b> — returned as a friendly refusal with no tool invoked.
///   </item>
///   <item>
///     <b>Mixed request</b> — only the tool-addressable portion is forwarded to the
///     main loop; out-of-scope parts are noted in the final reply.
///   </item>
///   <item>
///     <b>Fully in scope</b> — request passes through unchanged.
///   </item>
/// </list>
/// </para>
///
/// <para>
/// <b>Why configuration-based:</b> prompt-only approaches (system prompt instructions)
/// are unreliable — capable LLMs override them using their training instinct to be
/// helpful. This guard uses a dedicated structural LLM call whose JSON output is
/// validated by the orchestrator before the main loop is entered.
/// </para>
/// </summary>
public sealed class ScopeGuardOptions
{
    /// <summary>
    /// When <c>true</c> (default), the pre-flight scope classification call runs
    /// before every request. Requests fully outside tool domain are refused;
    /// mixed requests are trimmed to their tool-addressable portions only.
    /// <para>
    /// Set to <c>false</c> only in fully-trusted development environments where
    /// you want the LLM to answer freely regardless of tool scope.
    /// Default: <c>true</c>.
    /// </para>
    /// </summary>
    public bool Enabled { get; set; } = true;
}

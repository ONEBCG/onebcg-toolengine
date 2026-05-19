namespace ToolEngine.Llm.Guards;

using System.Text;
using ToolEngine.Llm.Prompts;
using ToolEngine.Tools.Registry;

/// <summary>
/// Derives a system prompt for the main orchestration loop from the registered
/// tool set and the configurable behavioural rules in prompts.json.
///
/// Scoped to response-quality rules only — scope classification and
/// request rewriting are handled upstream by <see cref="AgentScopeClassifier"/>
/// before this prompt is ever injected.
///
/// <para>
/// Two rules injected (loaded from prompts.json so they can be tuned without recompile):
/// <list type="number">
///   <item>
///     Missing parameters — ask for only the specific values required by the tool;
///     never guess or fabricate inputs.
///   </item>
///   <item>
///     Response grounding — the final answer must be derived exclusively from tool
///     output; no general knowledge or training data may be added.
///   </item>
/// </list>
/// </para>
///
/// <para>
/// Injection point: the system prompt is injected as the first
/// <c>MessageRole.System</c> message in a new session by
/// <c>AgentOrchestrator</c>. Multi-turn sessions carry it forward automatically.
/// </para>
/// </summary>
public sealed class AgentScopeEnforcer
{
    private readonly IPromptStore _prompts;

    public AgentScopeEnforcer(IPromptStore prompts)
        => _prompts = prompts;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a system prompt that constrains the LLM's response behaviour for
    /// the main orchestration loop. The prompt is re-derived on each new session
    /// so it always reflects the currently available (and guard-filtered) tool set.
    /// </summary>
    /// <param name="tools">
    /// The filtered tool list — must already be the post-<c>ToolGuardFilter</c>
    /// result so the tool list exactly matches what the model can invoke.
    /// </param>
    public string BuildSystemPrompt(IReadOnlyList<ToolDescriptor> tools)
    {
        var sb = new StringBuilder();

        sb.AppendLine(_prompts.Get(PromptKeys.ScopeEnforcerIntro));
        sb.AppendLine();

        AppendToolList(sb, tools);

        sb.AppendLine(_prompts.Get(PromptKeys.ScopeEnforcerRules));

        return sb.ToString().TrimEnd();
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static void AppendToolList(StringBuilder sb, IReadOnlyList<ToolDescriptor> tools)
    {
        sb.AppendLine("── AVAILABLE TOOLS ─────────────────────────────────────────────────────────");

        if (tools.Count == 0)
        {
            sb.AppendLine("(No tools are currently available. Reject all requests as out of scope.)");
            sb.AppendLine();
            return;
        }

        foreach (var t in tools)
        {
            var m = t.Metadata;
            sb.AppendLine($"• {m.FullName}");
            sb.AppendLine($"  Purpose : {m.InputSchema.Description}");

            if (!string.IsNullOrWhiteSpace(m.InputSchema.WhenToUse))
                sb.AppendLine($"  Use when: {m.InputSchema.WhenToUse}");

            if (!string.IsNullOrWhiteSpace(m.InputSchema.WhenNotToUse))
                sb.AppendLine($"  Avoid   : {m.InputSchema.WhenNotToUse}");
        }

        sb.AppendLine();
    }
}

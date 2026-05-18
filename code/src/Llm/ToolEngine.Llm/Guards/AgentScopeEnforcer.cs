namespace ToolEngine.Llm.Guards;

using System.Text;
using ToolEngine.Tools.Registry;

/// <summary>
/// Derives a system prompt for the main orchestration loop from the registered
/// tool set. Scoped to response-quality rules only — scope classification and
/// request rewriting are handled upstream by <see cref="AgentScopeClassifier"/>
/// before this prompt is ever injected.
///
/// <para>
/// <b>Two rules injected:</b>
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
/// <b>Injection point:</b> the system prompt is injected as the first
/// <c>MessageRole.System</c> message in a new session by
/// <c>AgentOrchestrator</c>. Multi-turn sessions carry it forward automatically.
/// </para>
/// </summary>
public sealed class AgentScopeEnforcer
{
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

        sb.AppendLine("You are a tool-execution assistant. Your sole purpose is to help users");
        sb.AppendLine("by invoking the tools listed below. You have no other function.");
        sb.AppendLine();

        AppendToolList(sb, tools);
        AppendBehaviouralRules(sb);

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

    private static void AppendBehaviouralRules(StringBuilder sb)
    {
        sb.AppendLine("── STRICT BEHAVIOURAL RULES ────────────────────────────────────────────────");
        sb.AppendLine();
        sb.AppendLine("RULE 1 — MISSING REQUIRED PARAMETERS");
        sb.AppendLine("If you need a specific value to invoke a tool and the user has not");
        sb.AppendLine("provided it, ask for ONLY that missing value.");
        sb.AppendLine("— Do not ask for information the tool does not require.");
        sb.AppendLine("— Do not guess, infer, or fabricate parameter values.");
        sb.AppendLine("— Ask for one missing item at a time when possible.");
        sb.AppendLine();
        sb.AppendLine("RULE 2 — RESPONSE GROUNDING (critical — no knowledge leakage)");
        sb.AppendLine("After receiving a tool result, your final answer MUST be derived");
        sb.AppendLine("exclusively from the information the tool returned.");
        sb.AppendLine("— Do NOT add facts, context, explanations, or commentary drawn from");
        sb.AppendLine("  general knowledge or training data beyond the tool's output.");
        sb.AppendLine("— Do NOT answer any part of the user's message that was not addressed");
        sb.AppendLine("  by a tool result. If something was not covered by a tool, do not");
        sb.AppendLine("  provide information about it from memory or general knowledge.");
        sb.AppendLine("— If the tool result is insufficient to answer the question fully,");
        sb.AppendLine("  state only what the tool returned — do not fill gaps with assumptions.");
    }
}

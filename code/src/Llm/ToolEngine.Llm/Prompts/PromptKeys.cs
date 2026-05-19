namespace ToolEngine.Llm.Prompts;

/// <summary>
/// Compile-time keys for every prompt in <c>prompts.json</c>.
///
/// Centralising the keys here means a renamed JSON property is caught at compile time
/// rather than at runtime when <see cref="IPromptStore.Get"/> throws KeyNotFoundException.
/// Every call site should reference these constants, not bare strings.
/// </summary>
public static class PromptKeys
{
    /// <summary>
    /// Injected as a User message immediately after each tool result.
    /// Instructs the model to confine its reply to information from the tool output only.
    /// </summary>
    public const string AgentGroundingReminder = "agent.grounding-reminder";

    /// <summary>
    /// Fallback system prompt used by the LLM provider when no session system message exists.
    /// Active only for providers that require a non-empty system field (e.g. Anthropic).
    /// </summary>
    public const string AgentFallbackSystem = "agent.fallback-system";

    /// <summary>
    /// Returned when the scope classifier determines the request is fully out of scope
    /// and the model did not supply a specific refusal message.
    /// </summary>
    public const string AgentScopeDefaultRefusal = "agent.scope.default-refusal";

    /// <summary>
    /// Returned when no tools are registered for the tenant, making every request out of scope.
    /// </summary>
    public const string AgentScopeNoToolsMessage = "agent.scope.no-tools-message";

    /// <summary>
    /// Opening paragraph of the scope-enforcer system prompt.
    /// States the assistant's purpose and sole function.
    /// </summary>
    public const string ScopeEnforcerIntro = "scope-enforcer.intro";

    /// <summary>
    /// The two behavioural rules (missing-parameter handling and response grounding)
    /// appended to every scope-enforcer system prompt.
    /// </summary>
    public const string ScopeEnforcerRules = "scope-enforcer.behavioural-rules";

    /// <summary>
    /// Output-format instruction placed at the top of the scope-classifier prompt.
    /// Positioned first so RLHF-trained models weight format compliance most highly.
    /// </summary>
    public const string ScopeClassifierOutputFormat = "scope-classifier.output-format";

    /// <summary>
    /// Field-rule definitions and examples for the scope-classifier JSON output schema.
    /// </summary>
    public const string ScopeClassifierFieldRules = "scope-classifier.field-rules";
}

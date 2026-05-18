namespace ToolEngine.Llm.Options;

/// <summary>
/// Configuration-based guard that controls which tools the LLM agent is permitted
/// to see and invoke. Applied at two points in the orchestration loop:
///
/// <list type="number">
///   <item>
///     <b>Pre-LLM filter</b> — tools not matching the allowlist (or matching the
///     denylist) are stripped from the schema sent to the LLM provider. The model
///     never sees or selects these tools.
///   </item>
///   <item>
///     <b>Post-selection guard</b> — after the LLM returns a tool_use decision, the
///     selected tool is re-validated before the MediatR pipeline executes it. This
///     is defence in depth against prompt-injection attacks where a malicious user
///     prompt tricks the LLM into "calling" a tool it was not shown.
///   </item>
/// </list>
///
/// <b>Precedence:</b> deny overrides allow. If a tool appears in both lists it is
/// blocked. If <see cref="AllowedTools"/> is empty, all tools pass the allowlist
/// check (only the denylist is active).
///
/// <b>Pattern syntax:</b>
/// <list type="bullet">
///   <item><c>"math.calculate"</c> — exact full name match (case-insensitive)</item>
///   <item><c>"math.*"</c> — all tools whose namespace is <c>math</c></item>
///   <item><c>"*"</c> — all tools</item>
/// </list>
/// </summary>
public sealed class ToolGuardOptions
{
    /// <summary>
    /// When <c>false</c> the guard is bypassed entirely — all tools are visible
    /// and executable by the LLM. Set to <c>false</c> only in fully-trusted
    /// development environments.
    /// Default: <c>true</c>.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Tools the LLM is allowed to see and invoke.
    /// Supports exact names (<c>"math.calculate"</c>), namespace wildcards
    /// (<c>"math.*"</c>), and global wildcard (<c>"*"</c>).
    /// <para>
    /// Empty list means <b>all tools are permitted</b> (subject to denylist).
    /// To restrict to a specific set, list each tool or namespace pattern.
    /// </para>
    /// </summary>
    public List<string> AllowedTools { get; set; } = new();

    /// <summary>
    /// Tools the LLM is explicitly blocked from seeing or invoking.
    /// Deny overrides allow: a tool in both lists is always blocked.
    /// Supports the same pattern syntax as <see cref="AllowedTools"/>.
    /// <para>
    /// Empty list means <b>nothing is denied</b> (only the allowlist applies).
    /// </para>
    /// </summary>
    public List<string> DeniedTools { get; set; } = new();
}

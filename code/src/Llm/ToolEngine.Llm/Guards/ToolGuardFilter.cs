namespace ToolEngine.Llm.Guards;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ToolEngine.Llm.Options;
using ToolEngine.Tools.Registry;

/// <summary>
/// Filters the tool list visible to the LLM based on <see cref="ToolGuardOptions"/>
/// configuration. Applied at two enforcement points in <c>AgentOrchestrator</c>:
///
/// <list type="number">
///   <item>
///     Before the schema is sent to the LLM provider — removes disallowed tools so
///     the model never selects them.
///   </item>
///   <item>
///     After the LLM returns a tool_use decision — re-validates the selected tool
///     before MediatR executes it (defence in depth against prompt injection).
///   </item>
/// </list>
/// </summary>
public sealed class ToolGuardFilter
{
    private readonly ToolGuardOptions             _opts;
    private readonly ILogger<ToolGuardFilter>     _logger;

    public ToolGuardFilter(
        IOptions<LlmOptions>        options,
        ILogger<ToolGuardFilter>    logger)
    {
        _opts   = options.Value.ToolGuard;
        _logger = logger;
    }

    /// <summary>
    /// Returns a filtered subset of <paramref name="tools"/> — only those permitted
    /// by the configured allowlist and denylist.
    /// When <see cref="ToolGuardOptions.Enabled"/> is <c>false</c>, returns the
    /// original list unchanged.
    /// </summary>
    public IReadOnlyList<ToolDescriptor> Filter(IReadOnlyList<ToolDescriptor> tools)
    {
        if (!_opts.Enabled)
            return tools;

        var permitted = tools.Where(t => IsPermitted(t.FullName)).ToList();

        var blocked = tools.Count - permitted.Count;
        if (blocked > 0)
            _logger.LogDebug(
                "ToolGuard filtered {Blocked}/{Total} tools from LLM schema.",
                blocked, tools.Count);

        return permitted;
    }

    /// <summary>
    /// Returns <c>true</c> if <paramref name="fullName"/> is permitted by the current
    /// guard configuration.
    /// <para>
    /// Deny overrides allow: a tool matching both lists is blocked.
    /// </para>
    /// </summary>
    public bool IsPermitted(string fullName)
    {
        if (!_opts.Enabled)
            return true;

        // Deny overrides allow
        if (MatchesAny(_opts.DeniedTools, fullName))
        {
            _logger.LogWarning(
                "ToolGuard blocked tool '{Tool}' — matched DeniedTools list.", fullName);
            return false;
        }

        // Empty allowlist = all tools permitted (only denylist active)
        if (_opts.AllowedTools.Count == 0)
            return true;

        if (MatchesAny(_opts.AllowedTools, fullName))
            return true;

        _logger.LogWarning(
            "ToolGuard blocked tool '{Tool}' — not in AllowedTools list.", fullName);
        return false;
    }

    // ── Pattern matching ──────────────────────────────────────────────────────────

    private static bool MatchesAny(IEnumerable<string> patterns, string fullName) =>
        patterns.Any(p => Matches(p, fullName));

    /// <summary>
    /// Matches a pattern against a tool full name (case-insensitive).
    /// <list type="bullet">
    ///   <item><c>"*"</c> — matches everything</item>
    ///   <item><c>"math.*"</c> — matches any tool whose namespace is <c>math</c></item>
    ///   <item><c>"math.calculate"</c> — exact full name</item>
    /// </list>
    /// </summary>
    internal static bool Matches(string pattern, string fullName)
    {
        if (pattern == "*")
            return true;

        // Namespace wildcard: "math.*" matches "math.calculate", "math.add", etc.
        if (pattern.EndsWith(".*", StringComparison.Ordinal))
        {
            var ns = pattern[..^2]; // strip ".*"
            return fullName.StartsWith(ns + ".", StringComparison.OrdinalIgnoreCase);
        }

        // Exact match
        return string.Equals(pattern, fullName, StringComparison.OrdinalIgnoreCase);
    }
}

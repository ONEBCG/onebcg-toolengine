namespace ToolEngine.Llm.Guards;

using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ToolEngine.Llm.Abstractions;
using ToolEngine.Llm.Models;
using ToolEngine.Llm.Options;
using ToolEngine.Tools.Registry;

/// <summary>
/// Pre-flight scope classifier — makes a single lightweight LLM call before the
/// main orchestration loop to determine whether the user's request is within the
/// domain of available tools.
///
/// <para>
/// This is the reliable enforcement layer. Unlike system-prompt-only approaches,
/// which a capable LLM often overrides with its training instinct to be helpful,
/// a dedicated classification call returns structured JSON that the orchestrator
/// can act on deterministically.
/// </para>
///
/// <para>
/// <b>Three outcomes:</b>
/// <list type="bullet">
///   <item>
///     <b>Fully in scope</b> — all parts of the request map to available tools.
///     The original text is passed to the main loop unchanged.
///   </item>
///   <item>
///     <b>Fully out of scope</b> — nothing in the request maps to any tool.
///     <see cref="AgentResult.OutOfScope"/> is returned immediately; no tool
///     loop is entered, no further LLM call is made.
///   </item>
///   <item>
///     <b>Mixed</b> — part of the request is in scope, part is not.
///     Only the in-scope portion is passed to the main loop. The out-of-scope
///     parts are recorded and appended as a note on the final reply.
///   </item>
/// </list>
/// </para>
///
/// <para>
/// <b>Fail-open:</b> if the classification call fails or returns unparseable JSON,
/// the request is treated as fully in scope. The system prompt rules in
/// <see cref="AgentScopeEnforcer"/> still apply as a secondary control.
/// </para>
/// </summary>
public sealed class AgentScopeClassifier
{
    private readonly ScopeGuardOptions            _opts;
    private readonly ILogger<AgentScopeClassifier> _logger;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public AgentScopeClassifier(
        IOptions<LlmOptions>           options,
        ILogger<AgentScopeClassifier>  logger)
    {
        _opts   = options.Value.ScopeGuard;
        _logger = logger;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Classifies the user's <paramref name="text"/> against the available
    /// <paramref name="tools"/> using a focused LLM call.
    /// Returns a <see cref="ScopeClassification"/> the orchestrator uses to
    /// decide whether to proceed, rewrite, or reject the request.
    /// </summary>
    public async Task<ScopeClassification> ClassifyAsync(
        string                          text,
        IReadOnlyList<ToolDescriptor>   tools,
        ILlmProvider                    provider,
        ProviderOptions                 providerOptions,
        CancellationToken               ct = default)
    {
        // Config gate — bypass entirely when ScopeGuard.Enabled = false
        if (!_opts.Enabled)
        {
            _logger.LogDebug("ScopeGuard is disabled — passing request through without classification.");
            return ScopeClassification.FullyInScope(text);
        }

        if (tools.Count == 0)
        {
            _logger.LogDebug("No tools available — rejecting request as fully out of scope.");
            return new ScopeClassification(
                true, null, [],
                "No tools are currently available. I'm unable to assist with any requests at this time.",
                LlmUsage.Zero);
        }

        var messages = BuildClassificationMessages(text, tools);

        // Cap output tokens for classification — the JSON response is ≤ 200 tokens.
        // Using the main-loop MaxTokens (2 048) would waste latency and cost.
        //
        // Temperature = 0.0 — deterministic output is critical for classification.
        // The JSON schema is strict and any sampling randomness risks invalid JSON or
        // inconsistent field values across retries. This override is intentional and
        // independent of whatever temperature the operator sets for main-loop calls.
        var classificationOpts = new ProviderOptions
        {
            Model          = providerOptions.Model,
            ApiKeyEnvVar   = providerOptions.ApiKeyEnvVar,
            BaseUrl        = providerOptions.BaseUrl,
            MaxTokens      = Math.Min(providerOptions.MaxTokens, 512),
            TimeoutSeconds = providerOptions.TimeoutSeconds,
            Temperature    = 0.0
        };

        try
        {
            var response = await provider.CompleteAsync(messages, [], classificationOpts, ct);

            if (response.StopReason == StopReason.Error || string.IsNullOrWhiteSpace(response.Content))
            {
                _logger.LogWarning(
                    "Scope classification call failed (StopReason={StopReason}). Failing open.",
                    response.StopReason);
                // Still surface whatever tokens were consumed so the session budget is accurate.
                return ScopeClassification.FailOpen(text) with { Usage = response.Usage };
            }

            return ParseClassificationResponse(response.Content, text, response.Usage);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Scope classification call threw an exception. Failing open.");
            return ScopeClassification.FailOpen(text);
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static IReadOnlyList<LlmMessage> BuildClassificationMessages(
        string                        text,
        IReadOnlyList<ToolDescriptor> tools)
    {
        var system = BuildClassificationSystemPrompt(tools);
        var user   = $"USER REQUEST: \"{text}\"";

        return
        [
            LlmMessage.System(system),
            LlmMessage.User(user)
        ];
    }

    private static string BuildClassificationSystemPrompt(IReadOnlyList<ToolDescriptor> tools)
    {
        var sb = new StringBuilder();

        // Critical: the output format instruction comes first so it is in the highest-weight
        // position of the system prompt. Models trained on RLHF weight early instructions
        // more heavily for format compliance.
        sb.AppendLine("OUTPUT FORMAT: Respond with ONLY a raw JSON object.");
        sb.AppendLine("No preamble. No explanation. No markdown. No code fences. Just JSON.");
        sb.AppendLine();
        sb.AppendLine("You are a tool scope classifier. Your job is to determine which parts of");
        sb.AppendLine("a user request can be fulfilled by the tools listed below.");
        sb.AppendLine();
        sb.AppendLine("AVAILABLE TOOLS:");

        foreach (var t in tools)
        {
            var m = t.Metadata;
            sb.AppendLine($"- {m.FullName}: {m.InputSchema.Description}");
            if (!string.IsNullOrWhiteSpace(m.InputSchema.WhenToUse))
                sb.AppendLine($"  Use when: {m.InputSchema.WhenToUse}");
            if (!string.IsNullOrWhiteSpace(m.InputSchema.WhenNotToUse))
                sb.AppendLine($"  NOT for: {m.InputSchema.WhenNotToUse}");
        }

        sb.AppendLine();
        sb.AppendLine("FIELD RULES:");
        sb.AppendLine("- isFullyOutOfScope: true only if NONE of the request maps to any tool.");
        sb.AppendLine("- inScopePortion: SHORT plain-language rephrasing of ONLY the tool-addressable");
        sb.AppendLine("  sub-requests. Do NOT mention tools, do NOT explain — just restate the question.");
        sb.AppendLine("  Set to null only when isFullyOutOfScope is true.");
        sb.AppendLine("- outOfScopeParts: array of SHORT plain-language descriptions of each");
        sb.AppendLine("  sub-request that no tool can address. Empty array when nothing is out of scope.");
        sb.AppendLine("- refusalMessage: one sentence stating what you CAN help with.");
        sb.AppendLine("  Set only when isFullyOutOfScope is true, otherwise null.");
        sb.AppendLine();
        sb.AppendLine("EXAMPLE — request: \"what is 2+2 and tell me about AWS\"");
        sb.AppendLine("{\"isFullyOutOfScope\":false,\"inScopePortion\":\"What is 2+2?\",\"outOfScopeParts\":[\"Tell me about AWS\"],\"refusalMessage\":null}");
        sb.AppendLine();
        sb.AppendLine("EXAMPLE — request: \"write me a poem\"");
        sb.AppendLine("{\"isFullyOutOfScope\":true,\"inScopePortion\":null,\"outOfScopeParts\":[\"Write a poem\"],\"refusalMessage\":\"I can only help with maths, weather lookups, and user profile queries.\"}");
        sb.AppendLine();
        sb.AppendLine("Now classify the user request below. Output JSON only:");

        return sb.ToString().TrimEnd();
    }

    private ScopeClassification ParseClassificationResponse(
        string   content,
        string   originalText,
        LlmUsage usage)
    {
        // Always log the raw response at Debug so operators can diagnose parse failures
        // without needing breakpoints. Set Serilog MinimumLevel.Override for this namespace
        // to Debug to see it in development.
        _logger.LogDebug(
            "Scope classifier raw response ({Length} chars, {Tokens} tokens): {Content}",
            content.Length,
            usage.TotalTokens,
            content.Length <= 800 ? content : content[..800] + "…");

        var json = ExtractJsonObject(content);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var isFullyOutOfScope = root.TryGetProperty("isFullyOutOfScope", out var oos)
                && oos.GetBoolean();

            var inScopePortion = root.TryGetProperty("inScopePortion", out var isp)
                && isp.ValueKind == JsonValueKind.String
                ? isp.GetString()
                : null;

            var outOfScopeParts = root.TryGetProperty("outOfScopeParts", out var osp)
                && osp.ValueKind == JsonValueKind.Array
                ? osp.EnumerateArray()
                      .Where(e => e.ValueKind == JsonValueKind.String)
                      .Select(e => e.GetString()!)
                      .ToArray()
                : Array.Empty<string>();

            var refusalMessage = root.TryGetProperty("refusalMessage", out var rm)
                && rm.ValueKind == JsonValueKind.String
                ? rm.GetString()
                : null;

            _logger.LogInformation(
                "Scope classification result: isFullyOutOfScope={IsOOS}, inScope='{InScope}', " +
                "outOfScope=[{OOS}], tokens={Tokens}",
                isFullyOutOfScope,
                inScopePortion ?? "(none)",
                string.Join(" | ", outOfScopeParts),
                usage.TotalTokens);

            // Sanity-check: not out-of-scope but no in-scope portion → classifier confused
            if (!isFullyOutOfScope && string.IsNullOrWhiteSpace(inScopePortion))
            {
                _logger.LogWarning(
                    "Classifier returned isFullyOutOfScope=false but inScopePortion is null or empty. " +
                    "Failing open so the request is not silently dropped.");
                return ScopeClassification.FailOpen(originalText) with { Usage = usage };
            }

            return new ScopeClassification(isFullyOutOfScope, inScopePortion, outOfScopeParts, refusalMessage, usage);
        }
        catch (JsonException ex)
        {
            // Log at Warning with the extracted candidate and the full raw response so
            // operators know exactly what the model returned and why parsing failed.
            _logger.LogWarning(
                ex,
                "Scope classification JSON parse failed. Failing open. " +
                "Extracted candidate: '{Candidate}' | Full raw response: '{Raw}'",
                json.Length <= 400 ? json : json[..400] + "…",
                content.Length <= 800 ? content : content[..800] + "…");
            return ScopeClassification.FailOpen(originalText) with { Usage = usage };
        }
    }

    /// <summary>
    /// Extracts the first well-formed JSON object from <paramref name="content"/>,
    /// handling all common LLM response patterns:
    /// <list type="bullet">
    ///   <item>Clean JSON: <c>{"key":"value"}</c></item>
    ///   <item>Markdown fences: <c>```json\n{...}\n```</c></item>
    ///   <item>Preamble text: <c>Sure! Here is the result:\n{...}</c></item>
    ///   <item>Postamble text: <c>{...}\n\nLet me know if you need more.</c></item>
    /// </list>
    /// Falls through and returns the trimmed input as-is if no braces are found,
    /// allowing <c>JsonDocument.Parse</c> to produce a diagnostic exception.
    /// </summary>
    private static string ExtractJsonObject(string content)
    {
        var text = content.Trim();

        // Strip markdown code fences first (``` or ```json or ```JSON)
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = text.IndexOf('\n');
            var lastFence    = text.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline > 0 && lastFence > firstNewline)
                text = text[(firstNewline + 1)..lastFence].Trim();
        }

        // Find the outermost JSON object boundaries.
        // LastIndexOf('}') correctly handles objects that contain nested objects.
        var objStart = text.IndexOf('{');
        var objEnd   = text.LastIndexOf('}');

        if (objStart >= 0 && objEnd > objStart)
            return text[objStart..(objEnd + 1)];

        // No braces found — return as-is so the caller's JsonException carries
        // the actual content for diagnostic logging.
        return text;
    }
}

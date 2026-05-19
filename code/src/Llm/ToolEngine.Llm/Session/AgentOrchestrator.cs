namespace ToolEngine.Llm.Session;

using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ToolEngine.Application.Commands;
using ToolEngine.Core.Domain.Constants;
using ToolEngine.Core.Domain.Enums;
using ToolEngine.Llm.Abstractions;
using ToolEngine.Llm.Conversion;
using ToolEngine.Llm.Guards;
using ToolEngine.Llm.Models;
using ToolEngine.Llm.Options;
using ToolEngine.Llm.Prompts;
using ToolEngine.Tools.Registry;

public sealed class AgentOrchestrator
{
    private readonly IAgentSessionStore         _sessionStore;
    private readonly IProviderRouter            _router;
    private readonly IToolRegistry              _registry;
    private readonly ToolSchemaConverter        _converter;
    private readonly ToolGuardFilter            _guardFilter;
    private readonly AgentScopeEnforcer         _scopeEnforcer;
    private readonly AgentScopeClassifier       _scopeClassifier;
    private readonly IPromptStore               _prompts;
    private readonly IMediator                  _mediator;
    private readonly BudgetOptions              _budget;
    private readonly ILogger<AgentOrchestrator> _logger;

    public AgentOrchestrator(
        IAgentSessionStore          sessionStore,
        IProviderRouter             router,
        IToolRegistry               registry,
        ToolSchemaConverter         converter,
        ToolGuardFilter             guardFilter,
        AgentScopeEnforcer          scopeEnforcer,
        AgentScopeClassifier        scopeClassifier,
        IPromptStore                prompts,
        IMediator                   mediator,
        IOptions<LlmOptions>        options,
        ILogger<AgentOrchestrator>  logger)
    {
        _sessionStore    = sessionStore;
        _router          = router;
        _registry        = registry;
        _converter       = converter;
        _guardFilter     = guardFilter;
        _scopeEnforcer   = scopeEnforcer;
        _scopeClassifier = scopeClassifier;
        _prompts         = prompts;
        _mediator        = mediator;
        _budget          = options.Value.Budget;
        _logger          = logger;
    }

    public async Task<AgentResult> RunAsync(
        Guid              correlationId,
        string            tenantId,
        string            userId,
        string            text,
        string?           sessionId,
        string?           tenantProviderOverride,
        CancellationToken ct)
    {
        var isSingleTurn = sessionId is null;
        // Acquire a per-session lock before reading/writing session state.
        // Prevents two concurrent requests on the same multi-turn session from
        // clobbering each other's message history (last-writer-wins data loss).
        var effectiveSessionId = sessionId ?? Guid.NewGuid().ToString();
        using var sessionLock  = await _sessionStore.AcquireSessionLockAsync(effectiveSessionId, ct);
        var session            = await _sessionStore.GetOrCreateAsync(sessionId, isSingleTurn, ct);

        // Tenant-scoped tool list — LLM only sees tools this tenant is allowed to use.
        // ToolGuardFilter (pre-LLM enforcement point 1/2): strips any tools not permitted
        // by the ToolGuard allowlist/denylist before the schema is sent to the provider.
        // The model never sees, and therefore cannot select, tools blocked at this point.
        var descriptors = _guardFilter.Filter(_registry.ListAll(tenantId));
        var toolDefs    = _converter.Convert(descriptors);

        var (provider, provOpts) = _router.Select(tenantProviderOverride);

        // Build a lookup for desanitizing tool names back to originals
        var toolLookup = toolDefs.ToDictionary(t => t.SanitizedName, t => t.OriginalFullName);

        // ── Pre-flight scope classification ───────────────────────────────────────
        // AgentScopeClassifier makes a dedicated lightweight LLM call BEFORE the
        // main loop. It returns structured JSON indicating which parts of the request
        // are tool-addressable and which are not. This is the primary enforcement
        // layer — more reliable than system-prompt-only approaches which capable LLMs
        // override with their training instinct to be helpful.
        //
        // Outcomes:
        //   Fully out of scope  → return AgentResult.OutOfScope immediately (no loop entered)
        //   Mixed               → only inScopePortion forwarded; outOfScopeParts noted
        //   Fully in scope      → original text forwarded unchanged
        //   Classification fail → fail-open (request passes through; system prompt still applies)
        var scopeCheck = await _scopeClassifier.ClassifyAsync(
            text, descriptors, provider, provOpts, ct);

        // Record classification tokens immediately — they represent real API cost and must
        // be included in the session total before the budget gate runs.
        // Without this, the budget check only sees main-loop tokens and under-reports cost;
        // sessions appear to have more headroom than they actually do.
        session.RecordUsage(scopeCheck.Usage);

        if (scopeCheck.IsFullyOutOfScope)
        {
            var refusal = scopeCheck.RefusalMessage
                ?? _prompts.Get(PromptKeys.AgentScopeDefaultRefusal);
            _logger.LogInformation(
                "Pre-flight classifier rejected request for session {SessionId} as fully out of scope. " +
                "Classification used {Tokens} tokens.",
                effectiveSessionId, scopeCheck.Usage.TotalTokens);
            return AgentResult.OutOfScope(refusal, effectiveSessionId, session.TotalUsage);
        }

        // Use only the in-scope portion for the main loop
        var effectiveText    = scopeCheck.InScopePortion ?? text;
        var outOfScopeParts  = scopeCheck.OutOfScopeParts;

        if (outOfScopeParts.Length > 0)
            _logger.LogInformation(
                "Pre-flight classifier trimmed {Count} out-of-scope portion(s) from request: [{Parts}]",
                outOfScopeParts.Length, string.Join(", ", outOfScopeParts));

        // ── System prompt injection ───────────────────────────────────────────────
        // Inject response-quality rules (missing params + response grounding) once
        // per session. Multi-turn sessions carry the prompt forward automatically.
        if (!session.Messages.Any(m => m.Role == MessageRole.System))
        {
            var systemPrompt = _scopeEnforcer.BuildSystemPrompt(descriptors);
            session.AddMessage(LlmMessage.System(systemPrompt));
            _logger.LogDebug(
                "System prompt injected for session {SessionId} ({ToolCount} tools).",
                effectiveSessionId, descriptors.Count);
        }

        session.AddMessage(LlmMessage.User(effectiveText));

        string?       lastToolInvoked = null;
        JsonElement?  lastToolResult  = null;

        for (int i = 0; i < _budget.MaxIterations; i++)
        {
            // Budget gate BEFORE calling LLM
            if (session.TokensUsed >= _budget.MaxTokensPerSession)
            {
                await _sessionStore.SaveAsync(session, ct);
                _logger.LogWarning("Session {SessionId} exceeded token budget.", session.SessionId);
                return AgentResult.BudgetExceeded(session.SessionId, session.TotalUsage);
            }

            _logger.LogDebug(
                "LLM call iteration {Iteration}, session {SessionId}, provider {Provider}",
                i + 1, session.SessionId, provider.ProviderName);

            var response = await provider.CompleteAsync(session.Messages, toolDefs, provOpts, ct);
            session.RecordUsage(response.Usage);

            if (response.StopReason == StopReason.Error)
            {
                await _sessionStore.SaveAsync(session, ct);
                return AgentResult.Failure(response.ErrorMessage ?? "LLM error", session.SessionId, session.TotalUsage);
            }

            if (response.StopReason == StopReason.EndTurn)
            {
                var content = response.Content ?? string.Empty;

                // If the pre-flight classifier trimmed out-of-scope parts from the
                // original request, append a note so the user knows what was discarded.
                if (outOfScopeParts.Length > 0)
                {
                    var note = outOfScopeParts.Length == 1
                        ? $"\n\nNote: \"{outOfScopeParts[0]}\" is outside the scope of what I can help with and was not processed."
                        : $"\n\nNote: The following parts of your request are outside the scope of what I can help with and were not processed: {string.Join("; ", outOfScopeParts.Select(p => $"\"{p}\""))}.";
                    content += note;
                }

                session.AddMessage(LlmMessage.Assistant(content));

                // When a tool was invoked, the reply should be predominantly derived from
                // the tool result. A reply disproportionately longer than the tool output
                // indicates the LLM supplemented with general knowledge. Log a warning so
                // operators can tune the grounding reminder or schedule a review.
                if (lastToolInvoked is not null)
                {
                    var lastToolMsg = session.Messages.LastOrDefault(
                        m => m.Role == MessageRole.Tool);
                    if (lastToolMsg?.Content is not null)
                    {
                        var toolLen  = lastToolMsg.Content.Length;
                        var replyLen = content.Length;
                        if (toolLen > 0 && replyLen > toolLen * ServiceLimits.GroundingLengthRatioWarningThreshold)
                            _logger.LogWarning(
                                "Grounding concern on session {SessionId}: reply ({ReplyLen} chars) " +
                                "is {Ratio}x the tool result ({ToolLen} chars) for '{Tool}'. " +
                                "Review for potential knowledge leakage beyond tool output.",
                                session.SessionId,
                                replyLen,
                                replyLen / toolLen,
                                toolLen,
                                lastToolInvoked);
                    }
                }

                if (!isSingleTurn) await _sessionStore.SaveAsync(session, ct);
                return AgentResult.Ok(
                    content,
                    session.SessionId,
                    session.TotalUsage,
                    lastToolInvoked,
                    lastToolResult);
            }

            if (response.StopReason == StopReason.ToolUse && response.ToolCall is not null)
            {
                session.AddMessage(LlmMessage.AssistantToolUse(response.ToolCall));

                var toolCallId   = response.ToolCall.Id;
                var sanitized    = response.ToolCall.ToolName;
                var originalName = toolLookup.TryGetValue(sanitized, out var orig)
                    ? orig
                    : ToolSchemaConverter.DesanitizeName(sanitized);

                // Parse namespace and name from "namespace.name"
                var dotIndex = originalName.IndexOf('.');
                var ns       = dotIndex > 0 ? originalName[..dotIndex]      : string.Empty;
                var name     = dotIndex > 0 ? originalName[(dotIndex + 1)..] : originalName;

                _logger.LogInformation(
                    "LLM selected tool {Tool} for session {SessionId}",
                    originalName, session.SessionId);

                // ToolGuardFilter (post-selection enforcement point 2/2): re-validates the
                // tool name returned by the LLM before executing it through MediatR.
                // Defence in depth against prompt-injection attacks where a malicious user
                // prompt tricks the model into "calling" a tool it was not shown in the schema.
                if (!_guardFilter.IsPermitted(originalName))
                {
                    _logger.LogWarning(
                        "ToolGuard blocked post-selection tool '{Tool}' for session {SessionId}. " +
                        "Possible prompt injection attempt.",
                        originalName, session.SessionId);

                    var errorResult = JsonSerializer.Serialize(new
                    {
                        error       = ErrorCodes.ToolGuardBlocked,
                        description = $"Tool '{originalName}' is not permitted by the current guard configuration."
                    });
                    session.AddMessage(LlmMessage.ToolResult(toolCallId, errorResult));
                    continue;
                }

                // Build governance metadata — recorded on ToolInvocationRecord for
                // EU AI Act traceability (which model made the call, in which session).
                var governance = JsonSerializer.Serialize(new
                {
                    provider  = provider.ProviderName,
                    model     = provOpts.Model,
                    sessionId = session.SessionId
                });

                // CallerType = AiAgent is NON-NEGOTIABLE — set here, never passed from outside.
                // This ensures the audit trail correctly identifies AI-originated calls
                // regardless of what a caller claims in the request body.
                var executeCmd = new ExecuteToolCommand<JsonElement, JsonElement>(
                    correlationId,
                    tenantId,
                    userId,
                    ToolName:               name,
                    ToolVersion:            "latest",
                    Input:                  response.ToolCall.Arguments,
                    ToolType:               ToolType.Logic,
                    ToolNamespace:          ns,
                    CallerType:             CallerType.AiAgent,
                    GovernanceMetadataJson: governance);

                var toolResponse = await _mediator.Send(executeCmd, ct);

                // Handle suspended (pending approval) case
                if (toolResponse.PendingInvocationId.HasValue)
                {
                    await _sessionStore.SaveAsync(session, ct);
                    return AgentResult.ToolPending(
                        toolResponse.PendingInvocationId.Value,
                        session.SessionId,
                        session.TotalUsage);
                }

                var resultJson = toolResponse.Success
                    ? JsonSerializer.Serialize(toolResponse.Data)
                    : JsonSerializer.Serialize(new
                    {
                        error       = toolResponse.Error?.Code,
                        description = toolResponse.Error?.Description
                    });

                lastToolInvoked = originalName;
                lastToolResult  = toolResponse.Success
                    ? JsonSerializer.Deserialize<JsonElement>(resultJson)
                    : default(JsonElement?);

                session.AddMessage(LlmMessage.ToolResult(toolCallId, resultJson));

                // Grounding constraint injection — injected as a User message immediately
                // after the tool result, before the LLM's summary generation step.
                // The model sees this in its live context window and is far less likely
                // to override it than a system prompt rule written at session start.
                // Industry reference: RAG grounding / faithfulness enforcement pattern.
                session.AddMessage(LlmMessage.User(_prompts.Get(PromptKeys.AgentGroundingReminder)));
                // Continue the loop — LLM will produce a grounded summary
            }
        }

        await _sessionStore.SaveAsync(session, ct);
        _logger.LogWarning("Session {SessionId} reached max iterations.", session.SessionId);
        return AgentResult.MaxIterations(session.SessionId, session.TotalUsage);
    }
}

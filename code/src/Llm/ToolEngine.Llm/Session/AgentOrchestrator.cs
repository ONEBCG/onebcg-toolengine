namespace ToolEngine.Llm.Session;

using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ToolEngine.Application.Commands;
using ToolEngine.Core.Domain.Enums;
using ToolEngine.Llm.Abstractions;
using ToolEngine.Llm.Conversion;
using ToolEngine.Llm.Models;
using ToolEngine.Llm.Options;
using ToolEngine.Tools.Registry;

public sealed class AgentOrchestrator
{
    private readonly IAgentSessionStore         _sessionStore;
    private readonly IProviderRouter            _router;
    private readonly IToolRegistry              _registry;
    private readonly ToolSchemaConverter        _converter;
    private readonly IMediator                  _mediator;
    private readonly BudgetOptions              _budget;
    private readonly ILogger<AgentOrchestrator> _logger;

    public AgentOrchestrator(
        IAgentSessionStore          sessionStore,
        IProviderRouter             router,
        IToolRegistry               registry,
        ToolSchemaConverter         converter,
        IMediator                   mediator,
        IOptions<LlmOptions>        options,
        ILogger<AgentOrchestrator>  logger)
    {
        _sessionStore = sessionStore;
        _router       = router;
        _registry     = registry;
        _converter    = converter;
        _mediator     = mediator;
        _budget       = options.Value.Budget;
        _logger       = logger;
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
        var session      = await _sessionStore.GetOrCreateAsync(sessionId, isSingleTurn, ct);

        // Tenant-scoped tool list — LLM only sees tools this tenant is allowed to use
        var descriptors = _registry.ListAll(tenantId);
        var toolDefs    = _converter.Convert(descriptors);

        var (provider, provOpts) = _router.Select(tenantProviderOverride);

        // Build a lookup for desanitizing tool names back to originals
        var toolLookup = toolDefs.ToDictionary(t => t.SanitizedName, t => t.OriginalFullName);

        session.AddMessage(LlmMessage.User(text));

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
                session.AddMessage(LlmMessage.Assistant(response.Content ?? string.Empty));
                if (!isSingleTurn) await _sessionStore.SaveAsync(session, ct);
                return AgentResult.Ok(
                    response.Content ?? string.Empty,
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

                // Build governance metadata — recorded on ToolInvocationRecord
                var governance = JsonSerializer.Serialize(new
                {
                    provider  = provider.ProviderName,
                    model     = provOpts.Model,
                    sessionId = session.SessionId
                });

                // Execute through the full MediatR pipeline (all behaviors apply).
                // CallerType = AiAgent is NON-NEGOTIABLE — set here, never passed from outside.
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
                // Continue the loop — LLM will summarize the tool result
            }
        }

        await _sessionStore.SaveAsync(session, ct);
        _logger.LogWarning("Session {SessionId} reached max iterations.", session.SessionId);
        return AgentResult.MaxIterations(session.SessionId, session.TotalUsage);
    }
}

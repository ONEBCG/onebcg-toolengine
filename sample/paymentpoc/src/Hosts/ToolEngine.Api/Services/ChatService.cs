using System.Text.Json;
using MediatR;
using ToolEngine.Application.Commands;
using ToolEngine.Core.Abstractions.Llm;
using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Core.Domain.Enums;
using ToolEngine.Tools.Abstractions.Interfaces;

namespace ToolEngine.Api.Services;

/// <summary>
/// Application-layer chat service.
/// Delegates the agentic loop entirely to ILlmProvider — provider selection
/// (Claude, OpenAI, or none) is driven by LLM:Provider in appsettings.
/// ChatService is responsible for:
///   - Building the LlmTool list from IToolRegistry
///   - Providing the executeTool callback that routes tool calls through MediatR
///   - Mapping LlmChatResponse → ChatResponse for the HTTP layer
/// </summary>
public sealed class ChatService
{
    // ── System prompt — two modes selectable via LLM:AutonomousToolSelection ───────

    /// <summary>
    /// Autonomous mode: context-only. The model reasons from tool descriptions
    /// and WhenToUse semantics to select and sequence tools without explicit guidance.
    /// Default — enables flexible, intent-driven tool use.
    /// </summary>
    private const string AutonomousSystemPrompt =
        "You are a payment processing assistant for ONE BCG. " +
        "Use the available payment tools to help users. " +
        "Each tool's description states exactly what inputs it needs and where they come from — " +
        "read WhenToUse carefully to determine the correct sequence and required inputs.\n\n" +

        "DEFAULT PAYER CONTEXT — use unless the user specifies otherwise:\n" +
        "  payerName=ONE BCG UK Ltd  payerJurisdiction=GB  payerEntityId=PAYER-ONEBCG-001\n" +
        "  initiatorId=chat-user  serviceType=ManagementConsulting\n\n" +

        "AVAILABLE PAYEES:\n" +
        "  Acme Consulting (PPM-001, active, GBP/USD/EUR, £250k cap)\n" +
        "  Horizon Advisory (PPM-002, EXPIRED — will block at Stage 2)\n" +
        "  Risq Capital (PPM-003, active — KYC blocks at Stage 4)\n\n" +

        "RESUME FLOW — when a user provides a PRID and says 'approved', 'resume', or wants to continue an existing payment:\n" +
        "  - NEVER call payment.initiate — the payment already exists in the system.\n" +
        "  - Ask only for the gross amount and currency to confirm (e.g. '5000 GBP').\n" +
        "  - Any amount and currency the user provides IS confirmation data, NOT a new payment request.\n" +
        "  - Immediately call payment.resume-payment-verify with the PRID, confirmedAmount, and confirmedCurrency.\n" +
        "  - On success, pass the verificationToken directly to payment.resume.\n\n" +

        "Only ask for clarification if genuinely ambiguous (unknown payee, missing amount).\n" +
        "ServiceType: 0=SoftwareLicense 1=CloudSaas 2=ManagementConsulting 3=InterestOnLoan 4=DividendDistribution 5=ContractStaffing 6=Other";

    /// <summary>
    /// Guided mode: includes explicit WORKFLOW section with prescribed call sequence.
    /// Use when strict step ordering is required or the model needs more direction.
    /// Enable via LLM:AutonomousToolSelection=false in appsettings.
    /// </summary>
    private const string GuidedSystemPrompt =
        "You are a payment processing assistant for ONE BCG. " +
        "Use the ToolEngine payment pipeline tools to help users check or process payments. " +
        "Briefly explain what you are doing before each tool call.\n\n" +

        "DEFAULT PAYER CONTEXT — use these values unless the user specifies otherwise:\n" +
        "  payerName:         ONE BCG UK Ltd\n" +
        "  payerJurisdiction: GB\n" +
        "  payerEntityId:     PAYER-ONEBCG-001\n" +
        "  initiatorId:       chat-user\n" +
        "  serviceType:       2  (ManagementConsulting)\n\n" +

        "SEEDED PAYEES AND CONTRACTS:\n" +
        "  Acme Consulting  — PPM-001 (active, GBP/USD/EUR, £250k cap)\n" +
        "  Horizon Advisory — PPM-002 (EXPIRED — will block at Stage 2)\n" +
        "  Risq Capital     — PPM-003 (active, USD/GBP, $100k cap — KYC will block at Stage 4)\n\n" +

        "WORKFLOW:\n" +
        "  - To process a payment: call payment.initiate first (creates the record, returns PRID),\n" +
        "    then use that PRID in payment.verify-payee, payment.ppm-check, etc.\n" +
        "  - Call tools directly using the defaults above when the user provides enough\n" +
        "    context (amount, payee, currency, PPM). Do NOT ask for fields that have defaults.\n" +
        "  - RESUME: when a user provides a PRID and says 'approved'/'resume', NEVER call\n" +
        "    payment.initiate. Ask only for the gross amount and currency to confirm.\n" +
        "    Call payment.resume-payment-verify with the PRID, confirmedAmount, confirmedCurrency, then payment.resume.\n" +
        "  - Only ask for clarification if genuinely ambiguous (e.g. unknown payee,\n" +
        "    missing amount, or conflicting instructions).\n" +
        "  - ServiceType codes: 0=SoftwareLicense 1=CloudSaas 2=ManagementConsulting 3=InterestOnLoan 4=DividendDistribution 5=ContractStaffing 6=Other";

    private readonly ILlmProvider  _provider;
    private readonly IToolRegistry _registry;
    private readonly ISender       _mediator;
    private readonly string        _systemPrompt;

    public ChatService(
        ILlmProvider provider,
        IToolRegistry registry,
        ISender mediator,
        Microsoft.Extensions.Options.IOptions<ToolEngine.Infrastructure.Llm.LlmOptions> llmOptions)
    {
        _provider     = provider;
        _registry     = registry;
        _mediator     = mediator;
        // Select prompt mode from config — defaults to autonomous (true)
        _systemPrompt = llmOptions.Value.AutonomousToolSelection
            ? AutonomousSystemPrompt
            : GuidedSystemPrompt;
    }

    public async Task<ChatResponse> SendAsync(string userMessage, CancellationToken ct)
    {
        var tools  = BuildLlmTools();
        var result = await _provider.ChatAsync(
            userMessage, tools, ExecuteToolAsync, _systemPrompt, ct: ct);

        if (!result.IsSuccess)
            return ChatResponse.ApiError(result.Error ?? "LLM provider returned an error.");

        var chatToolCalls = result.ToolCalls
            .Select(tc => new ChatToolCall(
                ToolName:   tc.ToolName,
                FullName:   tc.FullName,
                Input:      tc.Input,
                OutputJson: tc.OutputJson,
                Success:    tc.Success,
                Suspended:  tc.Suspended))
            .ToList();

        return ChatResponse.Success(result.Reply, chatToolCalls, result.InputTokens, result.OutputTokens);
    }

    /// <summary>
    /// Runs the agentic loop and calls <paramref name="onStream"/> for each
    /// <see cref="LlmStreamEvent"/> emitted by the provider (tool calls in real time).
    /// Used by the SSE streaming endpoint — returns the full response for the final "done" event.
    /// </summary>
    public async Task<ChatResponse> StreamAsync(
        string                     userMessage,
        Func<LlmStreamEvent, Task> onStream,
        CancellationToken          ct)
    {
        var tools  = BuildLlmTools();
        var result = await _provider.ChatAsync(
            userMessage, tools, ExecuteToolAsync, _systemPrompt, onStream, ct);

        if (!result.IsSuccess)
            return ChatResponse.ApiError(result.Error ?? "LLM provider returned an error.");

        var chatToolCalls = result.ToolCalls
            .Select(tc => new ChatToolCall(
                ToolName:   tc.ToolName,
                FullName:   tc.FullName,
                Input:      tc.Input,
                OutputJson: tc.OutputJson,
                Success:    tc.Success,
                Suspended:  tc.Suspended))
            .ToList();

        return ChatResponse.Success(result.Reply, chatToolCalls, result.InputTokens, result.OutputTokens);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private IReadOnlyList<LlmTool> BuildLlmTools() =>
        _registry.ListTools()
            .Select(t => new LlmTool(
                Name:        t.FullName,
                Description: $"{t.Schema.Description}\n\nUse when: {t.Schema.WhenToUse}\nDo not use when: {t.Schema.WhenNotToUse}",
                InputSchema: t.Schema.InputSchema))
            .ToList();

    /// <summary>
    /// Tool execution callback passed to ILlmProvider.
    /// Receives the tool name in provider wire format (e.g. payment__verify-payee)
    /// and routes it through the MediatR pipeline (Validation → Approval → Audit).
    /// Returns a JSON string: the tool's output, a suspension notice, or an error.
    /// </summary>
    private async Task<string> ExecuteToolAsync(string providerToolName, JsonElement input)
    {
        var (ns, name) = SplitProviderToolName(providerToolName);

        try
        {
            var response = await _mediator.Send(new ExecuteToolCommand(
                CorrelationId:  Guid.NewGuid(),
                ToolNamespace:  ns,
                ToolName:       name,
                ToolVersion:    "v1",
                Input:          input,
                UserId:         "chat-agent",
                CallerType:     CallerType.AiAgent,
                IdempotencyKey: null));

            if (response.IsSuspended)
                return "{ \"status\": \"SUSPENDED\", \"message\": \"This tool requires human approval. " +
                       "The request has been submitted and is pending review.\" }";

            if (response is ToolResponse<JsonElement> typed && typed.Success)
                return typed.Data.GetRawText();

            var errorDesc = response.Error?.Description ?? "Tool returned a failure response.";
            return $"{{ \"success\": false, \"error\": \"{errorDesc.Replace("\"", "'")}\" }}";
        }
        catch (Exception ex)
        {
            return $"{{ \"error\": \"{ex.Message.Replace("\"", "'")}\" }}";
        }
    }

    // payment__verify-payee → ("payment", "verify-payee")
    private static (string Namespace, string Name) SplitProviderToolName(string providerName)
    {
        var idx = providerName.IndexOf("__", StringComparison.Ordinal);
        return idx < 0
            ? (string.Empty, providerName)
            : (providerName[..idx], providerName[(idx + 2)..]);
    }
}

// ── Response models ───────────────────────────────────────────────────────────
// Kept here (not in Core) — they are HTTP-layer types specific to ChatController.

public sealed record ChatResponse
{
    public string                    Reply        { get; init; } = string.Empty;
    public IReadOnlyList<ChatToolCall> ToolCalls  { get; init; } = [];
    public bool                      IsSuccess    { get; init; }
    public string?                   Error        { get; init; }
    public int                       InputTokens  { get; init; }
    public int                       OutputTokens { get; init; }

    public static ChatResponse Success(string reply, IReadOnlyList<ChatToolCall> calls,
                                       int inputTokens = 0, int outputTokens = 0)
        => new() { Reply = reply, ToolCalls = calls, IsSuccess = true,
                   InputTokens = inputTokens, OutputTokens = outputTokens };

    public static ChatResponse NotConfigured()
        => new() { IsSuccess = false,
                   Error     = "Anthropic API key not configured. Set ANTHROPIC_API_KEY environment variable or Claude:ApiKey in appsettings.json." };

    public static ChatResponse ApiError(string error)
        => new() { IsSuccess = false, Error = error };
}

public sealed record ChatToolCall(
    string      ToolName,
    string      FullName,
    JsonElement Input,
    string      OutputJson,
    bool        Success,
    bool        Suspended);

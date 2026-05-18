namespace ToolEngine.Llm.Providers;

using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ToolEngine.Core.Abstractions.Security;
using ToolEngine.Llm.Abstractions;
using ToolEngine.Llm.Models;
using ToolEngine.Llm.Options;

public sealed class AnthropicLlmProvider : ILlmProvider
{
    public string ProviderName => "anthropic";

    private readonly IHttpClientFactory             _httpFactory;
    private readonly ISecretVault                   _secretVault;
    private readonly ILogger<AnthropicLlmProvider>  _logger;

    public AnthropicLlmProvider(
        IHttpClientFactory            httpFactory,
        ISecretVault                  secretVault,
        ILogger<AnthropicLlmProvider> logger)
    {
        _httpFactory  = httpFactory;
        _secretVault  = secretVault;
        _logger       = logger;
    }

    public async Task<LlmResponse> CompleteAsync(
        IReadOnlyList<LlmMessage>        messages,
        IReadOnlyList<LlmToolDefinition> tools,
        ProviderOptions                  options,
        CancellationToken                ct = default)
    {
        var apiKey = ResolveApiKey(options);
        if (apiKey is null)
            return new LlmResponse(StopReason.Error, null, null, LlmUsage.Zero, "Anthropic API key not configured.");

        // Build tools array
        var toolsArray = tools.Select(t =>
        {
            var schema = JsonDocument.Parse(t.InputSchemaJson).RootElement;
            return new { name = t.SanitizedName, description = t.Description, input_schema = schema };
        }).ToArray();

        // Build messages — Anthropic does not support role:system in messages array;
        // system content must go as the top-level "system" field.
        // Extract the injected system message (AgentScopeEnforcer) from the session so the
        // tool-scope rules, grounding constraints, and available-tool list all reach the model.
        // Previously this was hardcoded to a generic string which silently discarded the
        // AgentScopeEnforcer prompt — tools and scope rules were never sent.
        var systemContent  = ExtractSystemContent(messages);
        var anthropicMessages = BuildAnthropicMessages(messages);

        // Clamp temperature to Anthropic's valid range (0.0–1.0).
        var temperature = Math.Clamp(options.Temperature, 0.0, 1.0);

        var requestBody = new
        {
            model       = options.Model,
            max_tokens  = options.MaxTokens,
            temperature,
            system      = systemContent,
            messages    = anthropicMessages,
            tools       = toolsArray
        };

        var http    = _httpFactory.CreateClient("anthropic");
        var timeout = TimeSpan.FromSeconds(options.TimeoutSeconds > 0 ? options.TimeoutSeconds : 30);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
            request.Headers.Add("x-api-key", apiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");
            request.Content = JsonContent.Create(requestBody);

            using var response = await http.SendAsync(request, cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("Anthropic API error {Status}: {Body}", response.StatusCode, err);
                return new LlmResponse(StopReason.Error, null, null, LlmUsage.Zero, $"Anthropic API error: {response.StatusCode}");
            }

            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root  = doc.RootElement;
            var usage = ParseAnthropicUsage(root);

            // H12 — Anthropic occasionally returns HTTP 200 with an error envelope
            // (e.g. overloaded_error). Guard defensively rather than letting GetProperty throw.
            if (!root.TryGetProperty("stop_reason", out var stopReasonProp))
            {
                var errMsg = root.TryGetProperty("error", out var errProp)
                    ? errProp.TryGetProperty("message", out var msg) ? msg.GetString() : null
                    : null;
                _logger.LogError("Anthropic response missing stop_reason. Raw: {Body}", root.GetRawText());
                return new LlmResponse(StopReason.Error, null, null, usage,
                    $"Anthropic unexpected response: {errMsg ?? "missing stop_reason"}");
            }

            var stopReason = stopReasonProp.GetString();

            if (stopReason == "tool_use")
            {
                if (!root.TryGetProperty("content", out var content))
                    return new LlmResponse(StopReason.Error, null, null, usage, "Anthropic tool_use response missing content.");

                foreach (var item in content.EnumerateArray())
                {
                    if (!item.TryGetProperty("type", out var typeProp)) continue;
                    if (typeProp.GetString() != "tool_use") continue;

                    if (!item.TryGetProperty("id",    out var idProp)   ||
                        !item.TryGetProperty("name",  out var nameProp) ||
                        !item.TryGetProperty("input", out var inputProp))
                        continue;

                    return new LlmResponse(StopReason.ToolUse, null,
                        new LlmToolCall(idProp.GetString()!, nameProp.GetString()!, inputProp.Clone()), usage);
                }
            }

            // EndTurn — extract text
            var text = ExtractAnthropicText(root);
            return new LlmResponse(StopReason.EndTurn, text, null, usage);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new LlmResponse(StopReason.Error, null, null, LlmUsage.Zero, "Anthropic request timed out.");
        }
    }

    /// <summary>
    /// Extracts and concatenates all system-role messages into a single string for
    /// use as the Anthropic top-level <c>system</c> field.
    /// Returns a minimal fallback when no system message is present (e.g. test stubs).
    /// </summary>
    private static string ExtractSystemContent(IReadOnlyList<LlmMessage> messages)
    {
        var parts = messages
            .Where(m => m.Role == MessageRole.System && !string.IsNullOrWhiteSpace(m.Content))
            .Select(m => m.Content!.Trim())
            .ToList();

        return parts.Count > 0
            ? string.Join("\n\n", parts)
            : "You are a helpful assistant with access to tools. Use tools when appropriate.";
    }

    private static List<object> BuildAnthropicMessages(IReadOnlyList<LlmMessage> messages)
    {
        var result = new List<object>();
        foreach (var m in messages)
        {
            // System messages are handled as the top-level "system" field via ExtractSystemContent.
            // Anthropic's messages array must not contain role:system entries.
            if (m.Role == MessageRole.System) continue;

            if (m.Role == MessageRole.Tool && m.ToolCallId is not null)
            {
                // Tool results go as role:user with tool_result content
                result.Add(new
                {
                    role    = "user",
                    content = new[] { new { type = "tool_result", tool_use_id = m.ToolCallId, content = m.Content ?? "" } }
                });
            }
            else if (m.Role == MessageRole.Assistant && m.ToolCall is not null)
            {
                result.Add(new
                {
                    role    = "assistant",
                    content = new[] { new { type = "tool_use", id = m.ToolCall.Id, name = m.ToolCall.ToolName, input = m.ToolCall.Arguments } }
                });
            }
            else
            {
                result.Add(new { role = m.Role == MessageRole.Assistant ? "assistant" : "user", content = m.Content ?? "" });
            }
        }
        return result;
    }

    private static string? ExtractAnthropicText(JsonElement root)
    {
        if (!root.TryGetProperty("content", out var content)) return null;
        foreach (var item in content.EnumerateArray())
        {
            if (item.TryGetProperty("type", out var t) && t.GetString() == "text")
                return item.GetProperty("text").GetString();
        }
        return null;
    }

    private static LlmUsage ParseAnthropicUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var u)) return LlmUsage.Zero;
        var input  = u.TryGetProperty("input_tokens",  out var i) ? i.GetInt32() : 0;
        var output = u.TryGetProperty("output_tokens", out var o) ? o.GetInt32() : 0;
        // claude-sonnet-4-5 pricing: $3 / $15 per 1 M input / output tokens (2026).
        // Multipliers: input × 0.000003 = $/token, output × 0.000015 = $/token.
        var cost = (input * 0.000003m) + (output * 0.000015m);
        return new LlmUsage(input, output, cost);
    }

    private static string? ResolveApiKey(ProviderOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ApiKeyEnvVar))
            return System.Environment.GetEnvironmentVariable(options.ApiKeyEnvVar);
        return null;
    }
}

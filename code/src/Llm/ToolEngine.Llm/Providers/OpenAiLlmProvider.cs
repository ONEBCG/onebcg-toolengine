namespace ToolEngine.Llm.Providers;

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ToolEngine.Llm.Abstractions;
using ToolEngine.Llm.Models;
using ToolEngine.Llm.Options;

public class OpenAiLlmProvider : ILlmProvider
{
    public virtual string ProviderName => "openai";

    private readonly IHttpClientFactory          _httpFactory;
    private readonly ILogger<OpenAiLlmProvider>  _logger;

    // Subclasses (Ollama) can change these
    protected virtual string HttpClientName => "openai";
    protected virtual string BaseUrl        => "https://api.openai.com/v1/chat/completions";
    protected virtual bool   RequiresBearer => true;

    public OpenAiLlmProvider(IHttpClientFactory httpFactory, ILogger<OpenAiLlmProvider> logger)
    {
        _httpFactory = httpFactory;
        _logger      = logger;
    }

    public async Task<LlmResponse> CompleteAsync(
        IReadOnlyList<LlmMessage>        messages,
        IReadOnlyList<LlmToolDefinition> tools,
        ProviderOptions                  options,
        CancellationToken                ct = default)
    {
        var apiKey = ResolveApiKey(options);

        var toolsArray = tools.Select(t =>
        {
            var schema = JsonDocument.Parse(t.InputSchemaJson).RootElement;
            return new { type = "function", function = new { name = t.SanitizedName, description = t.Description, parameters = schema } };
        }).ToArray();

        var oaiMessages = BuildOpenAiMessages(messages);

        var requestBody = new
        {
            model       = options.Model,
            max_tokens  = options.MaxTokens,
            messages    = oaiMessages,
            tools       = toolsArray,
            tool_choice = "auto"
        };

        var http    = _httpFactory.CreateClient(HttpClientName);
        var timeout = TimeSpan.FromSeconds(options.TimeoutSeconds > 0 ? options.TimeoutSeconds : 30);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        try
        {
            var url = GetBaseUrl(options);
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            if (RequiresBearer && apiKey is not null)
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Content = JsonContent.Create(requestBody);

            using var response = await http.SendAsync(request, cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("{Provider} API error {Status}: {Body}", ProviderName, response.StatusCode, err);
                return new LlmResponse(StopReason.Error, null, null, LlmUsage.Zero, $"{ProviderName} API error: {response.StatusCode}");
            }

            using var doc   = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root         = doc.RootElement;
            var choices      = root.GetProperty("choices");
            var choice       = choices[0];
            var finishReason = choice.GetProperty("finish_reason").GetString();
            var message      = choice.GetProperty("message");
            var usage        = ParseOpenAiUsage(root);

            if (finishReason == "tool_calls")
            {
                var toolCalls = message.GetProperty("tool_calls");
                var first     = toolCalls[0];
                var id        = first.GetProperty("id").GetString()!;
                var fn        = first.GetProperty("function");
                var name      = fn.GetProperty("name").GetString()!;
                var argsJson  = fn.GetProperty("arguments").GetString() ?? "{}";
                var args      = JsonDocument.Parse(argsJson).RootElement;
                return new LlmResponse(StopReason.ToolUse, null, new LlmToolCall(id, name, args.Clone()), usage);
            }

            var text = message.TryGetProperty("content", out var c) ? c.GetString() : null;
            return new LlmResponse(StopReason.EndTurn, text, null, usage);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new LlmResponse(StopReason.Error, null, null, LlmUsage.Zero, $"{ProviderName} request timed out.");
        }
    }

    protected virtual string GetBaseUrl(ProviderOptions options) =>
        !string.IsNullOrWhiteSpace(options.BaseUrl)
            ? $"{options.BaseUrl.TrimEnd('/')}/chat/completions"
            : BaseUrl;

    protected static List<object> BuildOpenAiMessages(IReadOnlyList<LlmMessage> messages)
    {
        var result = new List<object>();
        foreach (var m in messages)
        {
            switch (m.Role)
            {
                case MessageRole.System:
                    result.Add(new { role = "system", content = m.Content ?? "" });
                    break;
                case MessageRole.User:
                    result.Add(new { role = "user", content = m.Content ?? "" });
                    break;
                case MessageRole.Assistant when m.ToolCall is not null:
                    result.Add(new
                    {
                        role       = "assistant",
                        tool_calls = new[] { new
                        {
                            id       = m.ToolCall.Id,
                            type     = "function",
                            function = new { name = m.ToolCall.ToolName, arguments = m.ToolCall.Arguments.GetRawText() }
                        }}
                    });
                    break;
                case MessageRole.Assistant:
                    result.Add(new { role = "assistant", content = m.Content ?? "" });
                    break;
                case MessageRole.Tool:
                    result.Add(new { role = "tool", tool_call_id = m.ToolCallId ?? "", content = m.Content ?? "" });
                    break;
            }
        }
        return result;
    }

    private static LlmUsage ParseOpenAiUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var u)) return LlmUsage.Zero;
        var input  = u.TryGetProperty("prompt_tokens",     out var i) ? i.GetInt32() : 0;
        var output = u.TryGetProperty("completion_tokens", out var o) ? o.GetInt32() : 0;
        // gpt-4o pricing (2026): $2.50 / $10 per 1 M input / output tokens.
        // gpt-4o-mini pricing:   $0.15 / $0.60 per 1 M input / output tokens.
        // Cost here is estimated for gpt-4o; actual cost differs for mini.
        // Both are acceptable for tool routing — mini is ~17× cheaper for CLI use.
        var cost = (input * 0.0000025m) + (output * 0.000010m);
        return new LlmUsage(input, output, cost);
    }

    private static string? ResolveApiKey(ProviderOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ApiKeyEnvVar))
            return System.Environment.GetEnvironmentVariable(options.ApiKeyEnvVar);
        return null;
    }
}

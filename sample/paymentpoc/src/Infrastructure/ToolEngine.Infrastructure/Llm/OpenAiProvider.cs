using System.Net.Http.Json;
using System.Text.Json;
using ToolEngine.Core.Abstractions.Llm;

namespace ToolEngine.Infrastructure.Llm;

/// <summary>
/// OpenAI implementation of ILlmProvider.
///
/// Wire format:
///   Tools  → { type: "function", function: { name, description, parameters } }
///   System → first message with role "system"
///   Output → choices[0].message with optional tool_calls array
///   History → assistant message with tool_calls,
///             then separate "tool" role messages per result
///   finish_reason: "stop" (done) | "tool_calls" (needs tool execution)
/// Tool names use "__" as namespace separator: payment.verify-payee → payment__verify-payee
/// </summary>
public sealed class OpenAiProvider : ILlmProvider
{
    public string ProviderName => "openai";

    private readonly HttpClient    _http;
    private readonly OpenAiOptions _opts;

    public OpenAiProvider(IHttpClientFactory httpFactory, OpenAiOptions opts)
    {
        _opts = opts;
        _http = httpFactory.CreateClient();
        _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {opts.ApiKey}");
    }

    public async Task<LlmChatResponse> ChatAsync(
        string                                  userMessage,
        IReadOnlyList<LlmTool>                 tools,
        Func<string, JsonElement, Task<string>> executeTool,
        string                                  systemPrompt,
        Func<LlmStreamEvent, Task>?            onStream = null,
        CancellationToken                       ct       = default)
    {
        var openAiTools = ToOpenAiTools(tools);
        var messages    = new List<object>
        {
            new { role = "system", content = systemPrompt },
            new { role = "user",   content = userMessage  },
        };
        var callLog     = new List<LlmToolCall>();
        var totalInput  = 0;
        var totalOutput = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var body = new { model = _opts.Model, tools = openAiTools, messages };

            using var req = new HttpRequestMessage(HttpMethod.Post, _opts.BaseUrl)
            {
                Content = JsonContent.Create(body),
            };

            using var resp = await _http.SendAsync(req, ct);
            var json       = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);

            if (!resp.IsSuccessStatusCode)
            {
                var msg = json.TryGetProperty("error", out var e) && e.TryGetProperty("message", out var m)
                    ? m.GetString() : resp.ReasonPhrase;
                return LlmChatResponse.Failure($"OpenAI {(int)resp.StatusCode}: {msg}");
            }

            if (json.TryGetProperty("usage", out var usage))
            {
                if (usage.TryGetProperty("prompt_tokens",     out var pt)) totalInput  += pt.GetInt32();
                if (usage.TryGetProperty("completion_tokens", out var ct2)) totalOutput += ct2.GetInt32();
            }

            var choice       = json.GetProperty("choices")[0];
            var message      = choice.GetProperty("message");
            var finishReason = choice.TryGetProperty("finish_reason", out var fr) ? fr.GetString() : null;

            if (finishReason == "stop" || !message.TryGetProperty("tool_calls", out var toolCalls))
            {
                var text = message.TryGetProperty("content", out var c) ? c.GetString() ?? string.Empty : string.Empty;
                return LlmChatResponse.Success(text, callLog, totalInput, totalOutput);
            }

            // Append assistant turn with tool_calls
            messages.Add(new
            {
                role       = "assistant",
                content    = message.TryGetProperty("content", out var mc) ? mc.GetString() : (string?)null,
                tool_calls = toolCalls,
            });

            // Execute each tool call and append a "tool" role message per result
            foreach (var toolCall in toolCalls.EnumerateArray())
            {
                var toolCallId = toolCall.GetProperty("id").GetString()!;
                var function   = toolCall.GetProperty("function");
                var toolName   = function.GetProperty("name").GetString()!;
                var argsStr    = function.GetProperty("arguments").GetString() ?? "{}";
                var toolInput  = JsonDocument.Parse(argsStr).RootElement;
                var (ns, name) = Split(toolName);

                // Emit tool_started event before execution
                if (onStream is not null)
                    await onStream(new ToolStartedEvent(toolName, toolInput));

                var (output, success, suspended) = await RunTool(executeTool, toolName, toolInput);

                // Emit tool_completed event after execution
                if (onStream is not null)
                    await onStream(new ToolCompletedEvent(toolName, output, success, suspended));

                callLog.Add(new LlmToolCall(toolName, $"{ns}.{name}", toolInput, output, success, suspended));

                // OpenAI tool result: a separate "tool" role message
                messages.Add(new { role = "tool", tool_call_id = toolCallId, content = output });
            }
        }
    }

    private static IReadOnlyList<object> ToOpenAiTools(IReadOnlyList<LlmTool> tools) =>
        tools.Select(t => (object)new
        {
            type     = "function",
            function = new
            {
                name        = t.Name.Replace(".", "__"),
                description = t.Description,
                parameters  = t.InputSchema,
            },
        }).ToList();

    private static (string Ns, string Name) Split(string name)
    {
        var idx = name.IndexOf("__", StringComparison.Ordinal);
        return idx < 0 ? (string.Empty, name) : (name[..idx], name[(idx + 2)..]);
    }

    private static async Task<(string Output, bool Success, bool Suspended)> RunTool(
        Func<string, JsonElement, Task<string>> executeTool,
        string toolName, JsonElement input)
    {
        try
        {
            var output    = await executeTool(toolName, input);
            var suspended = output.Contains("\"status\": \"SUSPENDED\"");
            var failed    = output.Contains("\"success\": false");
            return (output, !failed && !suspended, suspended);
        }
        catch (Exception ex)
        {
            return ($"{{\"error\":\"{ex.Message.Replace("\"", "'")}\"}}",
                    false, false);
        }
    }
}

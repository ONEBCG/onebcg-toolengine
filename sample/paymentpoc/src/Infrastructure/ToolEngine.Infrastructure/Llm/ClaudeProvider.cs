using System.Net.Http.Json;
using System.Text.Json;
using ToolEngine.Core.Abstractions.Llm;

namespace ToolEngine.Infrastructure.Llm;

/// <summary>
/// Anthropic Claude implementation of ILlmProvider.
///
/// Wire format:
///   Tools  → { name, description, input_schema }
///   Output → content[] with type "text" | "tool_use"
///   History → assistant turn with raw content array,
///             then user turn with tool_result blocks
/// Tool names use "__" as namespace separator: payment.verify-payee → payment__verify-payee
/// </summary>
public sealed class ClaudeProvider : ILlmProvider
{
    public string ProviderName => "claude";

    // anthropic-version is an API protocol constant, not a user-configurable setting.
    // It specifies the Claude Messages API schema version — not the model version.
    // Only update this when Anthropic releases a new API schema that requires code changes.
    private const string AnthropicApiVersion = "2023-06-01";

    private readonly HttpClient    _http;
    private readonly ClaudeOptions _opts;
    private readonly bool          _streaming;

    public ClaudeProvider(IHttpClientFactory httpFactory, ClaudeOptions opts, bool streaming = true)
    {
        _opts      = opts;
        _streaming = streaming;
        _http      = httpFactory.CreateClient();
        _http.DefaultRequestHeaders.Add("x-api-key",         opts.ApiKey);
        _http.DefaultRequestHeaders.Add("anthropic-version", AnthropicApiVersion);
    }

    public async Task<LlmChatResponse> ChatAsync(
        string                                  userMessage,
        IReadOnlyList<LlmTool>                 tools,
        Func<string, JsonElement, Task<string>> executeTool,
        string                                  systemPrompt,
        Func<LlmStreamEvent, Task>?            onStream = null,
        CancellationToken                       ct       = default)
    {
        var claudeTools  = ToClaudeTools(tools);
        var messages     = new List<object> { new { role = "user", content = userMessage } };
        var callLog      = new List<LlmToolCall>();
        var totalInput   = 0;
        var totalOutput  = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var body = new
            {
                model      = _opts.Model,
                max_tokens = 4096,
                system     = systemPrompt,
                tools      = claudeTools,
                messages,
            };

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
                return LlmChatResponse.Failure($"Claude {(int)resp.StatusCode}: {msg}");
            }

            if (json.TryGetProperty("usage", out var usage))
            {
                if (usage.TryGetProperty("input_tokens",  out var it)) totalInput  += it.GetInt32();
                if (usage.TryGetProperty("output_tokens", out var ot)) totalOutput += ot.GetInt32();
            }

            var content    = json.GetProperty("content");
            var stopReason = json.TryGetProperty("stop_reason", out var sr) ? sr.GetString() : null;

            var textParts  = new List<string>();
            var toolBlocks = new List<JsonElement>();

            foreach (var block in content.EnumerateArray())
            {
                var type = block.GetProperty("type").GetString();
                if (type == "text")          textParts.Add(block.GetProperty("text").GetString() ?? string.Empty);
                else if (type == "tool_use") toolBlocks.Add(block);
            }

            if (stopReason == "end_turn" || toolBlocks.Count == 0)
                return LlmChatResponse.Success(string.Join("\n", textParts), callLog, totalInput, totalOutput);

            // Append full assistant turn (text + tool_use) to conversation
            messages.Add(new { role = "assistant", content });

            // Execute each tool and collect results
            var toolResults = new List<object>();
            foreach (var block in toolBlocks)
            {
                var toolUseId  = block.GetProperty("id").GetString()!;
                var toolName   = block.GetProperty("name").GetString()!;
                var toolInput  = block.GetProperty("input");
                var (ns, name) = Split(toolName);

                // Emit tool_started event before execution (enables real-time streaming to browser)
                if (_streaming && onStream is not null)
                    await onStream(new ToolStartedEvent(toolName, toolInput));

                var (output, success, suspended) = await RunTool(executeTool, toolName, toolInput);

                // Emit tool_completed event after execution
                if (_streaming && onStream is not null)
                    await onStream(new ToolCompletedEvent(toolName, output, success, suspended));

                callLog.Add(new LlmToolCall(toolName, $"{ns}.{name}", toolInput, output, success, suspended));
                toolResults.Add(new { type = "tool_result", tool_use_id = toolUseId, content = output });
            }

            messages.Add(new { role = "user", content = toolResults });
        }
    }

    private static IReadOnlyList<object> ToClaudeTools(IReadOnlyList<LlmTool> tools) =>
        tools.Select(t => (object)new
        {
            name         = t.Name.Replace(".", "__"),
            description  = t.Description,
            input_schema = t.InputSchema,
        }).ToList();

    // payment__verify-payee → ("payment", "verify-payee")
    private static (string Ns, string Name) Split(string claudeName)
    {
        var idx = claudeName.IndexOf("__", StringComparison.Ordinal);
        return idx < 0 ? (string.Empty, claudeName) : (claudeName[..idx], claudeName[(idx + 2)..]);
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

using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ToolEngine.Core.Abstractions.Llm;

namespace ToolEngine.Infrastructure.Llm;

/// <summary>
/// Google Gemini implementation of ILlmProvider.
///
/// Wire format (verified against Gemini REST API, June 2025):
///   Endpoint  → POST https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}
///   Auth      → query param ?key=GOOGLE_API_KEY  (no header auth on v1beta REST)
///   Tools     → top-level "tools" array containing "functionDeclarations" list
///               { name, description, parameters }  — OpenAPI 3.0 subset schema
///   History   → "contents" array of { role: "user"|"model", parts: [...] }
///               NO "function" role — tool results go back under the "user" role
///   Tool call → candidates[0].content.parts[].functionCall  { name, args }
///   Tool resp → user-role part: { functionResponse: { name, response: { content: "..." } } }
///   Tokens    → usageMetadata.{ promptTokenCount, candidatesTokenCount }
///   Stop      → candidates[0].finishReason == "STOP"
///
/// Tool names use "__" as namespace separator: payment.verify-payee → payment__verify-payee
///
/// Sources:
///   https://ai.google.dev/api/generate-content
///   https://ai.google.dev/gemini-api/docs/function-calling
///   https://github.com/google-gemini/cookbook/blob/main/quickstarts/rest/Function_calling_REST.ipynb
/// </summary>
public sealed class GeminiProvider : ILlmProvider
{
    public string ProviderName => "gemini";

    private readonly HttpClient    _http;
    private readonly GeminiOptions _opts;
    private readonly bool          _streaming;

    public GeminiProvider(IHttpClientFactory httpFactory, GeminiOptions opts, bool streaming = true)
    {
        _opts      = opts;
        _streaming = streaming;
        _http      = httpFactory.CreateClient();
        // Auth is via ?key= query param on each request — no default headers needed
    }

    public async Task<LlmChatResponse> ChatAsync(
        string                                  userMessage,
        IReadOnlyList<LlmTool>                 tools,
        Func<string, JsonElement, Task<string>> executeTool,
        string                                  systemPrompt,
        Func<LlmStreamEvent, Task>?            onStream = null,
        CancellationToken                       ct       = default)
    {
        var geminiTools = ToGeminiTools(tools);
        var callLog     = new List<LlmToolCall>();
        var totalInput  = 0;
        var totalOutput = 0;

        // Gemini message history: contents array of { role, parts[] }
        // Initial user message
        var contents = new List<object>
        {
            new { role = "user", parts = new[] { new { text = userMessage } } }
        };

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var body = new
            {
                contents,
                systemInstruction = new
                {
                    parts = new[] { new { text = systemPrompt } }
                },
                tools = geminiTools,
                generationConfig = new
                {
                    maxOutputTokens = 4096,
                    temperature     = 0.2,
                },
            };

            var url = BuildUrl(_opts.BaseUrl, _opts.Model, _opts.ApiKey);
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(body),
            };

            using var resp = await _http.SendAsync(req, ct);
            var json       = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);

            if (!resp.IsSuccessStatusCode)
            {
                var msg = TryGetErrorMessage(json) ?? resp.ReasonPhrase;
                return LlmChatResponse.Failure($"Gemini {(int)resp.StatusCode}: {msg}");
            }

            // Accumulate token counts from usageMetadata
            if (json.TryGetProperty("usageMetadata", out var usage))
            {
                if (usage.TryGetProperty("promptTokenCount",     out var pt)) totalInput  += pt.GetInt32();
                if (usage.TryGetProperty("candidatesTokenCount", out var ct2)) totalOutput += ct2.GetInt32();
            }

            // Extract first candidate
            if (!json.TryGetProperty("candidates", out var candidates) ||
                candidates.GetArrayLength() == 0)
                return LlmChatResponse.Failure("Gemini returned no candidates.");

            var candidate    = candidates[0];
            var finishReason = candidate.TryGetProperty("finishReason", out var fr)
                ? fr.GetString() : null;

            if (!candidate.TryGetProperty("content", out var candidateContent))
                return LlmChatResponse.Failure("Gemini candidate has no content.");

            var parts = candidateContent.TryGetProperty("parts", out var p)
                ? p : default;

            // Collect text and function-call parts
            var textParts      = new List<string>();
            var functionCalls  = new List<JsonElement>();

            if (parts.ValueKind == JsonValueKind.Array)
            {
                foreach (var part in parts.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var textProp))
                        textParts.Add(textProp.GetString() ?? string.Empty);
                    else if (part.TryGetProperty("functionCall", out _))
                        functionCalls.Add(part);
                }
            }

            // No function calls — conversation complete.
            // Do NOT gate on finishReason == "STOP" here: Gemini 2.5 Flash returns
            // finishReason = "STOP" alongside functionCall parts in some response patterns.
            // Using finishReason as a stop signal would silently skip tool execution and
            // suppress all tool_started / tool_completed SSE events.
            // The presence of functionCall parts is the only reliable signal.
            if (functionCalls.Count == 0)
                return LlmChatResponse.Success(
                    string.Join("\n", textParts), callLog, totalInput, totalOutput);

            // Append model turn to history (Gemini requires the raw content object)
            contents.Add(new
            {
                role  = "model",
                parts = BuildModelParts(textParts, functionCalls),
            });

            // Execute each tool and build functionResponse parts for the next user turn
            var responseParts = new List<object>();

            foreach (var part in functionCalls)
            {
                var fc       = part.GetProperty("functionCall");
                var toolName = fc.GetProperty("name").GetString()!;
                var toolArgs = fc.GetProperty("args");
                var (ns, name) = Split(toolName);

                if (_streaming && onStream is not null)
                    await onStream(new ToolStartedEvent(toolName, toolArgs));

                var (output, success, suspended) = await RunTool(executeTool, toolName, toolArgs);

                if (_streaming && onStream is not null)
                    await onStream(new ToolCompletedEvent(toolName, output, success, suspended));

                callLog.Add(new LlmToolCall(toolName, $"{ns}.{name}", toolArgs, output, success, suspended));

                // Build functionResponse part — response must be a JSON object
                var responseObj = TryParseJson(output) is { } parsed
                    ? (object)new { functionResponse = new { name = toolName, response = parsed } }
                    : (object)new { functionResponse = new { name = toolName, response = new { content = output } } };

                responseParts.Add(responseObj);
            }

            // Append all tool results under a single user turn (Gemini requirement)
            contents.Add(new { role = "user", parts = responseParts });
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Build the generateContent URL.
    /// Gemini REST auth uses ?key= query param — no Authorization header on v1beta.
    /// </summary>
    private static string BuildUrl(string baseUrl, string model, string apiKey)
    {
        var trimmed = baseUrl.TrimEnd('/');
        return $"{trimmed}/models/{model}:generateContent?key={Uri.EscapeDataString(apiKey)}";
    }

    /// <summary>
    /// Convert provider-agnostic LlmTool list to Gemini tool format.
    /// Gemini: top-level "tools" array, each element has "functionDeclarations" list.
    /// Tool names: dots replaced with double-underscore (payment.verify-payee → payment__verify-payee).
    /// Parameters are passed as-is — Gemini accepts the same OpenAPI 3.0 subset JSON Schema.
    /// </summary>
    private static IReadOnlyList<object> ToGeminiTools(IReadOnlyList<LlmTool> tools)
    {
        if (tools.Count == 0) return Array.Empty<object>();

        var declarations = tools.Select(t => (object)new
        {
            name        = t.Name.Replace(".", "__"),
            description = t.Description,
            parameters  = t.InputSchema,
        }).ToList();

        // All declarations go into one Tool wrapper (single-element tools array)
        return new[] { (object)new { functionDeclarations = declarations } };
    }

    /// <summary>
    /// Build the model-turn parts list from text strings and raw functionCall part elements.
    /// Preserves the original JSON structure Gemini returned so it can be echoed back exactly.
    /// </summary>
    private static List<object> BuildModelParts(
        IEnumerable<string>      textParts,
        IEnumerable<JsonElement> functionCallParts)
    {
        var result = new List<object>();

        foreach (var t in textParts)
            if (!string.IsNullOrWhiteSpace(t))
                result.Add(new { text = t });

        foreach (var fc in functionCallParts)
        {
            // Re-serialize the functionCall part so it round-trips correctly
            var inner = fc.GetProperty("functionCall");
            result.Add(new
            {
                functionCall = new
                {
                    name = inner.GetProperty("name").GetString()!,
                    args = inner.GetProperty("args"),
                }
            });
        }

        return result;
    }

    // payment__verify-payee → ("payment", "verify-payee")
    private static (string Ns, string Name) Split(string geminiName)
    {
        var idx = geminiName.IndexOf("__", StringComparison.Ordinal);
        return idx < 0 ? (string.Empty, geminiName) : (geminiName[..idx], geminiName[(idx + 2)..]);
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

    /// <summary>
    /// Attempt to parse a JSON string into a JsonElement for structured embedding.
    /// Returns null if the string is not valid JSON — caller falls back to { content: raw }.
    /// </summary>
    private static JsonElement? TryParseJson(string json)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetErrorMessage(JsonElement json)
    {
        // Gemini error shape: { "error": { "code": 400, "message": "...", "status": "..." } }
        if (json.TryGetProperty("error", out var err))
        {
            if (err.TryGetProperty("message", out var msg)) return msg.GetString();
            if (err.ValueKind == JsonValueKind.String)       return err.GetString();
        }
        return null;
    }
}

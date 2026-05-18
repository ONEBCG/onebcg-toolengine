namespace ToolEngine.Api.Streaming;

using System.Text.Json;
using ToolEngine.Core.Domain.Contracts;

public static class SseWriter
{
    private static readonly JsonSerializerOptions _json = new()
        { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static async Task WriteChunkAsync<T>(
        HttpResponse           response,
        ToolChunk<T>           chunk,
        CancellationToken      ct)
    {
        var eventType = chunk.IsFinal ? "done" : "chunk";
        var data      = JsonSerializer.Serialize(chunk, _json);

        await response.WriteAsync($"event: {eventType}\n", ct);
        await response.WriteAsync($"data: {data}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }

    public static async Task WriteEventAsync(
        HttpResponse      response,
        string            eventName,
        string            data,
        CancellationToken ct)
    {
        await response.WriteAsync($"event: {eventName}\n", ct);
        await response.WriteAsync($"data: {data}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }

    public static async Task WriteErrorAsync(
        HttpResponse      response,
        string            message,
        CancellationToken ct)
    {
        // C6 — serialize via STJ so quotes, backslashes, and newlines in exception
        // messages are properly escaped. Raw interpolation broke the SSE JSON frame
        // and could poison the stream or allow SSE event injection.
        var payload = JsonSerializer.Serialize(new { error = message }, _json);
        await response.WriteAsync($"event: error\n", ct);
        await response.WriteAsync($"data: {payload}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }
}

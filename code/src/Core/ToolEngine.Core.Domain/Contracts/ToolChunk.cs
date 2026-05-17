namespace ToolEngine.Core.Domain.Contracts;

/// <summary>A single unit of a streaming response. Emitted via IAsyncEnumerable.</summary>
public sealed record ToolChunk<TContent>(
    Guid     CorrelationId,
    TContent Content,
    int      Index,
    bool     IsFinal   = false,
    string?  EventType = "chunk");   // "start" | "chunk" | "done"

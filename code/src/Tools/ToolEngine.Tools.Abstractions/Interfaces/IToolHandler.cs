namespace ToolEngine.Tools.Abstractions.Interfaces;

using ToolEngine.Core.Domain.Contracts;

/// <summary>
/// Execution contract. Every tool registered with ToolRegistry must implement this.
/// TInput and TOutput are strongly typed per tool — serialization is the caller's
/// responsibility.
/// </summary>
public interface IToolHandler<TInput, TOutput> : ITool
{
    /// <summary>Execute the tool and return a complete response.</summary>
    Task<ToolResponse<TOutput>> ExecuteAsync(
        ToolRequest<TInput> request,
        CancellationToken   ct = default);

    /// <summary>
    /// Execute the tool and emit results as an async stream of chunks.
    /// For non-streaming tools, the default implementation wraps ExecuteAsync
    /// as a single IsFinal=true chunk.
    /// </summary>
    IAsyncEnumerable<ToolChunk<TOutput>> StreamAsync(
        ToolRequest<TInput> request,
        CancellationToken   ct = default);
}

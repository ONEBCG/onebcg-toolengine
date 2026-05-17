namespace ToolEngine.Tools.Abstractions.Base;

using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Core.Domain.Enums;
using ToolEngine.Core.Domain.Schema;
using ToolEngine.Tools.Abstractions.Interfaces;

/// <summary>
/// Base for pure-computation tools. No I/O, no credentials.
/// Subclass implements only ExecuteAsync.
/// StreamAsync is auto-provided as a single-chunk wrapper.
/// </summary>
public abstract class LogicToolBase<TInput, TOutput>
    : IToolHandler<TInput, TOutput>
{
    public abstract string    Namespace    { get; }
    public abstract string    Name         { get; }
    public          string    FullName     => $"{Namespace}.{Name}";
    public abstract string    Version      { get; }
    public abstract string    Description  { get; }
    public          ToolType  Type         => ToolType.Logic;
    public abstract ToolSchema InputSchema  { get; }
    public abstract ToolSchema OutputSchema { get; }

    public abstract Task<ToolResponse<TOutput>> ExecuteAsync(
        ToolRequest<TInput> request,
        CancellationToken   ct = default);

    public async IAsyncEnumerable<ToolChunk<TOutput>> StreamAsync(
        ToolRequest<TInput> request,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken ct = default)
    {
        var response = await ExecuteAsync(request, ct);
        if (response.Success)
            yield return new ToolChunk<TOutput>(
                response.CorrelationId, response.Data!, 0, IsFinal: true, "done");
    }
}

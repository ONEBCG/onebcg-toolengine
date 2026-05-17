namespace ToolEngine.Tools.Abstractions.Base;

using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Core.Domain.Enums;
using ToolEngine.Core.Domain.Schema;
using ToolEngine.Tools.Abstractions.Interfaces;

/// <summary>
/// Injected executor abstraction (implemented in Phase 3 ToolEngine.Tools.Executor).
/// Declared here to avoid a circular dependency.
/// </summary>
public interface IToolExecutor
{
    Task<ToolResponse<TOutput>> ExecuteAsync<TInput, TOutput>(
        ToolRequest<TInput> request, CancellationToken ct = default);
}

/// <summary>
/// Base for tools that orchestrate child tools.
/// Uses IToolExecutor to run child tools — never instantiates them with new.
/// </summary>
public abstract class CompositeToolBase<TInput, TOutput>
    : IToolHandler<TInput, TOutput>
{
    private readonly IToolExecutor _executor;

    protected CompositeToolBase(IToolExecutor executor) =>
        _executor = executor;

    public abstract string    Namespace    { get; }
    public abstract string    Name         { get; }
    public          string    FullName     => $"{Namespace}.{Name}";
    public abstract string    Version      { get; }
    public abstract string    Description  { get; }
    public          ToolType  Type         => ToolType.Composite;
    public abstract ToolSchema InputSchema  { get; }
    public abstract ToolSchema OutputSchema { get; }

    /// <summary>
    /// Invoke a child tool by namespace + name + version.
    /// Preferred overload — namespace.name is the canonical routing key.
    /// </summary>
    protected Task<ToolResponse<TChildOut>> InvokeAsync<TChildIn, TChildOut>(
        string            tenantId,
        string            childNamespace,
        string            childName,
        string            childVersion,
        TChildIn          childInput,
        CancellationToken ct = default) =>
        _executor.ExecuteAsync<TChildIn, TChildOut>(
            new ToolRequest<TChildIn>(
                Guid.NewGuid(), tenantId,
                ToolName:      childName,
                ToolVersion:   childVersion,
                Input:         childInput,
                ToolNamespace: childNamespace), ct);

    /// <summary>
    /// Invoke a child tool by pre-composed fullName, e.g. "weather.current".
    /// Splits on first dot to populate ToolNamespace + ToolName separately.
    /// </summary>
    protected Task<ToolResponse<TChildOut>> InvokeAsync<TChildIn, TChildOut>(
        string            tenantId,
        string            childFullName,
        string            childVersion,
        TChildIn          childInput,
        CancellationToken ct = default)
    {
        var dot = childFullName.IndexOf('.');
        var (ns, name) = dot > 0
            ? (childFullName[..dot], childFullName[(dot + 1)..])
            : ("",                   childFullName);

        return InvokeAsync<TChildIn, TChildOut>(tenantId, ns, name, childVersion, childInput, ct);
    }

    public abstract Task<ToolResponse<TOutput>> ExecuteAsync(
        ToolRequest<TInput> request, CancellationToken ct = default);

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

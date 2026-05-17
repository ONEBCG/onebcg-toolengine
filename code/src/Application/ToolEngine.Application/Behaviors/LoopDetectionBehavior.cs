namespace ToolEngine.Application.Behaviors;

using System.Collections.Concurrent;
using MediatR;
using Microsoft.Extensions.Options;
using ToolEngine.Application.Abstractions;
using ToolEngine.Core.Domain.Contracts;

/// <summary>
/// Detects agent-driven tool call loops within a single correlation context.
/// A correlation represents one agent turn; if the same tool is invoked more
/// than MaxCallsPerCorrelation times the circuit opens and an error is returned.
///
/// State is in-process. For distributed deployments replace the static dict
/// with a distributed cache (Redis) keyed on correlationId + toolFullName.
/// </summary>
public sealed class LoopDetectionOptions
{
    public int MaxCallsPerCorrelation { get; set; } = 10;
}

public sealed class LoopDetectionBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    // Process-scoped — correlationId naturally bounds the detection window.
    private static readonly ConcurrentDictionary<string, int> _counter = new();

    private readonly LoopDetectionOptions _options;

    public LoopDetectionBehavior(IOptions<LoopDetectionOptions> options)
        => _options = options.Value;

    public async Task<TResponse> Handle(
        TRequest                          request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken                 ct)
    {
        if (request is not IExecuteToolCommand cmd)
            return await next();

        var key   = $"{cmd.CorrelationId}:{cmd.ToolNamespace}.{cmd.ToolName}";
        var count = _counter.AddOrUpdate(key, 1, (_, v) => v + 1);

        if (count > _options.MaxCallsPerCorrelation)
        {
            _counter.TryRemove(key, out _);
            return Fail(cmd, new ToolError(
                "AGENT_LOOP_DETECTED",
                $"Tool '{cmd.ToolNamespace}.{cmd.ToolName}' called {count} times " +
                $"for correlation {cmd.CorrelationId}. Circuit open — agent loop suspected.",
                429));
        }

        TResponse result;
        try
        {
            result = await next();
        }
        finally
        {
            // Prune at threshold to prevent unbounded memory growth in long-running processes.
            if (count >= _options.MaxCallsPerCorrelation)
                _counter.TryRemove(key, out _);
        }

        return result;
    }

    private static TResponse Fail(IExecuteToolCommand cmd, ToolError error)
    {
        if (typeof(TResponse).IsGenericType &&
            typeof(TResponse).GetGenericTypeDefinition() == typeof(ToolResponse<>))
        {
            var outputType = typeof(TResponse).GetGenericArguments()[0];
            var method = typeof(ToolResponse<>)
                .MakeGenericType(outputType)
                .GetMethod(nameof(ToolResponse<object>.Fail),
                    [typeof(Guid), typeof(ToolError)])!;
            return (TResponse)method.Invoke(null, [cmd.CorrelationId, error])!;
        }

        throw new InvalidOperationException(error.Description);
    }
}

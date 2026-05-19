namespace ToolEngine.Application.Behaviors;

using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Options;
using ToolEngine.Application.Abstractions;
using ToolEngine.Application.Telemetry;
using ToolEngine.Core.Abstractions.Common;
using ToolEngine.Core.Domain.Constants;
using ToolEngine.Core.Domain.Contracts;

/// <summary>
/// Detects agent-driven tool call loops within a single correlation context.
/// A correlation represents one agent turn; if the same tool is invoked more than
/// MaxCallsPerCorrelation times the circuit opens and returns an error.
///
/// State is stored in ICacheProvider:
///   - Memory  (default): in-process, correct for single-pod.
///   - Redis   ("Cache:Provider": "redis"): distributed, correct for multi-pod.
///
/// Key pattern: "loop:{correlationId}:{namespace}.{name}"
/// TTL:         <see cref="ServiceLimits.LoopDetectionTtlMinutes"/> minutes (one agent turn lifetime).
/// </summary>
public sealed class LoopDetectionOptions
{
    public int MaxCallsPerCorrelation { get; set; } = ServiceLimits.LoopDetectionMaxCallsPerCorrelation;
}

public sealed class LoopDetectionBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private static readonly TimeSpan Ttl =
        TimeSpan.FromMinutes(ServiceLimits.LoopDetectionTtlMinutes);

    private readonly ICacheProvider        _cache;
    private readonly LoopDetectionOptions  _options;

    public LoopDetectionBehavior(ICacheProvider cache, IOptions<LoopDetectionOptions> options)
    {
        _cache   = cache;
        _options = options.Value;
    }

    public async Task<TResponse> Handle(
        TRequest                          request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken                 ct)
    {
        if (request is not IExecuteToolCommand cmd)
            return await next();

        var key   = $"loop:{cmd.CorrelationId}:{cmd.ToolNamespace}.{cmd.ToolName}";
        var count = await _cache.IncrementAsync(key, Ttl, ct);

        if (count > _options.MaxCallsPerCorrelation)
        {
            await _cache.RemoveAsync(key, ct);
            // Emit a metric so loop-detection events are visible in dashboards
            // and can trigger alerts before cost accumulates further.
            ToolEngineTelemetry.LoopDetectionTriggers.Add(1,
                new TagList
                {
                    { "tool.fullName", $"{cmd.ToolNamespace}.{cmd.ToolName}" },
                    { "tenant.id",     cmd.TenantId }
                });
            return Fail(cmd, new ToolError(
                ErrorCodes.AgentLoopDetected,
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
            if (count >= _options.MaxCallsPerCorrelation)
                await _cache.RemoveAsync(key, ct);
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
                    [typeof(Guid), typeof(ToolError), typeof(ToolUsageMetrics)])!;
            return (TResponse)method.Invoke(null, [cmd.CorrelationId, error, null])!;
        }

        throw new InvalidOperationException(error.Description);
    }
}

using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using ToolEngine.Application.Abstractions;
using ToolEngine.Core.Abstractions.Cache;
using ToolEngine.Core.Domain.Contracts;

namespace ToolEngine.Application.Behaviors;

/// <summary>
/// Behavior 5 of 8 — F4: Distributed loop detection.
/// Increments a TTL-keyed counter per (tenantId, toolFullName, correlationId).
/// A tool invoked more than MaxInvocationsPerCorrelation times within the TTL window
/// is considered a loop — returns 429 LOOP_DETECTED.
/// Uses ICacheProvider.IncrementAsync — safe across replicas.
/// </summary>
public sealed class LoopDetectionBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest  : notnull
    where TResponse : IToolResponse
{
    private const int MaxInvocations = 5;
    private static readonly TimeSpan DetectionWindow = TimeSpan.FromMinutes(5);

    private readonly ICacheProvider _cache;
    private readonly ILogger<LoopDetectionBehavior<TRequest, TResponse>> _log;

    public LoopDetectionBehavior(
        ICacheProvider cache,
        ILogger<LoopDetectionBehavior<TRequest, TResponse>> log)
    {
        _cache = cache;
        _log   = log;
    }

    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (request is not IExecuteToolCommand cmd)
            return await next();

        var key   = $"loop:{cmd.FullName}:{cmd.CorrelationId}";
        var count = await _cache.IncrementAsync(key, 1, DetectionWindow, ct);

        if (count > MaxInvocations)
        {
            _log.LogError(
                "Loop detected: tool '{FullName}' invoked {Count} times " +
                "within {Window} with correlation '{CorrelationId}'.",
                cmd.FullName, count, DetectionWindow, cmd.CorrelationId);

            return Fail(cmd, new ToolError(429, "LOOP_DETECTED",
                $"Tool '{cmd.FullName}' has been invoked {count} times within {DetectionWindow.TotalMinutes} minutes " +
                $"for correlation '{cmd.CorrelationId}'. Possible infinite loop — request blocked."));
        }

        return await next();
    }

    private static TResponse Fail(IExecuteToolCommand cmd, ToolError error) =>
        (TResponse)(object)ToolResponse<JsonElement>.Fail(cmd.CorrelationId, error);
}

namespace ToolEngine.Tools.Executor;

using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ToolEngine.Core.Domain.Common;
using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Core.Domain.Enums;
using ToolEngine.Tools.Abstractions.Base;
using ToolEngine.Tools.Abstractions.Interfaces;
using ToolEngine.Tools.Registry;

public sealed class ToolExecutor : IToolExecutor
{
    private readonly IToolRegistry    _registry;
    private readonly IServiceProvider _services;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public ToolExecutor(IToolRegistry registry, IServiceProvider services)
    {
        _registry = registry;
        _services = services;
    }

    public async Task<ToolResponse<TOutput>> ExecuteAsync<TInput, TOutput>(
        ToolRequest<TInput> request,
        CancellationToken   ct = default)
    {
        // Use FullName ("namespace.name") for registry lookup — never the bare ToolName alone.
        var resolve = _registry.Resolve(request.FullName, request.ToolVersion, request.TenantId);

        if (resolve.IsFailure)
            return ToolResponse<TOutput>.Fail(
                request.CorrelationId,
                ToolError.FromError(resolve.Error, 404));

        var handlerObj = _services.GetService(resolve.Value.HandlerType);

        if (handlerObj is null)
            return ToolResponse<TOutput>.Fail(
                request.CorrelationId,
                ToolError.Internal(
                    $"Handler for '{request.FullName}@{request.ToolVersion}' " +
                    $"is not registered in the DI container."));

        // Fast path: caller's TInput/TOutput match the handler's generics exactly.
        if (handlerObj is IToolHandler<TInput, TOutput> typedHandler)
            return await typedHandler.ExecuteAsync(request, ct);

        // Bridge path: JSON boundary types — deserialize input, invoke typed handler,
        // serialize output back. Used by CLI and REST API hosts.
        if (typeof(TInput) == typeof(JsonElement) && typeof(TOutput) == typeof(JsonElement)
            && request is ToolRequest<JsonElement> jsonRequest)
        {
            return await ExecuteWithJsonBridgeAsync<TOutput>(
                resolve.Value.HandlerType, handlerObj, jsonRequest, ct);
        }

        return ToolResponse<TOutput>.Fail(
            request.CorrelationId,
            ToolError.Internal(
                $"Type mismatch: handler '{resolve.Value.HandlerType.Name}' cannot " +
                $"accept input type '{typeof(TInput).Name}'."));
    }

    private async Task<ToolResponse<TOutput>> ExecuteWithJsonBridgeAsync<TOutput>(
        Type                    handlerType,
        object                  handler,
        ToolRequest<JsonElement> jsonRequest,
        CancellationToken       ct)
    {
        // Discover the handler's concrete IToolHandler<ActualInput, ActualOutput>.
        var handlerInterface = handlerType.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType
                && i.GetGenericTypeDefinition() == typeof(IToolHandler<,>));

        if (handlerInterface is null)
            return ToolResponse<TOutput>.Fail(jsonRequest.CorrelationId,
                ToolError.Internal($"{handlerType.Name} does not implement IToolHandler<,>."));

        var actualInputType  = handlerInterface.GetGenericArguments()[0];
        var actualOutputType = handlerInterface.GetGenericArguments()[1];

        // Deserialize JsonElement → actual input type.
        object? deserializedInput;
        try
        {
            deserializedInput = JsonSerializer.Deserialize(
                jsonRequest.Input, actualInputType, _jsonOptions);
        }
        catch (Exception ex)
        {
            return ToolResponse<TOutput>.Fail(jsonRequest.CorrelationId,
                ToolError.FromError(
                    Error.Validation($"Input deserialization failed: {ex.Message}"), 400));
        }

        if (deserializedInput is null)
            return ToolResponse<TOutput>.Fail(jsonRequest.CorrelationId,
                ToolError.FromError(Error.Validation("Input deserialized to null."), 400));

        // Build ToolRequest<ActualInput> — forward ALL fields including Phase A additions.
        var typedRequestType = typeof(ToolRequest<>).MakeGenericType(actualInputType);
        var typedRequest = Activator.CreateInstance(
            typedRequestType,
            jsonRequest.CorrelationId,
            jsonRequest.TenantId,
            jsonRequest.ToolName,
            jsonRequest.ToolVersion,
            deserializedInput,
            jsonRequest.Mode,
            jsonRequest.Streaming,
            jsonRequest.UserId,
            jsonRequest.Metadata,
            jsonRequest.MaxResponseTokens,
            jsonRequest.ResponseFormat,
            jsonRequest.ToolNamespace);

        // Invoke handler.ExecuteAsync(ToolRequest<ActualInput>, CancellationToken).
        var executeMethod = handlerInterface.GetMethod(nameof(IToolHandler<object, object>.ExecuteAsync))!;
        var task = (Task)executeMethod.Invoke(handler, [typedRequest, ct])!;
        await task.ConfigureAwait(false);

        // Unbox Task<ToolResponse<ActualOutput>>.Result via reflection.
        var boxedResult = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var resultType  = boxedResult.GetType();

        var isSuccess = (bool)resultType
            .GetProperty(nameof(ToolResponse<object>.Success))!
            .GetValue(boxedResult)!;

        if (!isSuccess)
        {
            var toolError = (ToolError)resultType
                .GetProperty(nameof(ToolResponse<object>.Error))!
                .GetValue(boxedResult)!;
            return ToolResponse<TOutput>.Fail(jsonRequest.CorrelationId, toolError);
        }

        var data    = resultType.GetProperty(nameof(ToolResponse<object>.Data))!.GetValue(boxedResult);
        var metrics = (ToolUsageMetrics)resultType
            .GetProperty(nameof(ToolResponse<object>.Metrics))!
            .GetValue(boxedResult)!;

        // Serialize ActualOutput → JsonElement, then cast to TOutput.
        var jsonElement = JsonSerializer.SerializeToElement(data, _jsonOptions);
        return ToolResponse<TOutput>.Ok(
            jsonRequest.CorrelationId, (TOutput)(object)jsonElement, metrics);
    }
}

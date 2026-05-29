using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Core.Domain.Enums;
using ToolEngine.Tools.Abstractions.Interfaces;
using ToolEngine.Tools.Abstractions.Models;

namespace ToolEngine.Tools.Executor;

// CRITICAL — M1: ToolExecutor MUST use IServiceScopeFactory + CreateAsyncScope + await using.
// Tool handlers may depend on scoped services (IUnitOfWork, DbContext).
// Resolving from root IServiceProvider causes captive dependency and throws at runtime.
public sealed class ToolExecutor : IToolExecutor
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IToolRegistry        _registry;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        // Accept both string enum names ("ManagementConsulting") and integer values (0).
        // LLMs generate string names from JSON Schema enum definitions; integer fallback
        // preserves compatibility with direct API callers and existing serialised data.
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    // Cached MethodInfo for BuildTypedRequest<T> — used to construct ToolRequest<TActualInput>
    // without Activator.CreateInstance (which has fragile type-binder behaviour for value types).
    private static readonly MethodInfo s_buildRequestMethod =
        typeof(ToolExecutor)
            .GetMethod(nameof(BuildTypedRequest), BindingFlags.NonPublic | BindingFlags.Static)!;

    public ToolExecutor(IServiceScopeFactory scopeFactory, IToolRegistry registry)
    {
        _scopeFactory = scopeFactory;
        _registry     = registry;
    }

    public async Task<ToolResponse<TOutput>> ExecuteAsync<TInput, TOutput>(
        ToolRequest<TInput> request, CancellationToken ct = default)
    {
        // M1: Create a new DI scope — scoped services (IUnitOfWork) are safe within this scope
        await using var scope = _scopeFactory.CreateAsyncScope();

        var resolveResult = _registry.Resolve(request.FullName, request.ToolVersion);
        if (resolveResult.IsFailure)
            return ToolResponse<TOutput>.Fail(request.CorrelationId,
                ToolError.FromError(resolveResult.Error, 404));

        var descriptor  = resolveResult.Value;
        var handlerType = descriptor.HandlerType;

        // Resolve the handler from the scoped container
        var handler = scope.ServiceProvider.GetService(handlerType);
        if (handler is null)
            return ToolResponse<TOutput>.Fail(request.CorrelationId,
                ToolError.NotFound($"Handler for tool '{request.FullName}' is not registered in DI."));

        // Locate ExecuteAsync by structural match.
        // The handler's actual TInput (e.g. InitiatePaymentInput) differs from the caller's
        // TInput, which is always JsonElement on the MediatR / API path.
        // Searching by exact parameter type typeof(ToolRequest<TInput>) always fails on the API
        // path — instead find by method shape and extract the real input type from it.
        var executeMethod = handlerType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(m =>
                m.Name == "ExecuteAsync"
                && m.GetParameters() is { Length: 2 } ps
                && ps[0].ParameterType.IsGenericType
                && ps[0].ParameterType.GetGenericTypeDefinition() == typeof(ToolRequest<>)
                && ps[1].ParameterType == typeof(CancellationToken));

        if (executeMethod is null)
            return ToolResponse<TOutput>.Fail(request.CorrelationId,
                ToolError.Internal($"Handler '{handlerType.Name}' has no ExecuteAsync method."));

        // Extract the actual input type the handler expects (e.g. InitiatePaymentInput)
        var actualInputType = executeMethod.GetParameters()[0]
            .ParameterType.GetGenericArguments()[0];

        // Deserialise the input to the handler's actual input type.
        // On the API path TInput = JsonElement; on a typed path we round-trip via JSON.
        object? handlerInputObj;
        if (request.Input is JsonElement jsonIn)
        {
            handlerInputObj = jsonIn.Deserialize(actualInputType, _jsonOptions);
            if (handlerInputObj is null)
                return ToolResponse<TOutput>.Fail(request.CorrelationId,
                    ToolError.Validation($"Cannot deserialise input to {actualInputType.Name}."));
        }
        else if (request.Input is not null && actualInputType.IsInstanceOfType(request.Input))
        {
            handlerInputObj = request.Input;
        }
        else
        {
            var serialised = JsonSerializer.SerializeToElement(request.Input, _jsonOptions);
            handlerInputObj = serialised.Deserialize(actualInputType, _jsonOptions);
            if (handlerInputObj is null)
                return ToolResponse<TOutput>.Fail(request.CorrelationId,
                    ToolError.Validation($"Cannot convert input to {actualInputType.Name}."));
        }

        // Build ToolRequest<TActualInput> using a private generic helper invoked via
        // MakeGenericMethod. This avoids Activator.CreateInstance whose default binder
        // has fragile behaviour when matching value-type params (enums, bool, int) through
        // a params object[] array. The helper uses a normal C# constructor call, which is
        // reliable regardless of parameter arity or nullability.
        object? handlerRequestObj;
        try
        {
            var buildMethod = s_buildRequestMethod.MakeGenericMethod(actualInputType);
            handlerRequestObj = buildMethod.Invoke(null, [
                request.CorrelationId,
                request.ToolName,
                request.ToolVersion,
                handlerInputObj,
                request.Mode,
                request.Streaming,
                request.UserId,
                request.Metadata,
                request.MaxResponseTokens,
                request.ResponseFormat,
                request.ToolNamespace]);
        }
        catch (Exception ex)
        {
            var inner = ex is TargetInvocationException tie ? tie.InnerException ?? ex : ex;
            return ToolResponse<TOutput>.Fail(request.CorrelationId,
                ToolError.Internal(
                    $"Failed to build ToolRequest<{actualInputType.Name}>: {inner.Message}"));
        }

        if (handlerRequestObj is null)
            return ToolResponse<TOutput>.Fail(request.CorrelationId,
                ToolError.Internal($"BuildTypedRequest returned null for {actualInputType.Name}."));

        // Invoke the handler and await the Task it returns.
        Task task;
        try
        {
            var invokeResult = executeMethod.Invoke(handler, [handlerRequestObj, ct]);
            if (invokeResult is not Task t)
                return ToolResponse<TOutput>.Fail(request.CorrelationId,
                    ToolError.Internal(
                        $"Handler '{handlerType.Name}.ExecuteAsync' returned null or a non-Task value."));
            task = t;
        }
        catch (Exception ex)
        {
            var inner = ex is TargetInvocationException tie ? tie.InnerException ?? ex : ex;
            return ToolResponse<TOutput>.Fail(request.CorrelationId,
                ToolError.Internal($"Handler '{handlerType.Name}' threw during invocation: {inner.Message}"));
        }

        try
        {
            await task;
        }
        catch (Exception ex)
        {
            return ToolResponse<TOutput>.Fail(request.CorrelationId,
                ToolError.Internal($"Handler '{handlerType.Name}' threw during execution: {ex.Message}"));
        }

        // Unbox Task<ToolResponse<THandlerOutput>> → ToolResponse<TOutput>
        var resultProp = task.GetType().GetProperty("Result");
        if (resultProp is null)
            return ToolResponse<TOutput>.Fail(request.CorrelationId,
                ToolError.Internal(
                    $"Cannot read Task.Result from handler '{handlerType.Name}' — unexpected Task type."));

        var boxed = resultProp.GetValue(task);
        if (boxed is null)
            return ToolResponse<TOutput>.Fail(request.CorrelationId,
                ToolError.Internal($"Handler '{handlerType.Name}' returned a null ToolResponse."));

        var rType       = boxed.GetType();
        var successProp = rType.GetProperty(nameof(ToolResponse<object>.Success));
        if (successProp is null)
            return ToolResponse<TOutput>.Fail(request.CorrelationId,
                ToolError.Internal("ToolResponse.Success property not found — type contract broken."));

        var success = (bool)successProp.GetValue(boxed)!;

        if (!success)
        {
            var errProp = rType.GetProperty(nameof(ToolResponse<object>.Error));
            var err     = errProp?.GetValue(boxed) as ToolError
                          ?? ToolError.Internal($"Handler '{handlerType.Name}' returned failure with no error detail.");
            return ToolResponse<TOutput>.Fail(request.CorrelationId, err);
        }

        var data        = rType.GetProperty(nameof(ToolResponse<object>.Data))?.GetValue(boxed);
        var metricsProp = rType.GetProperty(nameof(ToolResponse<object>.Metrics));
        var metrics     = metricsProp?.GetValue(boxed) as ToolUsageMetrics
                          ?? new ToolUsageMetrics(0, 0);

        // If caller wants JsonElement output (API path), serialise; else cast directly.
        // Guard: data should never be null here (success path), but a null would produce
        // JsonValueKind.Null (safe). An existing JsonElement is returned as-is to avoid
        // double-serialisation (some handlers return JsonElement directly).
        TOutput output;
        if (typeof(TOutput) == typeof(JsonElement))
        {
            var element = data is JsonElement alreadyJson
                ? alreadyJson
                : JsonSerializer.SerializeToElement(data, _jsonOptions);
            output = (TOutput)(object)element;
        }
        else
        {
            output = (TOutput)data!;
        }

        return ToolResponse<TOutput>.Ok(request.CorrelationId, output, metrics);
    }

    /// <summary>
    /// Private generic helper — invoked via MakeGenericMethod(actualInputType).
    /// Constructs a properly-typed ToolRequest&lt;T&gt; from the boxed <paramref name="input"/>
    /// and the scalar fields of the originating request.
    /// Using a real constructor call here (vs Activator.CreateInstance) avoids the reflection
    /// binder's fragile type-coercion for value types boxed inside a params object[] array.
    /// </summary>
    private static ToolRequest<T> BuildTypedRequest<T>(
        Guid correlationId, string toolName, string toolVersion,
        object input, ExecutionMode mode, bool streaming, string? userId,
        Dictionary<string, string>? metadata, int maxResponseTokens,
        string? responseFormat, string? toolNamespace) =>
        new(correlationId, toolName, toolVersion, (T)input,
            mode, streaming, userId, metadata, maxResponseTokens, responseFormat, toolNamespace);
}
